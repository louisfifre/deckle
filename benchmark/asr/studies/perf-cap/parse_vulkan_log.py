"""Parse un log llama-mtmd-cli capturé avec GGML_VK_PERF_LOGGER=1.

Le log contient plusieurs blocs "Vulkan Timings: ... Total time: ..."
émis par le backend Vulkan à chaque évaluation du graph de calcul.
On distingue quatre types de blocs selon le contenu :

- ``warmup`` : warm-up llama.cpp ou template chat (n petit, mix matvec/matmul)
- ``audio_prefill`` : encoding des tokens audio (ops f16, batch n=1500)
- ``lm_prefill`` : encoding du prompt LM batché (MUL_MAT q4_K avec n>1)
- ``gen`` : génération token par token (MUL_MAT_VEC q4_K/q5_K/q6_K avec n=1)

On agrège les blocs ``gen`` pour produire les métriques de caractérisation :
gen tok/s, RTF, bande passante effective des ops dominantes. Un JSONL row
par invocation est appendu dans ``aggregated.jsonl``.

Le calcul de bande passante effective d'une op matvec : bytes lus par appel
= m * k * bytes_par_param du format de quantization. Diviser par l'avg
us/call donne la bande passante effective sur cette op. Le shader Vulkan
qui sature lit à la bande passante VRAM théorique (800 GB/s sur RX 7900 XT) ;
celui qui ne sature pas révèle son inefficience par cette mesure.
"""

from __future__ import annotations

import argparse
import json
import re
import statistics
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any


# Bytes per parameter of each llama.cpp quantization format, from
# ggml/src/ggml-quants.c (QK_K and super-block sizes). K-quants pack 256
# elements per super-block with hierarchical scales.
BYTES_PER_PARAM: dict[str, float] = {
    "q2_K": 84 / 256,    # 2.625 bits
    "q3_K": 110 / 256,   # 3.4375 bits
    "q4_K": 144 / 256,   # 4.5 bits
    "q5_K": 176 / 256,   # 5.5 bits
    "q6_K": 210 / 256,   # 6.5625 bits
    "q4_0": 18 / 32,     # 4.5 bits (32-element block with an f16 scale)
    "q5_0": 22 / 32,     # 5.5 bits
    "q8_0": 34 / 32,     # 8.5 bits (32-element block with an f16 scale)
    "f16":  2.0,
    "bf16": 2.0,
    "f32":  4.0,
}


# Vulkan op lines as the log prints them:
#   ADD: 79 x 2020.49 us = 159619 us
#   MUL_MAT_VEC q4_K m=32768 n=1 k=5120: 80 x 864.695 us = 69175.6 us (388.011 GFLOPS/s)
#   MUL_MAT_ADD MUL_MAT_VEC q4_K m=5120 n=1 k=32768: 35 x 967.105 us = 33848.7 us (346.952 GFLOPS/s)
#   FLASH_ATTN_EXT dst(128,32,1,1),  q(128,1,32,1),  k(128,512,8,1),  v(128,512,8,1),  m(512,1,1,1): 40 x 66.144 us = 2645.76 us (126.823 GFLOPS/s)
OP_LINE_RE = re.compile(
    r"^(?P<head>[A-Z_][A-Z_0-9 ]*?(?:\s+[a-z][a-zA-Z0-9_]*)?(?:\s+[a-zA-Z()][^:]*?)?)"
    r":\s+(?P<count>\d+)\s+x\s+(?P<avg_us>[\d.]+)\s+us\s+=\s+(?P<total_us>[\d.eE+]+)\s+us"
    r"(?:\s+\((?P<gflops>[\d.eE+]+)\s+GFLOPS/s\))?\s*$"
)


# Quant format and dimensions inside an op head, such as "q4_K m=32768 n=1 k=5120".
MATVEC_DIM_RE = re.compile(
    r"(?P<quant>[bif][fp]?16|[qf]\d+(?:_[KMS])?(?:_[KMSL])?|q\d+_\d+)\s+"
    r"m=(?P<m>\d+)\s+n=(?P<n>\d+)\s+k=(?P<k>\d+)"
)


@dataclass
class OpStat:
    name: str  # op head, e.g. "MUL_MAT_VEC q4_K m=32768 n=1 k=5120"
    quant: str | None = None
    m: int | None = None
    n: int | None = None
    k: int | None = None
    calls_per_block: list[int] = field(default_factory=list)
    avg_us_per_call: list[float] = field(default_factory=list)
    total_us_per_block: list[float] = field(default_factory=list)
    gflops_per_block: list[float] = field(default_factory=list)


