#!/usr/bin/env python3
"""Verify the frozen ACX-0023 adapter-ready graph and retain byte identities."""

from __future__ import annotations

import argparse
from collections import Counter, defaultdict
import copy
import hashlib
import json
import math
from pathlib import Path
import tempfile

import numpy as np
import onnx
from onnx import TensorProto, helper, numpy_helper


LAYER_COUNT = 28
RANK = 8
INPUT_SIZE = 1024
QUERY_OUTPUT_SIZE = 2048
VALUE_OUTPUT_SIZE = 1024
INTERMEDIATE_SIZE = 3072
VOCABULARY_SIZE = 151936
INT4_BITS = 4
INT4_BLOCK_SIZE = 128
INT4_ACCURACY_LEVEL = 4
LORA_PROJECTIONS = ("q_proj", "v_proj")
BASE_ATTENTION_PROJECTIONS = ("q_proj", "k_proj", "v_proj", "o_proj")
BASE_MLP_PROJECTIONS = ("gate_proj", "up_proj", "down_proj")


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


def lora_contract() -> tuple[dict[str, tuple[int, int]], set[str]]:
    tensors: dict[str, tuple[int, int]] = {}
    nodes: set[str] = set()
    for layer in range(LAYER_COUNT):
        for projection, output_size in (
            ("q_proj", QUERY_OUTPUT_SIZE),
            ("v_proj", VALUE_OUTPUT_SIZE),
        ):
            base = f"/model/layers.{layer}/attn/{projection}"
            a_node = f"{base}/lora_A/MatMul"
            b_node = f"{base}/lora_B/MatMul"
            nodes.update((a_node, b_node))
            tensors[a_node[1:].replace("/", ".") + ".weight"] = (
                INPUT_SIZE,
                RANK,
            )
            tensors[b_node[1:].replace("/", ".") + ".weight"] = (
                RANK,
                output_size,
            )
    return tensors, nodes


def base_matmul_contract() -> dict[str, tuple[int, int]]:
    nodes = {"/lm_head/MatMul": (INPUT_SIZE, VOCABULARY_SIZE)}
    for layer in range(LAYER_COUNT):
        nodes.update(
            {
                f"/model/layers.{layer}/attn/q_proj/MatMul": (
                    INPUT_SIZE,
                    QUERY_OUTPUT_SIZE,
                ),
                f"/model/layers.{layer}/attn/k_proj/MatMul": (
                    INPUT_SIZE,
                    VALUE_OUTPUT_SIZE,
                ),
                f"/model/layers.{layer}/attn/v_proj/MatMul": (
                    INPUT_SIZE,
                    VALUE_OUTPUT_SIZE,
                ),
                f"/model/layers.{layer}/attn/o_proj/MatMul": (
                    QUERY_OUTPUT_SIZE,
                    INPUT_SIZE,
                ),
            }
        )
        nodes.update(
            {
                f"/model/layers.{layer}/mlp/gate_proj/MatMul": (
                    INPUT_SIZE,
                    INTERMEDIATE_SIZE,
                ),
                f"/model/layers.{layer}/mlp/up_proj/MatMul": (
                    INPUT_SIZE,
                    INTERMEDIATE_SIZE,
                ),
                f"/model/layers.{layer}/mlp/down_proj/MatMul": (
                    INTERMEDIATE_SIZE,
                    INPUT_SIZE,
                ),
            }
        )
    return nodes


def attribute_values(node: onnx.NodeProto) -> dict[str, object]:
    return {
        attribute.name: helper.get_attribute_value(attribute)
        for attribute in node.attribute
    }


def initializer_values(tensor: TensorProto, graph_path: Path) -> np.ndarray:
    return numpy_helper.to_array(tensor, base_dir=str(graph_path.parent))


def quantized_weight_names(base_name: str) -> tuple[str, str]:
    stem = base_name[1:].replace("/", ".") + ".weight"
    return f"{stem}_Q4", f"{stem}_scales"


