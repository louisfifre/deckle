---
name: acx-0022-one-qwen-multi-adapter
description: "Frozen compatibility design for one resident Qwen base with per-task LoRA adapters."
type: benchmark-plan
module: benchmark/autoresearch/campaigns/interactive-autocorrect
---

# ACX-0022 — one shared Qwen base with per-task LoRA adapters

## Preregistration status

Static design validated after an independent NO-GO, revision, and final GO.
Closed as a valid pre-registration toolchain negative: the frozen builder
environment did not import. No model run or experiment was preregistered.

## Product question

Can Deckle store and load one local Qwen base, then give each LLM use a small,
isolated LoRA adapter, without duplicating the base model or its resident
session?

This experiment studies runtime and artifact compatibility only. It does not
train a task adapter, compare task quality, or change production behavior.

## Why this shape

A full copy of the installed Qwen3-1.7B sentence judge occupies 1,612,620,826
bytes. Repeating that export once per task would multiply the dominant disk
cost. LoRA freezes the base and stores only low-rank matrices for selected
linear layers, so the intended layout is:

```text
one adapter-ready Qwen base
├── autocorrect.onnx_adapter
├── rewrite.onnx_adapter
├── command-routing.onnx_adapter
└── future-task.onnx_adapter
```

The runtime must still prove whether those adapter tensors are mapped, copied,
or retained separately in RAM and DirectML memory. Disk deduplication does not
by itself establish memory deduplication or fast switching.

## Phase-0 observations established before design freeze

- Deckle pins `Microsoft.ML.OnnxRuntimeGenAI.DirectML` 0.13.0.
- The installed managed assembly exposes one `Adapters(Model)` manager,
  `LoadAdapter(path, name)`, `UnloadAdapter(name)`, and the singular
  `Generator.SetActiveAdapter(adapters, name)` method.
- The matching native header says one manager loads all model adapters and
  reference-counts them. An adapter cannot be unloaded while in use.
- The public ONNX Runtime GenAI tutorial describes Multi-LoRA as multiple
  fine-tunings of the same model, requires every adapter to use the same
  fine-tuned layers, and uses one `.onnx_adapter` file per adapter.
- The current `sentence-judge` export totals 1,612,620,826 bytes, including a
  1,600,847,872-byte int4 external-data file.
- `models\sentence-judge` is already an NTFS junction to the staged
  Qwen3-1.7B DirectML directory. The identical visible paths therefore resolve
  to one file identity and do not currently duplicate those 1.6 GB. This is a
  useful deployment precedent, not evidence that multiple task-specific model
  exports would share their distinct weights.
- A read-only ONNX inspection found 602 graph nodes, 510 initializers, and only
  the ordinary Qwen inputs: token, mask, position, and 28 key/value cache pairs.
  No input, node input, or node output name contains `lora` or `adapter`.

The last observation refutes drop-in activation against the current production
export. ACX-0022 needs a separate adapter-ready research export. The production
judge remains untouched and remains the comparison artifact.

The existing Python environment is not eligible: it contains GenAI 0.13.0 but
ONNX Runtime DirectML 1.24.4, while Deckle's 0.13.0 NuGet depends on ONNX Runtime
DirectML 1.23.0. It also lacks PEFT and Olive. Its successful ordinary Qwen
export is useful history, not a compatible adapter toolchain.

## Frozen source and synthetic LoRA contract

- base: `Qwen/Qwen3-1.7B`;
- Hugging Face revision: `70d244cc86ccca08cf5af4e1e306ecf908b1ad5e`;
- local source snapshot: the exact cached directory for that revision, with
  every consumed file hashed before export and no remote resolution;
- architecture: 28 layers, hidden 2,048, intermediate 6,144, 16 query heads,
  8 key/value heads, head size 128;
- PEFT type/task: `LORA` / `CAUSAL_LM`;
- target modules, in this exact order: `q_proj`, `v_proj`;
- rank 8, alpha 16, scaling 2, dropout 0, bias `none`;
- runtime adapter tensor dtype: float16;
- control tensors: every value exactly positive zero;
- sentinel seed: 20260730;
- sentinel generator: NumPy `PCG64`, standard-normal values multiplied by
  exactly 0.01 before float16 conversion; consume tensors in layer-ascending,
  `q_proj`-then-`v_proj`, `lora_A`-then-`lora_B` order, filling each frozen ONNX
  shape in C order before moving to the next tensor;