@dataclass
class Block:
    ops: list[dict[str, Any]] = field(default_factory=list)
    total_us: float = 0.0


def _parse_blocks(log_text: str) -> list[Block]:
    blocks: list[Block] = []
    current: Block | None = None

    for line in log_text.splitlines():
        if line.strip() == "Vulkan Timings:":
            current = Block()
            continue
        if current is None:
            continue
        m = re.match(r"^Total time:\s+([\d.eE+]+)\s+us\.\s*$", line.strip())
        if m:
            current.total_us = float(m.group(1))
            blocks.append(current)
            current = None
            continue
        op_match = OP_LINE_RE.match(line)
        if op_match:
            head = op_match.group("head").strip()
            op = {
                "head": head,
                "count": int(op_match.group("count")),
                "avg_us": float(op_match.group("avg_us")),
                "total_us": float(op_match.group("total_us")),
                "gflops": float(op_match.group("gflops")) if op_match.group("gflops") else None,
            }
            dim_match = MATVEC_DIM_RE.search(head)
            if dim_match:
                op["quant"] = dim_match.group("quant")
                op["m"] = int(dim_match.group("m"))
                op["n"] = int(dim_match.group("n"))
                op["k"] = int(dim_match.group("k"))
            current.ops.append(op)

    return blocks


def _classify_block(block: Block) -> str:
    """A batched MUL_MAT on f16/bf16 is the audio encoder; on a quantized
    format it is the LM prefill; matvecs with n=1 only are one generated
    token; anything else is warmup.
    """
    has_matvec_n1 = False
    has_matvec_n_gt_1 = False
    has_matmul_f16 = False
    has_matmul_q = False

    for op in block.ops:
        head = op["head"]
        n = op.get("n")
        quant = op.get("quant")
        is_matvec = "MUL_MAT_VEC" in head
        is_matmul = head.startswith("MUL_MAT ") or head == "MUL_MAT"

        if is_matvec and n == 1:
            has_matvec_n1 = True
        if is_matvec and n is not None and n > 1:
            has_matvec_n_gt_1 = True
        if is_matmul and quant in ("f16", "bf16"):
            has_matmul_f16 = True
        if is_matmul and quant and quant.startswith("q"):
            has_matmul_q = True

    if has_matmul_f16 and not has_matmul_q:
        return "audio_prefill"
    if has_matmul_q:
        return "lm_prefill"
    if has_matvec_n1 and not has_matvec_n_gt_1:
        return "gen"
    return "warmup"


def _aggregate_gen_ops(gen_blocks: list[Block]) -> list[OpStat]:
    by_head: dict[str, OpStat] = {}
    for block in gen_blocks:
        for op in block.ops:
            head = op["head"]
            stat = by_head.get(head)
            if stat is None:
                stat = OpStat(name=head)
                stat.quant = op.get("quant")
                stat.m = op.get("m")
                stat.n = op.get("n")
                stat.k = op.get("k")
                by_head[head] = stat
            stat.calls_per_block.append(op["count"])
            stat.avg_us_per_call.append(op["avg_us"])
            stat.total_us_per_block.append(op["total_us"])
            if op.get("gflops") is not None:
                stat.gflops_per_block.append(op["gflops"])
    return list(by_head.values())


def _bandwidth_gb_s(stat: OpStat, mean_us_per_call: float) -> float | None:
    """A matvec reads mostly its weight matrix, m * k elements in the
    quantization format; for n=1 the input and output vectors are
    negligible beside it.
    """
    if stat.quant is None or stat.m is None or stat.k is None:
        return None
    bpp = BYTES_PER_PARAM.get(stat.quant)
    if bpp is None:
        return None
    bytes_per_call = stat.m * stat.k * bpp
    if mean_us_per_call <= 0:
        return None
    # GB/s = bytes / us * 1e6 / 1e9 = bytes / us * 1e-3
    return bytes_per_call / mean_us_per_call / 1e3