def expected_quantized_shape(rows: int, columns: int) -> tuple[tuple[int, ...], tuple[int, ...]]:
    blocks = math.ceil(rows / INT4_BLOCK_SIZE)
    return (
        (columns, blocks, INT4_BLOCK_SIZE // 2),
        (columns, blocks),
    )


def add_error(errors: list[str], code: str) -> None:
    if code not in errors:
        errors.append(code)


def reaches_graph_output(
    tensor_name: str,
    consumers: dict[str, list[onnx.NodeProto]],
    graph_outputs: set[str],
) -> bool:
    """Prove that a tensor participates in a live path to a declared graph output."""
    pending = [tensor_name]
    visited: set[str] = set()
    while pending:
        current = pending.pop()
        if current in graph_outputs:
            return True
        if current in visited:
            continue
        visited.add(current)
        for consumer in consumers.get(current, []):
            pending.extend(consumer.output)
    return False


def verify_graph(model: onnx.ModelProto, graph_path: Path) -> dict[str, object]:
    expected_tensors, expected_lora_nodes = lora_contract()
    expected_base = base_matmul_contract()
    expected_quantized_nodes = {f"{name}_Q4" for name in expected_base}
    node_name_counts = Counter(node.name for node in model.graph.node)
    initializer_name_counts = Counter(tensor.name for tensor in model.graph.initializer)
    duplicate_node_names = sorted(
        name for name, count in node_name_counts.items() if count != 1
    )
    duplicate_initializer_names = sorted(
        name for name, count in initializer_name_counts.items() if count != 1
    )
    nodes = {node.name: node for node in model.graph.node}
    initializers = {tensor.name: tensor for tensor in model.graph.initializer}
    consumers: dict[str, list[onnx.NodeProto]] = defaultdict(list)
    for node in model.graph.node:
        for input_name in node.input:
            consumers[input_name].append(node)
    graph_outputs = {output.name for output in model.graph.output}
    errors: list[str] = []

    if duplicate_node_names:
        errors.append("duplicate_node_names")
    if duplicate_initializer_names:
        errors.append("duplicate_initializer_names")

    observed_lora_names = {name for name in nodes if "lora_" in name}
    if observed_lora_names != expected_lora_nodes:
        errors.append("lora_node_name_set_mismatch")

    float_matmul_names = {
        node.name for node in model.graph.node if node.op_type == "MatMul"
    }
    quantized_matmul_names = {
        node.name for node in model.graph.node if node.op_type == "MatMulNBits"
    }
    exact_matmul_set = (
        float_matmul_names == expected_lora_nodes
        and quantized_matmul_names == expected_quantized_nodes
    )
    if not exact_matmul_set:
        errors.append("exact_matmul_node_set_mismatch")

    exact_wiring = True
    exact_consumer_replacement = True
    for layer in range(LAYER_COUNT):
        for projection in LORA_PROJECTIONS:
            base_name = f"/model/layers.{layer}/attn/{projection}/MatMul"
            base_node = nodes.get(f"{base_name}_Q4")
            a_name = f"/model/layers.{layer}/attn/{projection}/lora_A/MatMul"
            b_name = f"/model/layers.{layer}/attn/{projection}/lora_B/MatMul"
            add_name = f"/model/layers.{layer}/attn/{projection}/lora/Add"
            a_node = nodes.get(a_name)
            b_node = nodes.get(b_name)
            add_node = nodes.get(add_name)
            a_weight = a_name[1:].replace("/", ".") + ".weight"
            b_weight = b_name[1:].replace("/", ".") + ".weight"
            base_output = f"{base_name}/output_0"
            a_output = f"{a_name}/output_0"
            b_output = f"{b_name}/output_0"
            add_output = f"{add_name}/output_0"
            if (
                base_node is None
                or a_node is None
                or b_node is None
                or add_node is None
                or a_node.op_type != "MatMul"
                or b_node.op_type != "MatMul"
                or add_node.op_type != "Add"
                or len(base_node.input) != 3
                or list(a_node.input) != [base_node.input[0], a_weight]
                or list(a_node.output) != [a_output]
                or list(b_node.input) != [a_output, b_weight]
                or list(b_node.output) != [b_output]
                or list(add_node.input)
                != [base_output, b_output]
                or list(add_node.output) != [add_output]
            ):
                exact_wiring = False
                add_error(errors, f"lora_wiring_mismatch:{layer}:{projection}")
                continue

            if (
                [node.name for node in consumers.get(base_output, [])] != [add_name]
                or [node.name for node in consumers.get(a_output, [])] != [b_name]
                or [node.name for node in consumers.get(b_output, [])] != [add_name]
                or not consumers.get(add_output)
                or not reaches_graph_output(add_output, consumers, graph_outputs)
            ):
                exact_consumer_replacement = False
                add_error(
                    errors,
                    f"lora_consumer_replacement_mismatch:{layer}:{projection}",
                )

    observed_lora_initializers = {
        name for name in initializers if ".lora_" in name
    }
    exact_initializer_set = observed_lora_initializers == set(expected_tensors)
    if not exact_initializer_set:
        errors.append("lora_initializer_name_set_mismatch")
    lora_initializers_positive_zero = True
    for name, expected_shape in expected_tensors.items():
        tensor = initializers.get(name)
        if tensor is None:
            continue
        if tensor.data_type != TensorProto.FLOAT16:
            errors.append(f"lora_initializer_dtype_mismatch:{name}")
        if tuple(tensor.dims) != expected_shape:
            errors.append(f"lora_initializer_shape_mismatch:{name}")
        try:
            values = initializer_values(tensor, graph_path)
        except (OSError, ValueError) as exception:
            lora_initializers_positive_zero = False
            errors.append(f"lora_initializer_unreadable:{name}:{type(exception).__name__}")
            continue
        if (
            values.dtype != np.float16
            or values.shape != expected_shape
            or np.any(values != np.float16(0.0))
            or np.signbit(values).any()
        ):
            lora_initializers_positive_zero = False
            errors.append(f"lora_initializer_not_positive_zero:{name}")

    exact_int4_attributes = True
    exact_int4_weight_wiring = True
    quantized_base_count = 0
    for base_name, (rows, columns) in expected_base.items():
        node_name = f"{base_name}_Q4"
        node = nodes.get(node_name)
        if node is None or node.op_type != "MatMulNBits":
            exact_int4_attributes = False
            exact_int4_weight_wiring = False
            add_error(errors, f"base_matmul_missing:{base_name}")
            continue
        quantized_base_count += 1
        expected_attributes = {
            "K": rows,
            "N": columns,
            "bits": INT4_BITS,
            "block_size": INT4_BLOCK_SIZE,
            "accuracy_level": INT4_ACCURACY_LEVEL,
        }
        if node.domain != "com.microsoft" or attribute_values(node) != expected_attributes:
            exact_int4_attributes = False
            add_error(errors, f"base_matmul_attribute_mismatch:{base_name}")

        weight_name, scales_name = quantized_weight_names(base_name)
        weight = initializers.get(weight_name)
        scales = initializers.get(scales_name)
        expected_weight_shape, expected_scales_shape = expected_quantized_shape(
            rows,
            columns,
        )
        if (
            len(node.input) != 3
            or list(node.input[1:]) != [weight_name, scales_name]
            or list(node.output) != [f"{base_name}/output_0"]
            or weight is None
            or weight.data_type != TensorProto.UINT8
            or tuple(weight.dims) != expected_weight_shape
            or scales is None
            or scales.data_type != TensorProto.FLOAT16
            or tuple(scales.dims) != expected_scales_shape
        ):
            exact_int4_weight_wiring = False
            add_error(errors, f"base_matmul_weight_wiring_mismatch:{base_name}")

    graph_identity = identity(graph_path)
    return {
        "schemaVersion": 2,
        "valid": not errors,
        "graph": graph_identity,
        "loRaMatMulNodeCount": len(observed_lora_names),
        "loRaInitializerCount": len(observed_lora_initializers),
        "expectedLoRaCount": len(expected_lora_nodes),
        "nonExcludedBaseMatMulCount": len(expected_base),
        "quantizedNonExcludedBaseMatMulCount": quantized_base_count,
        "duplicateNodeNameCount": len(duplicate_node_names),
        "duplicateInitializerNameCount": len(duplicate_initializer_names),
        "exactMatMulNodeSet": exact_matmul_set,
        "exactLoRaInitializerSet": exact_initializer_set,
        "loRaInitializersPositiveZero": lora_initializers_positive_zero,
        "exactInt4Attributes": exact_int4_attributes,
        "exactInt4WeightWiring": exact_int4_weight_wiring,
        "exactLoRaWiring": exact_wiring,
        "exactLoRaConsumerReplacement": exact_consumer_replacement,
        "errors": errors,
    }


def write_json_new(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("x", encoding="utf-8", newline="\n") as stream:
        json.dump(value, stream, ensure_ascii=False, indent=2, sort_keys=True)
        stream.write("\n")


def build_artifact_manifest(
    model_directory: Path,
    graph_verification: Path,
    artifacts: list[Path],
) -> dict[str, object]:
    model_root = model_directory.resolve(strict=True)
    paths = list(path for path in model_root.rglob("*") if path.is_file())
    paths.append(graph_verification)
    paths.extend(artifacts)
    unique = {path.resolve(strict=True) for path in paths}
    return {
        "schemaVersion": 1,
        "modelDirectory": str(model_root),
        "completeModelDirectory": True,
        "files": [identity(path) for path in sorted(unique)],
    }


def synthetic_valid_model() -> onnx.ModelProto:
    tensors, _ = lora_contract()
    nodes = []
    initializers = []
    graph_outputs = []
    base = base_matmul_contract()
    for base_name, (rows, columns) in base.items():
        weight_name, scales_name = quantized_weight_names(base_name)
        weight_shape, scales_shape = expected_quantized_shape(rows, columns)
        weight = TensorProto(
            name=weight_name,
            data_type=TensorProto.UINT8,
            dims=weight_shape,
        )
        scales = TensorProto(
            name=scales_name,
            data_type=TensorProto.FLOAT16,
            dims=scales_shape,
        )
        initializers.extend((weight, scales))
        nodes.append(
            helper.make_node(
                "MatMulNBits",
                [f"{base_name}/input_0", weight_name, scales_name],
                [f"{base_name}/output_0"],
                name=f"{base_name}_Q4",
                domain="com.microsoft",
                K=rows,
                N=columns,
                bits=INT4_BITS,
                block_size=INT4_BLOCK_SIZE,
                accuracy_level=INT4_ACCURACY_LEVEL,
            )
        )
    for name, shape in tensors.items():
        initializers.append(numpy_helper.from_array(np.zeros(shape, dtype=np.float16), name))
    for layer in range(LAYER_COUNT):
        for projection in LORA_PROJECTIONS:
            base_name = f"/model/layers.{layer}/attn/{projection}/MatMul"
            a_name = f"/model/layers.{layer}/attn/{projection}/lora_A/MatMul"
            b_name = f"/model/layers.{layer}/attn/{projection}/lora_B/MatMul"
            add_name = f"/model/layers.{layer}/attn/{projection}/lora/Add"
            a_weight = a_name[1:].replace("/", ".") + ".weight"
            b_weight = b_name[1:].replace("/", ".") + ".weight"
            root_input = f"{base_name}/input_0"
            nodes.extend(
                (
                    helper.make_node(
                        "MatMul",
                        [root_input, a_weight],
                        [f"{a_name}/output_0"],
                        name=a_name,
                    ),
                    helper.make_node(
                        "MatMul",
                        [f"{a_name}/output_0", b_weight],
                        [f"{b_name}/output_0"],
                        name=b_name,
                    ),
                    helper.make_node(
                        "Add",
                        [f"{base_name}/output_0", f"{b_name}/output_0"],
                        [f"{add_name}/output_0"],
                        name=add_name,
                    ),
                    helper.make_node(
                        "Identity",
                        [f"{add_name}/output_0"],
                        [f"{add_name}/continuation_0"],
                        name=f"{add_name}/continuation",
                    ),
                )
            )
            graph_outputs.append(
                helper.make_tensor_value_info(
                    f"{add_name}/continuation_0",
                    TensorProto.FLOAT16,
                    None,
                )
            )
    return helper.make_model(
        helper.make_graph(nodes, "acx-0023", [], graph_outputs, initializers)
    )


def self_test() -> None:
    with tempfile.TemporaryDirectory() as temporary:
        graph_path = Path(temporary) / "synthetic.onnx"
        model = synthetic_valid_model()
        onnx.save_model(model, graph_path)
        result = verify_graph(model, graph_path)
        assert result["valid"] is True
        assert result["loRaMatMulNodeCount"] == 112
        assert result["nonExcludedBaseMatMulCount"] == 197

        broken_models = []

        duplicate = copy.deepcopy(model)
        duplicate.graph.node.append(copy.deepcopy(duplicate.graph.node[0]))
        broken_models.append(duplicate)

        extra = copy.deepcopy(model)
        extra.graph.node.append(helper.make_node("MatMul", ["a", "b"], ["c"], name="extra"))
        broken_models.append(extra)

        attributes = copy.deepcopy(model)
        quantized = next(node for node in attributes.graph.node if node.op_type == "MatMulNBits")
        next(attribute for attribute in quantized.attribute if attribute.name == "block_size").i = 32
        broken_models.append(attributes)

        wiring = copy.deepcopy(model)
        lora_b = next(node for node in wiring.graph.node if "/lora_B/" in node.name)
        lora_b.input[0] = "wrong"
        broken_models.append(wiring)

        weight_wiring = copy.deepcopy(model)
        quantized = next(node for node in weight_wiring.graph.node if node.op_type == "MatMulNBits")
        quantized.input[1] = "wrong"
        broken_models.append(weight_wiring)

        disconnected = copy.deepcopy(model)
        continuation = next(
            node for node in disconnected.graph.node if node.name.endswith("/continuation")
        )
        continuation.input[0] = "detached/input"
        broken_models.append(disconnected)

        nonzero = copy.deepcopy(model)
        lora_initializer = next(
            tensor for tensor in nonzero.graph.initializer if ".lora_" in tensor.name
        )
        replacement = numpy_helper.from_array(
            np.ones(tuple(lora_initializer.dims), dtype=np.float16),
            lora_initializer.name,
        )
        lora_initializer.CopyFrom(replacement)
        broken_models.append(nonzero)

        for broken in broken_models:
            broken_result = verify_graph(broken, graph_path)
            assert broken_result["valid"] is False


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--model-dir", type=Path)
    parser.add_argument("--graph", type=Path)
    parser.add_argument("--graph-verification", type=Path)
    parser.add_argument("--artifact-manifest", type=Path)
    parser.add_argument("--artifact", action="append", default=[], type=Path)
    parser.add_argument("--self-test", action="store_true")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    if args.self_test:
        self_test()
        print("ACX-0023 export verifier self-test passed")
        return 0
    if not all(
        (
            args.model_dir,
            args.graph,
            args.graph_verification,
            args.artifact_manifest,
        )
    ):
        raise SystemExit("All export-verification paths are required.")

    model = onnx.load_model(args.graph, load_external_data=False)
    result = verify_graph(model, args.graph)
    write_json_new(args.graph_verification, result)
    manifest = build_artifact_manifest(
        args.model_dir,
        args.graph_verification,
        args.artifact,
    )
    write_json_new(args.artifact_manifest, manifest)
    print(json.dumps(result, sort_keys=True))
    return 0 if result["valid"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