- ONNX adapter format version: 1;
- ONNX model version: 0, matching the adapter-ready model's frozen
  `ModelProto.model_version`;
- adapter version: 1 for both `control-zero` and `sentinel-seeded`.

All 28 layers must expose exactly four adaptable initializers with these frozen
name and shape patterns:

- `model.layers.{n}.attn.q_proj.lora_A.MatMul.weight`: `[2048,8]`;
- `model.layers.{n}.attn.q_proj.lora_B.MatMul.weight`: `[8,2048]`;
- `model.layers.{n}.attn.v_proj.lora_A.MatMul.weight`: `[2048,8]`;
- `model.layers.{n}.attn.v_proj.lora_B.MatMul.weight`: `[8,1024]`.

The export is invalid unless there are exactly 112 such float16 tensors and no
other LoRA tensor. PEFT A matrices are transposed from `[rank,in]` to `[in,rank]`.
PEFT B matrices are first multiplied by alpha/rank, then transposed from
`[out,rank]` to `[rank,out]`. The builder's source implementing those transforms
is hashed and retained. No name, transpose, scale, cast, or target can be inferred
from a successful runtime load.

The corresponding 112 LoRA MatMul nodes use these exact names, for `n=0..27`:

- `/model/layers.{n}/attn/q_proj/lora_A/MatMul`;
- `/model/layers.{n}/attn/q_proj/lora_B/MatMul`;
- `/model/layers.{n}/attn/v_proj/lora_A/MatMul`;
- `/model/layers.{n}/attn/v_proj/lora_B/MatMul`.

Before `save_model`, generate the exclusion list in layer-ascending,
`q_proj`-then-`v_proj`, `lora_A`-then-`lora_B` order and pass all entries as
`int4_nodes_to_exclude`. The export fails closed unless that list has exactly
112 unique names, every named node exists and is a `MatMul`, and
post-quantization inspection finds exactly those 112 nodes still backed by the
frozen float16 initializers and shapes above. No other LoRA node or initializer
may exist, and every non-excluded base MatMul must meet the frozen int4
quantization contract.

## Frozen toolchain and export route

Use two new ignored Python environments dedicated to ACX-0022. They may be
created only from separate hash-locked requirements manifests.

The builder environment contains:

- CPython 3.13.14;
- `onnxruntime-genai-directml==0.13.0`;
- its declared `onnxruntime-directml==1.24.4` dependency;
- `numpy==2.2.6`, `onnx==1.22.0`, `onnx-ir==0.2.1`,
  `transformers==4.51.0`, `torch==2.12.1`, and `peft==0.15.2`.

The converter environment contains:

- CPython 3.13.14;
- `onnxruntime-directml==1.23.0` and `numpy==2.2.6`;
- no ONNX Runtime GenAI package.

The consumer is the actual repository-pinned .NET stack:
`Microsoft.ML.OnnxRuntimeGenAI.DirectML==0.13.0` with its declared
`Microsoft.ML.OnnxRuntime.DirectML==1.23.0` dependency. The builder/runtime
version boundary is deliberate. A graph produced by 1.24.4 is not called
compatible until the unmodified 1.23.0 .NET consumer passes the complete probe.
The existing ordinary graph crossing that boundary is precedent only, not
adapter evidence.

Every wheel hash, requirements manifest, `pip freeze`, Python executable hash,
GenAI builder source hash, and ONNX Runtime NPZ-converter source hash is
retained for its environment. Failure to resolve, import, or execute either
exact set is a valid compatibility failure; versions are not changed after
observation.

The only Qwen export route is the installed GenAI 0.13.0 model builder with the
local base snapshot, `int4`, `dml`, block 32, accuracy level 0, and the frozen
zero PEFT adapter passed as `adapter_path`. The builder produces one graph with
the frozen LoRA subgraphs and default zero initializers. It does not produce the
runtime adapter files.

A probe-owned deterministic extractor verifies the 112 graph tensors, applies
the frozen PEFT-to-ONNX mapping above, and writes one NPZ per adapter. The only
converter is ONNX Runtime 1.23.0's
`onnxruntime.capi.convert_npz_to_onnx_adapter`, invoked with the frozen adapter
and model versions. Each output is read back through `AdapterFormat`; version,
name, shape, dtype, raw tensor bytes, and finite-value checks must match the NPZ
exactly before the .NET runtime may see it.

## Size calculation, not measurement