def summarize(log_path: Path, config_name: str, audio_duration_s: float,
              model_file: str | None = None,
              model_size_bytes: int | None = None) -> dict[str, Any]:
    """Reads the log only; the caller decides where the summary goes."""
    log_text = log_path.read_text(encoding="utf-8", errors="replace")
    blocks = _parse_blocks(log_text)

    phases: dict[str, dict[str, Any]] = {
        "warmup": {"blocks_count": 0, "total_us": 0.0},
        "audio_prefill": {"blocks_count": 0, "total_us": 0.0},
        "lm_prefill": {"blocks_count": 0, "total_us": 0.0},
        "gen": {"blocks_count": 0, "total_us": 0.0},
    }
    gen_blocks: list[Block] = []
    for block in blocks:
        kind = _classify_block(block)
        phases[kind]["blocks_count"] += 1
        phases[kind]["total_us"] += block.total_us
        if kind == "gen":
            gen_blocks.append(block)

    # One gen block is one generated token.
    gen_total_s = phases["gen"]["total_us"] / 1e6
    if phases["gen"]["blocks_count"] > 0 and gen_total_s > 0:
        phases["gen"]["tok_per_sec"] = phases["gen"]["blocks_count"] / gen_total_s
        phases["gen"]["rtf"] = gen_total_s / audio_duration_s
    else:
        phases["gen"]["tok_per_sec"] = None
        phases["gen"]["rtf"] = None

    ops = _aggregate_gen_ops(gen_blocks)
    gen_ops_rows: list[dict[str, Any]] = []
    for stat in ops:
        if not stat.avg_us_per_call:
            continue
        mean_us = statistics.mean(stat.avg_us_per_call)
        std_us = statistics.stdev(stat.avg_us_per_call) if len(stat.avg_us_per_call) > 1 else 0.0
        mean_calls = statistics.mean(stat.calls_per_block) if stat.calls_per_block else 0
        mean_total_us = statistics.mean(stat.total_us_per_block) if stat.total_us_per_block else 0
        mean_gflops = statistics.mean(stat.gflops_per_block) if stat.gflops_per_block else None
        bw = _bandwidth_gb_s(stat, mean_us)
        share = mean_total_us / (phases["gen"]["total_us"] / phases["gen"]["blocks_count"]) \
                if phases["gen"]["blocks_count"] > 0 else None

        gen_ops_rows.append({
            "op": stat.name,
            "quant": stat.quant,
            "m": stat.m,
            "n": stat.n,
            "k": stat.k,
            "calls_per_token": round(mean_calls, 2),
            "us_per_call_mean": round(mean_us, 2),
            "us_per_call_std": round(std_us, 2),
            "total_us_per_token": round(mean_total_us, 1),
            "share_of_token": round(share, 4) if share is not None else None,
            "gflops": round(mean_gflops, 1) if mean_gflops is not None else None,
            "bandwidth_gb_s": round(bw, 1) if bw is not None else None,
        })

    gen_ops_rows.sort(key=lambda r: r["share_of_token"] or 0, reverse=True)

    summary = {
        "config_name": config_name,
        "model_file": model_file,
        "model_size_bytes": model_size_bytes,
        "audio_duration_s": audio_duration_s,
        "log_path": str(log_path),
        "phases": phases,
        "gen_ops": gen_ops_rows,
    }

    if gen_ops_rows:
        bws_with_values = [r["bandwidth_gb_s"] for r in gen_ops_rows if r["bandwidth_gb_s"]]
        summary["bandwidth_min_gb_s"] = min(bws_with_values) if bws_with_values else None
        summary["bandwidth_max_gb_s"] = max(bws_with_values) if bws_with_values else None
        summary["dominant_op"] = gen_ops_rows[0]["op"]
        summary["dominant_share"] = gen_ops_rows[0]["share_of_token"]

    return summary


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--log", required=True, type=Path, help="Chemin du log llama-mtmd-cli")
    ap.add_argument("--config", required=True, help="Nom du config (slug)")
    ap.add_argument("--audio-duration", type=float, default=12.3,
                    help="Durée audio en secondes (default 12.3 pour sample-bc08abb2.wav)")
    ap.add_argument("--model-file", default=None, help="Nom du fichier GGUF")
    ap.add_argument("--model-size", type=int, default=None, help="Taille du GGUF en octets")
    ap.add_argument("--output", required=True, type=Path,
                    help="JSONL où appendre le row (créé si absent)")
    ap.add_argument("--echo", action="store_true",
                    help="Imprime le summary sur stdout en plus de l'écrire")
    args = ap.parse_args()

    summary = summarize(
        args.log, args.config, args.audio_duration,
        model_file=args.model_file, model_size_bytes=args.model_size,
    )

    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("a", encoding="utf-8") as f:
        f.write(json.dumps(summary, ensure_ascii=False) + "\n")

    print(f"[parse] {args.config} : "
          f"{summary['phases']['gen']['blocks_count']} gen tokens, "
          f"{summary['phases']['gen'].get('tok_per_sec', 0) or 0:.2f} tok/s, "
          f"RTF {summary['phases']['gen'].get('rtf', 0) or 0:.3f}, "
          f"bottleneck {summary.get('bandwidth_min_gb_s', 0) or 0:.0f} GB/s")

    if args.echo:
        print(json.dumps(summary, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
