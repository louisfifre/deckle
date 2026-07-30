"""Verify ACX-0022 NPZ and ONNX Runtime adapter artifacts byte-for-byte."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
from typing import Any

import numpy as np


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    subparsers = parser.add_subparsers(dest="command", required=True)

    npz = subparsers.add_parser("npz")
    npz.add_argument("--manifest", type=Path, required=True)
    npz.add_argument("--control", type=Path, required=True)
    npz.add_argument("--sentinel", type=Path, required=True)

    adapter = subparsers.add_parser("adapter")
    adapter.add_argument("--npz", type=Path, required=True)
    adapter.add_argument("--adapter", type=Path, required=True)
    adapter.add_argument("--adapter-version", type=int, required=True)
    adapter.add_argument("--model-version", type=int, required=True)
    return parser.parse_args()


def sha256_bytes(value: np.ndarray) -> str:
    return hashlib.sha256(value.tobytes(order="C")).hexdigest()


def verify_npz(
    path: Path,
    expected: list[dict[str, Any]],
    *,
    require_positive_zero: bool,
    require_non_zero: bool,
) -> dict[str, Any]:
    with np.load(path) as archive:
        actual_names = list(archive.files)
        expected_names = [tensor["name"] for tensor in expected]
        if actual_names != expected_names:
            raise ValueError("NPZ tensor names or order do not match the manifest")

        total_raw_bytes = 0
        for tensor in expected:
            value = archive[tensor["name"]]
            if list(value.shape) != tensor["shape"]:
                raise ValueError(f"shape mismatch: {tensor['name']}")
            if str(value.dtype) != tensor["dtype"]:
                raise ValueError(f"dtype mismatch: {tensor['name']}")
            if value.nbytes != tensor["raw_bytes"]:
                raise ValueError(f"raw byte-count mismatch: {tensor['name']}")
            if sha256_bytes(value) != tensor["raw_sha256"]:
                raise ValueError(f"raw hash mismatch: {tensor['name']}")
            if not np.isfinite(value).all():
                raise ValueError(f"non-finite tensor: {tensor['name']}")
            if require_positive_zero and (np.any(value != 0) or np.signbit(value).any()):
                raise ValueError(f"control is not positive zero: {tensor['name']}")
            total_raw_bytes += value.nbytes

        any_non_zero = any(np.any(archive[name] != 0) for name in actual_names)
        if require_non_zero and not any_non_zero:
            raise ValueError("sentinel contains no non-zero value")

    return {
        "path": str(path.resolve()),
        "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
        "bytes": path.stat().st_size,
        "tensor_count": len(expected),
        "raw_tensor_bytes": total_raw_bytes,
        "positive_zero": require_positive_zero,
        "contains_non_zero": any_non_zero,
    }


def verify_npz_command(args: argparse.Namespace) -> dict[str, Any]:
    manifest = json.loads(args.manifest.read_text(encoding="utf-8"))
    if manifest.get("record_type") != "acx0022_synthetic_lora":
        raise ValueError("unexpected synthetic manifest record type")
    control = verify_npz(
        args.control,
        manifest["control"],
        require_positive_zero=True,
        require_non_zero=False,
    )
    sentinel = verify_npz(
        args.sentinel,
        manifest["sentinel"],
        require_positive_zero=False,
        require_non_zero=True,
    )
    return {
        "record_type": "acx0022_npz_verification",
        "valid": True,
        "manifest_sha256": hashlib.sha256(args.manifest.read_bytes()).hexdigest(),
        "control": control,
        "sentinel": sentinel,
        "claim_boundary": (
            "Synthetic NPZ identity only; no adapter conversion, model export, inference, "
            "DirectML, latency, memory, quality, or production claim."
        ),
    }


def verify_adapter_command(args: argparse.Namespace) -> dict[str, Any]:
    import onnxruntime as ort

    adapter = ort.AdapterFormat.read_adapter(str(args.adapter.resolve()))
    if adapter.get_format_version() != 1:
        raise ValueError("adapter format version mismatch")
    if adapter.get_adapter_version() != args.adapter_version:
        raise ValueError("adapter version mismatch")
    if adapter.get_model_version() != args.model_version:
        raise ValueError("model version mismatch")

    with np.load(args.npz) as archive:
        parameters = adapter.get_parameters()
        if set(parameters) != set(archive.files):
            raise ValueError("adapter tensor names do not match NPZ")
        total_raw_bytes = 0
        for name in archive.files:
            expected = archive[name]
            actual = parameters[name].numpy()
            if actual.dtype != expected.dtype:
                raise ValueError(f"adapter dtype mismatch: {name}")
            if actual.shape != expected.shape:
                raise ValueError(f"adapter shape mismatch: {name}")
            if not np.array_equal(actual, expected):
                raise ValueError(f"adapter values mismatch: {name}")
            total_raw_bytes += actual.nbytes

    return {
        "record_type": "acx0022_adapter_verification",
        "valid": True,
        "format_version": adapter.get_format_version(),
        "adapter_version": adapter.get_adapter_version(),
        "model_version": adapter.get_model_version(),
        "tensor_count": len(parameters),
        "raw_tensor_bytes": total_raw_bytes,
        "npz_sha256": hashlib.sha256(args.npz.read_bytes()).hexdigest(),
        "adapter_sha256": hashlib.sha256(args.adapter.read_bytes()).hexdigest(),
        "adapter_bytes": args.adapter.stat().st_size,
        "claim_boundary": (
            "ONNX Runtime 1.23 adapter serialization identity only; no model load, inference, "
            "DirectML, latency, memory, quality, or production claim."
        ),
    }


def main() -> int:
    args = parse_args()
    if args.command == "npz":
        result = verify_npz_command(args)
    elif args.command == "adapter":
        result = verify_adapter_command(args)
    else:
        raise AssertionError(f"unexpected command: {args.command}")
    print(json.dumps(result, indent=2, ensure_ascii=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