The local Qwen3-1.7B configuration has 28 layers, hidden size 2,048,
intermediate size 6,144, 16 query heads, and 8 key/value heads. For rank 8:

- adapting only `q_proj` and `v_proj` contains 1,605,632 parameters: about
  3.06 MiB at float16 or 6.13 MiB at float32;
- adapting all seven attention/MLP projections contains 8,716,288 parameters:
  about 16.63 MiB at float16 or 33.25 MiB at float32.

These are matrix-payload estimates. `.onnx_adapter` metadata, alignment,
conversion behavior, and the actual target set can change the measured file
size. No disk-saving claim is admissible until exact exported bytes are retained.

## Frozen architecture contract

The compatibility probe owns one `Model`, one `Tokenizer`, and one `Adapters`
manager for the complete run. It loads at least two adapters under distinct
names. Every scored request creates a fresh `Generator`, selects exactly one
adapter on that generator before its first forward, completes or disposes the
generator, and never changes the active adapter mid-request.

This matches the pinned C# API: adapter selection is generator-scoped, not a
global mutation of the base model. “Switch latency” therefore has three
separately reported components:

1. adapter-file load into the shared manager;
2. fresh generator creation;
3. `SetActiveAdapter` plus first adapted forward.

No result may report their sum as a pure adapter switch. Concurrent generators,
adapter-aware batching, and mid-generation switching are outside the first
probe.

## Frozen synthetic artifacts

No task training is allowed in ACX-0022. The export stage creates the two frozen
synthetic adapters defined above:

- `control-zero`: every LoRA tensor is zero;
- `sentinel-seeded`: the frozen deterministic non-zero tensor stream.

The zero adapter tests the adapter-ready base path without claiming exact
equivalence to the existing production export. The sentinel exists only to make
cross-adapter contamination observable. It is not a language model improvement
and must never be used in production.

The zero adapter supplies the frozen target-layer contract used to create the
adapter-ready ONNX graph. Both PEFT directories, the extracted NPZ files, and converted
`.onnx_adapter` files retain manifests, hashes, byte counts, tensor names,
shapes, dtypes, rank, alpha, targets, base identifier, and base revision.

Before `LoadAdapter`, the probe validates a sidecar manifest containing the
base repository and revision, adapter-ready graph SHA-256, target modules,
rank, dtype, and the complete sorted tensor-name/shape set. A mismatch is a
probe-policy refusal and the runtime is not called. This gate is mandatory
because `.onnx_adapter` model version and tensor metadata cannot identify the
semantic Hugging Face source or graph bytes. Runtime structural rejection is
recorded separately and never presented as semantic-base validation.

## Phased experiment

### Phase A — low-cost CPU API smoke

Before DirectML work, use `Qwen/Qwen3-0.6B` at frozen revision
`c1899de289a04d12100db370d81485cdf75e47ca`, exported as CPU int4 block 128
with the same rank, alpha, target modules, adapter versions, synthetic-value
rules, and manager lifecycle. Its model-specific tensor shapes are frozen from
its local config before export. This phase proves:

- one model and one adapters manager;
- two simultaneously loaded names;
- generator-scoped activation;
- alternating A/B/A/B output isolation;
- probe-policy refusal of a mismatched semantic-base/graph manifest;
- runtime refusal of an incompatible target name, rank, shape, or dtype;
- refusal to unload an adapter while a live generator references it;
- successful unload after every referencing generator is disposed.

This phase validates only the wrapper and lifecycle. It cannot establish Qwen or
DirectML compatibility.

### Phase B — exact Qwen3-1.7B DirectML compatibility

Create one adapter-ready int4 DirectML export from the exact frozen Qwen base
revision and one frozen layer contract. Load that base once, load both synthetic
adapters, then run seeded serialized requests in base/control/sentinel and
reversed adapter orders.

The existing non-adapter production export is scored separately on the same
public prompts. Its outputs are a drift reference, never an equality oracle:
adding an adapter-capable graph path may introduce numerical differences even
when the LoRA contribution is zero.

The frozen public cases, in order, are `la_location`, `literal_ratures`, and
`participle_after_avoir`, read from the committed `CorrectionBenchmarkCorpus`
whose source hash is retained. The scorer uses its existing exact forward prompt
and forced-candidate construction. For each case, after one unmeasured warm-up
per state, fresh generators run these exact state orders:

