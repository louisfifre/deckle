#!/usr/bin/env python3
"""Generate and independently verify the frozen ACX-0023 negative adapters."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import tempfile
from typing import Any

import numpy as np


EXPECTED_IDS = (
    "missing-file",
    "truncated-file",
    "wrong-model-version",
    "wrong-target-name",
    "wrong-rank-shape",
    "wrong-dtype",
    "missing-tensor",
    "extra-tensor",
)
FORMAT_VERSION = 1
ADAPTER_VERSION = 1
MODEL_VERSION = 0
WRONG_MODEL_VERSION = 1
EXTRA_TENSOR_NAME = "model.layers.28.attn.q_proj.lora_A.MatMul.weight"


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def identity(path: Path) -> dict[str, object]:
    resolved = path.resolve(strict=True)
    return {
        "path": str(resolved),
        "bytes": resolved.stat().st_size,
        "sha256": sha256(resolved),
    }


def load_npz(path: Path) -> dict[str, np.ndarray]:
    with np.load(path) as archive:
        return {name: archive[name].copy() for name in archive.files}


def read_adapter(path: Path) -> tuple[int, int, dict[str, np.ndarray]]:
    import onnxruntime as ort

    adapter = ort.AdapterFormat.read_adapter(str(path.resolve(strict=True)))
    parameters = {
        name: value.numpy().copy()
        for name, value in adapter.get_parameters().items()
    }
    return adapter.get_adapter_version(), adapter.get_model_version(), parameters


def export_adapter(
    path: Path,
    parameters: dict[str, np.ndarray],
    *,
    model_version: int = MODEL_VERSION,
) -> None:
    import onnxruntime as ort

    adapter = ort.AdapterFormat()
    adapter.set_adapter_version(ADAPTER_VERSION)
    adapter.set_model_version(model_version)
    adapter.set_parameters(
        {
            name: ort.OrtValue.ortvalue_from_numpy(np.ascontiguousarray(value))
            for name, value in parameters.items()
        }
    )
    adapter.export_adapter(str(path.resolve()))


def generate(control_npz: Path, control_adapter: Path, output: Path) -> None:
    if output.exists():
        raise FileExistsError(f"output already exists: {output}")
    control_parameters = load_npz(control_npz.resolve(strict=True))
    names = list(control_parameters)
    if len(names) != 112:
        raise ValueError("the frozen control must contain exactly 112 tensors")
    first_name = names[0]
    last_name = names[-1]
    first = control_parameters[first_name]
    if first.ndim != 2 or first.shape[1] != 8 or first.dtype != np.float16:
        raise ValueError("the first frozen tensor does not expose rank eight float16")

    output.mkdir(parents=True, exist_ok=False)
    control_bytes = control_adapter.resolve(strict=True).read_bytes()
    (output / "truncated-file.onnx_adapter").write_bytes(
        control_bytes[: len(control_bytes) // 2]
    )

    export_adapter(
        output / "wrong-model-version.onnx_adapter",
        control_parameters,
        model_version=WRONG_MODEL_VERSION,
    )

    wrong_target = dict(control_parameters)
    wrong_target[f"{first_name}.wrong_target"] = wrong_target.pop(first_name)
    export_adapter(output / "wrong-target-name.onnx_adapter", wrong_target)

    wrong_rank = dict(control_parameters)
    wrong_rank[first_name] = wrong_rank[first_name][:, :-1].copy()
    export_adapter(output / "wrong-rank-shape.onnx_adapter", wrong_rank)

    wrong_dtype = dict(control_parameters)
    wrong_dtype[first_name] = wrong_dtype[first_name].astype(np.float32)
    export_adapter(output / "wrong-dtype.onnx_adapter", wrong_dtype)

    missing_tensor = dict(control_parameters)
    del missing_tensor[last_name]
    export_adapter(output / "missing-tensor.onnx_adapter", missing_tensor)

    extra_tensor = dict(control_parameters)
    extra_tensor[EXTRA_TENSOR_NAME] = np.zeros(first.shape, dtype=np.float16)
    export_adapter(output / "extra-tensor.onnx_adapter", extra_tensor)


def exact_array(left: np.ndarray, right: np.ndarray) -> bool:
    return (
        left.dtype == right.dtype
        and left.shape == right.shape
        and np.array_equal(left, right)
    )


def exact_parameter_set(
    actual: dict[str, np.ndarray],
    expected: dict[str, np.ndarray],
) -> bool:
    return set(actual) == set(expected) and all(
        exact_array(actual[name], expected[name]) for name in expected
    )


def verify_parsed_negative(
    experiment_id: str,
    path: Path,
    control: dict[str, np.ndarray],
) -> dict[str, object]:
    adapter_version, model_version, parameters = read_adapter(path)
    names = list(control)
    first_name = names[0]
    last_name = names[-1]
    common_metadata = (
        adapter_version == ADAPTER_VERSION
        and model_version == MODEL_VERSION
    )

    if experiment_id == "wrong-model-version":
        valid = (
            adapter_version == ADAPTER_VERSION
            and model_version == WRONG_MODEL_VERSION
            and exact_parameter_set(parameters, control)
        )
        evidence = {
            "observed_model_version": model_version,
            "expected_control_model_version": MODEL_VERSION,
        }
    elif experiment_id == "wrong-target-name":
        wrong_name = f"{first_name}.wrong_target"
        expected_names = [wrong_name, *names[1:]]
        valid = (
            common_metadata
            and set(parameters) == set(expected_names)
            and exact_array(parameters[wrong_name], control[first_name])
            and all(exact_array(parameters[name], control[name]) for name in names[1:])
        )
        evidence = {"removed_name": first_name, "added_name": wrong_name}
    elif experiment_id == "wrong-rank-shape":
        valid = (
            common_metadata
            and set(parameters) == set(names)
            and parameters[first_name].dtype == control[first_name].dtype
            and parameters[first_name].shape
            == (control[first_name].shape[0], control[first_name].shape[1] - 1)
            and np.array_equal(parameters[first_name], control[first_name][:, :-1])
            and all(exact_array(parameters[name], control[name]) for name in names[1:])
        )
        evidence = {
            "name": first_name,
            "control_shape": list(control[first_name].shape),
            "observed_shape": list(parameters[first_name].shape),
        }
    elif experiment_id == "wrong-dtype":
        valid = (
            common_metadata
            and set(parameters) == set(names)
            and parameters[first_name].dtype == np.float32
            and parameters[first_name].shape == control[first_name].shape
            and np.array_equal(parameters[first_name], control[first_name].astype(np.float32))
            and all(exact_array(parameters[name], control[name]) for name in names[1:])
        )
        evidence = {
            "name": first_name,
            "control_dtype": str(control[first_name].dtype),
            "observed_dtype": str(parameters[first_name].dtype),
        }
    elif experiment_id == "missing-tensor":
        valid = (
            common_metadata
            and set(parameters) == set(names[:-1])
            and all(exact_array(parameters[name], control[name]) for name in names[:-1])
        )
        evidence = {"missing_name": last_name}
    elif experiment_id == "extra-tensor":
        valid = (
            common_metadata
            and set(parameters) == {*names, EXTRA_TENSOR_NAME}
            and all(exact_array(parameters[name], control[name]) for name in names)
            and parameters[EXTRA_TENSOR_NAME].dtype == np.float16
            and parameters[EXTRA_TENSOR_NAME].shape == control[first_name].shape
            and not np.any(parameters[EXTRA_TENSOR_NAME])
        )
        evidence = {"extra_name": EXTRA_TENSOR_NAME}
    else:
        raise ValueError(f"unexpected parsed negative: {experiment_id}")

    if not valid:
        raise ValueError(f"negative is not the exact frozen mutation: {experiment_id}")
    return {
        "id": experiment_id,
        "mutationKind": experiment_id.replace("-", "_"),
        "path": str(path.resolve(strict=True)),
        "exists": True,
        "artifact": identity(path),
        "evidence": evidence,
    }


def verify(control_npz: Path, control_adapter: Path, directory: Path) -> dict[str, object]:
    control = load_npz(control_npz.resolve(strict=True))
    adapter_version, model_version, serialized_control = read_adapter(control_adapter)
    if (
        adapter_version != ADAPTER_VERSION
        or model_version != MODEL_VERSION
        or not exact_parameter_set(serialized_control, control)
    ):
        raise ValueError("the serialized control does not exactly match the frozen NPZ")

    records: list[dict[str, object]] = []
    missing_path = (directory / "missing-file.onnx_adapter").resolve()
    if missing_path.exists():
        raise ValueError("missing-file negative unexpectedly exists")
    records.append(
        {
            "id": "missing-file",
            "mutationKind": "missing_file",
            "path": str(missing_path),
            "exists": False,
            "artifact": None,
            "evidence": {"path_absent": True},
        }
    )

    truncated_path = directory / "truncated-file.onnx_adapter"
    expected_prefix = control_adapter.resolve(strict=True).read_bytes()
    actual_truncated = truncated_path.resolve(strict=True).read_bytes()
    expected_length = len(expected_prefix) // 2
    parse_failed = False
    parse_exception_type = None
    try:
        read_adapter(truncated_path)
    except Exception as exception:  # native parser type is retained as evidence
        parse_failed = True
        parse_exception_type = type(exception).__name__
    if (
        actual_truncated != expected_prefix[:expected_length]
        or not parse_failed
    ):
        raise ValueError("truncated-file is not the exact unreadable half-prefix mutation")
    records.append(
        {
            "id": "truncated-file",
            "mutationKind": "truncated_file",
            "path": str(truncated_path.resolve(strict=True)),
            "exists": True,
            "artifact": identity(truncated_path),
            "evidence": {
                "control_bytes": len(expected_prefix),
                "retained_prefix_bytes": expected_length,
                "parse_failed": True,
                "parse_exception_type": parse_exception_type,
            },
        }
    )

    for experiment_id in EXPECTED_IDS[2:]:
        records.append(
            verify_parsed_negative(
                experiment_id,
                directory / f"{experiment_id}.onnx_adapter",
                control,
            )
        )

    if [record["id"] for record in records] != list(EXPECTED_IDS):
        raise AssertionError("negative record order drifted")
    return {
        "schemaVersion": 1,
        "experimentId": "ACX-0023",
        "phase": "A",
        "valid": True,
        "controlNpz": identity(control_npz),
        "controlAdapter": identity(control_adapter),
        "negatives": records,
        "claimBoundary": (
            "Exact malformed adapter identities and structural mutations only; no model "
            "load, inference, rejection-family, recovery, latency, quality, or production claim."
        ),
    }


def write_json_new(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("x", encoding="utf-8", newline="\n") as stream:
        json.dump(value, stream, ensure_ascii=True, indent=2, sort_keys=True)
        stream.write("\n")


def self_test() -> None:
    with tempfile.TemporaryDirectory() as temporary:
        root = Path(temporary)
        control_npz = root / "control.npz"
        control_adapter = root / "control.onnx_adapter"
        output = root / "negatives"
        values = {
            "a": np.zeros((4, 8), dtype=np.float16),
            "b": np.zeros((8, 4), dtype=np.float16),
        }
        np.savez(control_npz, **values)
        export_adapter(control_adapter, values)

        # Exercise the generic serializer with the full generator shape contract.
        full_values: dict[str, np.ndarray] = {}
        for layer in range(28):
            for projection, output_size in (("q_proj", 2048), ("v_proj", 1024)):
                full_values[f"model.layers.{layer}.attn.{projection}.lora_A.MatMul.weight"] = np.zeros(
                    (1024, 8), dtype=np.float16
                )
                full_values[f"model.layers.{layer}.attn.{projection}.lora_B.MatMul.weight"] = np.zeros(
                    (8, output_size), dtype=np.float16
                )
        full_npz = root / "full.npz"
        full_adapter = root / "full.onnx_adapter"
        np.savez(full_npz, **full_values)
        export_adapter(full_adapter, full_values)
        generate(full_npz, full_adapter, output)
        result = verify(full_npz, full_adapter, output)
        assert result["valid"] is True
        assert [item["id"] for item in result["negatives"]] == list(EXPECTED_IDS)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    subparsers = parser.add_subparsers(dest="command", required=True)

    subparsers.add_parser("self-test")

    generate_parser = subparsers.add_parser("generate")
    generate_parser.add_argument("--control-npz", type=Path, required=True)
    generate_parser.add_argument("--control-adapter", type=Path, required=True)
    generate_parser.add_argument("--output", type=Path, required=True)

    verify_parser = subparsers.add_parser("verify")
    verify_parser.add_argument("--control-npz", type=Path, required=True)
    verify_parser.add_argument("--control-adapter", type=Path, required=True)
    verify_parser.add_argument("--directory", type=Path, required=True)
    verify_parser.add_argument("--output", type=Path, required=True)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    if args.command == "self-test":
        self_test()
        print("ACX-0023 negative-artifact self-test passed")
    elif args.command == "generate":
        generate(args.control_npz, args.control_adapter, args.output)
    elif args.command == "verify":
        result = verify(args.control_npz, args.control_adapter, args.directory)
        write_json_new(args.output, result)
        print(json.dumps(result, ensure_ascii=True, sort_keys=True))
    else:
        raise AssertionError(f"unexpected command: {args.command}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
