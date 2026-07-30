"""Generate frozen synthetic LoRA inputs for the ACX-0022 compatibility probe."""

from __future__ import annotations

import argparse
import hashlib
import json
from collections import OrderedDict
from pathlib import Path
from types import SimpleNamespace
from typing import Any

import numpy as np


RANK = 8
ALPHA = 16
SCALE = ALPHA / RANK
SEED = 20260730
AMPLITUDE = 0.01
TARGETS = ("q_proj", "v_proj")
FACTORS = ("lora_A", "lora_B")
DTYPE = "float16"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    subparsers = parser.add_subparsers(dest="command", required=True)

    subparsers.add_parser("self-test")

    generate = subparsers.add_parser("generate")
    generate.add_argument("--source", type=Path, required=True)
    generate.add_argument("--output", type=Path, required=True)
    generate.add_argument("--repository", required=True)
    generate.add_argument("--revision", required=True)
    return parser.parse_args()


def tensor_contract(config: Any) -> list[dict[str, Any]]:
    layers = positive_int(config.num_hidden_layers, "num_hidden_layers")
    input_size = positive_int(config.hidden_size, "hidden_size")
    head_size = positive_int(config.head_dim, "head_dim")
    query_output = positive_int(config.num_attention_heads, "num_attention_heads") * head_size
    value_output = positive_int(config.num_key_value_heads, "num_key_value_heads") * head_size

    tensors: list[dict[str, Any]] = []
    for layer in range(layers):
        for projection, output_size in (
            ("q_proj", query_output),
            ("v_proj", value_output),
        ):
            for factor, shape in (
                ("lora_A", (input_size, RANK)),
                ("lora_B", (RANK, output_size)),
            ):
                node_name = f"/model/layers.{layer}/attn/{projection}/{factor}/MatMul"
                tensors.append(
                    {
                        "layer": layer,
                        "projection": projection,
                        "factor": factor,
                        "node_name": node_name,
                        "initializer_name": node_name[1:].replace("/", ".") + ".weight",
                        "shape": shape,
                        "peft_name": (
                            "base_model.model.model.layers."
                            f"{layer}.self_attn.{projection}.{factor}.default.weight"
                        ),
                    }
                )
    return tensors


def positive_int(value: Any, name: str) -> int:
    parsed = int(value)
    if parsed <= 0:
        raise ValueError(f"{name} must be positive")
    return parsed


def runtime_values(
    contract: list[dict[str, Any]],
    *,
    sentinel: bool,
) -> OrderedDict[str, np.ndarray]:
    values: OrderedDict[str, np.ndarray] = OrderedDict()
    generator = np.random.Generator(np.random.PCG64(SEED))
    for tensor in contract:
        shape = tensor["shape"]
        if sentinel:
            value = (
                generator.standard_normal(shape) * np.float64(AMPLITUDE)
            ).astype(np.float16)
        else:
            value = np.zeros(shape, dtype=np.float16)
        if value.dtype != np.float16 or value.shape != shape or not np.isfinite(value).all():
            raise ValueError(f"invalid generated tensor {tensor['initializer_name']}")
        if not sentinel and (np.signbit(value).any() or np.any(value != 0)):
            raise ValueError(f"control tensor is not positive zero: {tensor['initializer_name']}")
        values[tensor["initializer_name"]] = value
    return values


def peft_values(
    contract: list[dict[str, Any]],
    values: OrderedDict[str, np.ndarray],
) -> dict[str, np.ndarray]:
    transformed: dict[str, np.ndarray] = {}
    for tensor in contract:
        value = values[tensor["initializer_name"]]
        if tensor["factor"] == "lora_A":
            peft_value = value.T
        else:
            peft_value = (value.astype(np.float32) / SCALE).T
        transformed[tensor["peft_name"]] = np.ascontiguousarray(peft_value)
    return transformed


def write_npz(path: Path, values: OrderedDict[str, np.ndarray]) -> None:
    path.parent.mkdir(parents=True, exist_ok=False)
    np.savez(path, **values)


def tensor_manifest(
    contract: list[dict[str, Any]],
    values: OrderedDict[str, np.ndarray],
) -> list[dict[str, Any]]:
    result: list[dict[str, Any]] = []
    for tensor in contract:
        value = values[tensor["initializer_name"]]
        result.append(
            {
                "name": tensor["initializer_name"],
                "node_name": tensor["node_name"],
                "shape": list(value.shape),
                "dtype": DTYPE,
                "raw_bytes": value.nbytes,
                "raw_sha256": hashlib.sha256(value.tobytes(order="C")).hexdigest(),
            }
        )
    return result