1. `base, control, sentinel, control, sentinel, control`;
2. `base, sentinel, control, sentinel, control, sentinel`.

No random sampling or free generation is used. The base state uses a fresh
generator with no call to `SetActiveAdapter`; every adapted state calls it once
before the first forward.

DirectML execution is fail-closed. The probe constructs one `Config`, clears
every provider, appends only `dml`, then calls
`config.SetProviderOption("dml", "enable_graph_capture", "1")` before constructing
the `Model`. Every request supplies exactly one input sequence; no unsupported
managed max-batch API or JSON field is inferred. It retains verbose ONNX Runtime
provider-assignment logs, loaded `onnxruntime.dll`, `onnxruntime-genai.dll`, and
`DirectML.dll` paths/hashes, plus the DXGI adapter description and LUID. Phase B
is invalid if graph capture does not complete, any compute node is assigned to a
CPU provider, provider identity is missing, or the output is not finite. Merely
reporting the requested provider string is insufficient.

Phase B is invalid if the adapter-ready graph duplicates the base weights per
adapter, the probe constructs more than one managed `Model`, silently falls back
from DirectML, or cannot retain both adapter names simultaneously. Native
session count remains unclaimed unless the retained runtime logs expose it.

## Retained measurements

### Artifact identity and disk

- exact command, tool/package versions, base repository and revision;
- hashes and bytes for the adapter-ready base and every adapter artifact;
- graph inputs, initializers, and LoRA tensor names/shapes/dtypes;
- total bytes for one base plus N adapters;
- counterfactual bytes for N complete base copies, explicitly labeled arithmetic.

### Timing

- five serial fresh-process base loads and adapter loads, with no OS cache flush;
  the first chronological sample and the following four samples are reported
  separately and none is called a physical cold load;
- within-process adapter load/unload cycles after every generator is disposed,
  10 untimed warm-ups followed by 100 measured cycles per adapter;
- fresh generator construction;
- activation call alone;
- activation-to-first-finite-logits;
- complete one-forward request;
- unload after disposal;
- raw samples and p50/p95/p99/max for every repeated phase.

Activation-only timing uses 10 untimed warm-ups and 100 measured fresh-generator
samples per adapter. First-forward timing uses 3 untimed warm-ups and 20 measured
samples per base/control/sentinel state on `la_location`. Wall time, process CPU
time, and managed allocation are separate. File-cache state and warm-up order
are retained. Full GC is requested before each measurement block, never inside
an individual timed sample.

### Memory

- private bytes, working set, and managed heap before model load, after model
  load, after each adapter load, after alternating requests, after unload, and
  after disposal;
- DirectML dedicated/shared GPU-memory observations only when a reproducible
  process-attributed measurement is available;
- otherwise an explicit `gpu_memory_unmeasured` result, never a zero delta.

The probe samples process memory immediately before each state transition,
every 50 ms while the transition is active, and after 250 ms of quiescence. Raw
samples and both endpoint and observed-peak deltas are retained. The sampler's
CPU/allocation overhead is measured in a no-model control process.

Peak deltas do not prove physical sharing. One resident `Model` instance and the
absence of duplicate base files are necessary but not sufficient evidence.

### Isolation and validity

- finite logits with frozen shape and dtype;
- SHA-256 fingerprints of exact serialized float16 logits per
  base/control/sentinel prompt and order, retained as diagnostics only;
- A/B/A and B/A/B numerical stability for each adapter;
- fresh-generator repetition stability;
- within-state repeatability floor defined as the maximum absolute logit delta
  across the repeated fresh-generator outputs for that state;
- sentinel differs from control by max-absolute logit delta at least 0.01 and
  at least ten times the larger control/sentinel repeatability floor;
- control versus adapter-ready no-adapter max-absolute logit delta is at most
  0.001, every forced candidate-score delta is at most 0.001, and winner identity
  is stable;
- adapter-ready no-adapter drift versus the current production export is
  accepted only when max-absolute logit delta is at most 0.001, every forced
  candidate-score delta is at most 0.001, and winner identity is stable; the
  threshold is inherited from ACX-0018 and is not relaxed after observation;
- A/B/A and B/A/B repetitions for the same state pass those same 0.001 logit
  and candidate-score tolerances;
- incompatible adapter, duplicate name, missing file, unload-in-use, and
  use-after-unload negative cases return named failures without corrupting the
  remaining manager state.