def write_peft_adapter(
    model: Any,
    directory: Path,
    transformed: dict[str, np.ndarray],
) -> None:
    import torch

    parameters = dict(model.named_parameters())
    actual_names = {name for name in parameters if ".lora_" in name}
    expected_names = set(transformed)
    if actual_names != expected_names:
        missing = sorted(expected_names - actual_names)
        extra = sorted(actual_names - expected_names)
        raise ValueError(f"PEFT parameter mismatch; missing={missing}, extra={extra}")

    with torch.no_grad():
        for name, value in transformed.items():
            parameter = parameters[name]
            source = torch.from_numpy(value.copy()).to(
                device=parameter.device,
                dtype=parameter.dtype,
            )
            if tuple(parameter.shape) != tuple(source.shape):
                raise ValueError(
                    f"PEFT shape mismatch for {name}: {tuple(parameter.shape)} != {tuple(source.shape)}"
                )
            parameter.copy_(source)

    model.save_pretrained(directory, safe_serialization=True)


def generate(args: argparse.Namespace) -> None:
    import torch
    from peft import LoraConfig, TaskType, get_peft_model
    from transformers import AutoConfig, AutoModelForCausalLM

    source = args.source.resolve(strict=True)
    output = args.output.resolve()
    if output.exists():
        raise FileExistsError(f"output already exists: {output}")
    if len(args.revision) != 40 or any(character not in "0123456789abcdef" for character in args.revision):
        raise ValueError("revision must be a lowercase 40-character hexadecimal commit")

    config = AutoConfig.from_pretrained(
        source,
        local_files_only=True,
        trust_remote_code=False,
    )
    contract = tensor_contract(config)
    control = runtime_values(contract, sentinel=False)
    sentinel = runtime_values(contract, sentinel=True)

    base = AutoModelForCausalLM.from_pretrained(
        source,
        local_files_only=True,
        trust_remote_code=False,
        torch_dtype=torch.float16,
        low_cpu_mem_usage=True,
    )
    model = get_peft_model(
        base,
        LoraConfig(
            task_type=TaskType.CAUSAL_LM,
            r=RANK,
            lora_alpha=ALPHA,
            lora_dropout=0.0,
            bias="none",
            target_modules=list(TARGETS),
        ),
    )

    output.mkdir(parents=True, exist_ok=False)
    write_npz(output / "control-zero" / "parameters.npz", control)
    write_npz(output / "sentinel-seeded" / "parameters.npz", sentinel)
    write_peft_adapter(model, output / "control-zero" / "peft", peft_values(contract, control))
    write_peft_adapter(model, output / "sentinel-seeded" / "peft", peft_values(contract, sentinel))

    manifest = {
        "record_type": "acx0022_synthetic_lora",
        "base_repository": args.repository,
        "base_revision": args.revision,
        "source_path": str(source),
        "layer_count": int(config.num_hidden_layers),
        "input_size": int(config.hidden_size),
        "query_output_size": int(config.num_attention_heads) * int(config.head_dim),
        "value_output_size": int(config.num_key_value_heads) * int(config.head_dim),
        "targets": list(TARGETS),
        "rank": RANK,
        "alpha": ALPHA,
        "scale": SCALE,
        "dropout": 0.0,
        "bias": "none",
        "dtype": DTYPE,
        "seed": SEED,
        "amplitude": AMPLITUDE,
        "tensor_order": "layer_ascending,q_proj,v_proj,lora_A,lora_B",
        "array_order": "C",
        "adapter_format_version": 1,
        "model_version": 0,
        "adapter_version": 1,
        "tensor_count": len(contract),
        "control": tensor_manifest(contract, control),
        "sentinel": tensor_manifest(contract, sentinel),
        "claim_boundary": (
            "Synthetic tensor and PEFT artifact generation only; no adapter conversion, "
            "model export, inference, DirectML, latency, memory, quality, or production claim."
        ),
    }
    (output / "manifest.json").write_text(
        json.dumps(manifest, indent=2, ensure_ascii=True) + "\n",
        encoding="utf-8",
        newline="\n",
    )


def self_test() -> None:
    config = SimpleNamespace(
        num_hidden_layers=2,
        hidden_size=1024,
        head_dim=128,
        num_attention_heads=16,
        num_key_value_heads=8,
    )
    contract = tensor_contract(config)
    if len(contract) != 8:
        raise AssertionError("unexpected contract size")
    if contract[0]["node_name"] != "/model/layers.0/attn/q_proj/lora_A/MatMul":
        raise AssertionError("unexpected first node")
    if contract[-1]["shape"] != (RANK, 1024):
        raise AssertionError("unexpected last shape")
    first = runtime_values(contract, sentinel=True)
    second = runtime_values(contract, sentinel=True)
    if any(not np.array_equal(first[name], second[name]) for name in first):
        raise AssertionError("sentinel stream is not deterministic")
    control = runtime_values(contract, sentinel=False)
    if any(np.signbit(value).any() or np.any(value != 0) for value in control.values()):
        raise AssertionError("control stream is not positive zero")
    print("ACX-0022 generator self-test passed")


def main() -> int:
    args = parse_args()
    if args.command == "self-test":
        self_test()
    elif args.command == "generate":
        generate(args)
    else:
        raise AssertionError(f"unexpected command: {args.command}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