The frozen negative matrix contains distinct missing-file, truncated-file,
wrong-model-version, duplicate-name, wrong target name, wrong rank/shape, wrong
dtype, missing tensor, extra tensor, unload-while-referenced, and
activation-after-successful-unload cases. It also contains semantic-base,
graph-hash, target-set, rank, dtype, name-set, and shape-set sidecar mismatches;
these must stop at the probe-policy gate before any runtime call. Every runtime
case must fail in its named stage, then a fresh control generator must still
produce its reference output within the 0.001 gates. Use-after-unload means
activation on a fresh generator after unload; no disposed native handle is
dereferenced.

The `Adapters` manager bound to another `Model` negative runs in a separate
fresh process with two disposable models. Its result is retained independently
and does not count against Phase B's one-managed-`Model` invariant.

The lifetime order is fixed: dispose every generator; unload both adapter names;
dispose `Adapters`; dispose `Tokenizer`; dispose `Model`; dispose `Config`; then
dispose `OgaHandle`. Failure-path cleanup follows the same dependency order.

## Falsifiers

The shared-base direction is rejected for the pinned Deckle stack if any of the
following holds:

- each task requires its own base export or the probe cannot serve both through
  its one managed `Model`;
- DirectML cannot execute the adapter-ready graph without fallback;
- two compatible adapters cannot remain loaded together;
- output depends on prior adapter order or leaks between fresh generators;
- unload/reference lifetime is unsafe or ambiguous;
- measured disk or memory savings are too small relative to load, switch, and
  inference costs;
- the adapter-ready base changes the frozen sentence-judge behavior beyond the
  frozen absolute tolerance of 0.001 before any task adaptation is active.

A CPU-only success does not rescue a DirectML failure. A runtime success does
not establish that one 1.7B base has enough capability for every future Deckle
task; an adapter specializes existing capability but cannot guarantee new base
capabilities.

## Validation before preregistration

- independent static audit of export assumptions, adapter lifetimes, negative
  cases, numerical comparison, and claim boundary;
- a probe/test-only implementation with no production reference or asset copy;
- behavior tests using metadata-only fakes before any model execution;
- exact clean source head and raw ignored plan hashed before execution;
- serial CPU smoke before any serial DirectML run;
- all raw artifacts retained locally and append-only campaign events recorded.

## Claim boundary

ACX-0022 can establish only artifact deduplication, pinned-runtime API behavior,
measured load/activation/inference costs, observed memory deltas, and output
isolation for the exact synthetic adapters, exports, prompts, providers, and
machine tested. “One model” means one managed `Model` constructed by the probe;
native session sharing is not claimed without direct runtime evidence. The
experiment cannot establish task quality, one-model product sufficiency,
autocorrection precision, grouped validation, field behavior, concurrent safety,
adapter training quality, end-to-end interaction latency, production readiness,
or a general ONNX Runtime/DirectML guarantee. Production model files and runtime
wiring remain unchanged.

## Primary references

- https://onnxruntime.ai/docs/genai/tutorials/finetune.html
- https://onnxruntime.ai/docs/genai/reference/adapter.html
- https://onnxruntime.ai/docs/genai/api/csharp.html
- https://github.com/microsoft/onnxruntime-genai/releases/tag/v0.13.0

## Observed pre-registration outcome

The converter environment resolved, imported, converted both frozen 112-tensor
NPZ files with ONNX Runtime 1.23.0, and read both `.onnx_adapter` files back with
exact names, shapes, dtypes, values, and versions. Each Qwen3-0.6B synthetic
adapter file measured 2,305,600 bytes. This establishes adapter serialization
only.

The builder environment passed dependency resolution and top-level package
imports, but importing `onnxruntime_genai.models.builder` failed before argument
parsing. GenAI 0.13's Qwen builder imports
`Qwen3VLForConditionalGeneration`, which the frozen `transformers==4.51.0` does
not export. The raw stderr is retained at
`benchmark/runs/interactive-autocorrect/ACX-0022/builder-help-attempt-1.stderr.txt`
with SHA-256
`A5E7DD496A8B22AABE38B39163903C32536002ECCF84E176FA915B5D053231E3`.

Per the frozen failure rule, ACX-0022 does not change Transformers after this
observation. It makes no adapter-ready model, inference, DirectML, shared-memory,
latency, task-quality, or production claim. A successor experiment must freeze
and audit a new toolchain before reusing the retained source and synthetic
artifacts.
