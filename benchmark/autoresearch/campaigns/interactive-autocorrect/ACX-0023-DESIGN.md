# ACX-0023 — Qwen adapter builder toolchain successor

Date: 2026-07-30
Status: runtime tooling independently validated; ready for preregistration preparation

## Product question

Can the official ONNX Runtime GenAI 0.13 builder produce the adapter-ready
Qwen3 graph needed to test one shared local base with multiple task adapters,
after replacing only ACX-0022's refuted Transformers pin?

## Predecessor and reason for a new experiment

ACX-0022 closed before preregistration as a valid toolchain negative. Its
hash-locked builder environment resolved, but importing
`onnxruntime_genai.models.builder` failed because
`transformers==4.51.0` does not export `Qwen3VLForConditionalGeneration`.
Changing that version after observation is forbidden inside ACX-0022.

ACX-0023 is a separately frozen successor. It inherits the complete runtime,
tensor, numerical, lifecycle, timing, memory, negative-case, and claim contracts
from `ACX-0022-DESIGN.md`, exact SHA-256
`545F082F0F019AD9B5729552F37A60331694546E021ABA57EF54DDCF484D7DD5`
and 23,091 bytes. Any contradiction is resolved in favor of the explicit delta
below; no other inherited threshold or rule changes.

## Frozen toolchain delta

The new builder environment uses the same direct pins as ACX-0022 except:

- `transformers==4.57.1`, replacing the refuted `4.51.0`.

The complete direct pin set is therefore:

- CPython 3.13.14;
- `onnxruntime-genai-directml==0.13.0`;
- `onnxruntime-directml==1.24.4`;
- `numpy==2.2.6`;
- `onnx==1.22.0`;
- `onnx-ir==0.2.1`;
- `transformers==4.57.1`;
- `torch==2.12.1`;
- `peft==0.15.2`.

The choice is frozen from the Qwen3-VL project's published minimum
`transformers>=4.57.1`. It is a compatibility hypothesis, not a claim that the
whole GenAI builder supports that release. All resolved transitive wheels are
downloaded before environment creation, converted into a complete hash-locked
manifest, and installed offline with `--require-hashes`. No dependency version
is changed after the first full builder import attempt.

The ACX-0022 converter environment remains the serialization authority:
ONNX Runtime DirectML 1.23.0, lock SHA-256
`5DA091F44A423ABE8AC54D9ACDA6D9CF9083A5C9D0D34316B0CEA788D5389985`.
Its two 112-tensor round trips already passed exact name, shape, dtype, value,
format-version, adapter-version, and model-version checks.

## Frozen import gate before preregistration

No export is preregistered or started until a fresh ACX-0023 builder environment
passes all of these in one unchanged environment:

1. `pip check` reports no broken requirements;
2. import `onnxruntime_genai.models.builder`;
3. import `onnxruntime_genai.models.builders.qwen`;
4. import `Qwen3ForCausalLM`, `Qwen2_5_VLForConditionalGeneration`, and
   `Qwen3VLForConditionalGeneration` from Transformers;
5. import PEFT and construct the frozen `LoraConfig` without a model;
6. execute the exact installed `builder.py --help` with exit code zero and
   non-empty usage output;
7. retain `pip freeze --all`, every wheel hash, Python executable hash, builder,
   base-builder, Qwen-builder, and quantizer source hashes, stdout, stderr, and
   command exit code.

Any failure closes the ACX-0023 builder toolchain as a valid negative. Imports
are not patched, optional model families are not deleted from the installed
builder, and versions are not advanced after observation.

## Observed import-gate result

The fresh hash-locked environment passed all frozen gates unchanged. `pip check`
reported no broken requirements; the GenAI builder, Qwen builder, all three
frozen Qwen-family Transformers classes, and PEFT configuration imported; and
the installed builder's `--help` command exited zero with 9,723 bytes of usage
output and empty stderr.

The retained environment record is
`benchmark/runs/interactive-autocorrect/ACX-0023/environment.json`, 3,336 bytes,
SHA-256
`4294CF3DCD30DACA2379D953770C949D9EF5D4FFF786B0AEEED939DC6F98BFD3`.
It identifies the 35-wheel offline hash-locked installation, Python executable,
four consumed GenAI source files, complete freeze, gate script, raw stdout and
stderr, and command exit codes. This result establishes builder import
compatibility for the frozen toolchain only; no export or inference has run.

## Reused frozen artifacts

ACX-0023 may reuse only artifacts whose bytes still match these retained
identities:

- Qwen3-0.6B source repository `Qwen/Qwen3-0.6B`, revision
  `c1899de289a04d12100db370d81485cdf75e47ca`, 10 files and 1,519,209,243
  bytes, with the per-file manifest retained in
  `benchmark/runs/interactive-autocorrect/ACX-0022/source-qwen3-0.6b.json`,
  1,663 bytes, SHA-256
  `8A98D09A5882408CEAC777F6CC596B12671CC09B0EB648266F4F8D42AB312D85`;
- generator script path
  `benchmark/autoresearch/campaigns/interactive-autocorrect/probes/ACX-0022-generate.py`,
  10,471 bytes, SHA-256
  `1DE876337F8B16B07EE01C041E2A9A504A6C18F7BF4365EB2843A4C1482A819E`;
- verifier script path
  `benchmark/autoresearch/campaigns/interactive-autocorrect/probes/ACX-0022-verify.py`,
  6,404 bytes, SHA-256
  `4CD3A4434138C19419B4FF6C5FE37D0918092E5D4E5897CB92F81B79C0DB00AC`;
- control NPZ path
  `benchmark/runs/interactive-autocorrect/ACX-0022/synthetic/phase-a-attempt-1/control-zero/parameters.npz`,
  2,330,438 bytes, SHA-256
  `894768E41A14E4501C18D38853E9BEF537A869BE79869506BBE27255428DF58D`;
- sentinel NPZ path
  `benchmark/runs/interactive-autocorrect/ACX-0022/synthetic/phase-a-attempt-1/sentinel-seeded/parameters.npz`,
  2,330,438 bytes, SHA-256
  `19AECF1FD79BB61E0DA750AF18BB4AA80D61258CD8ED364D9BF5F85AE2C45564`;
- control adapter path
  `benchmark/runs/interactive-autocorrect/ACX-0022/synthetic/phase-a-attempt-1/control-zero/control-zero.onnx_adapter`,
  2,305,600 bytes, SHA-256
  `15DDD18B5BB1FAD92D906777D6F2CD9C4E10C1E97C5FF44DED6A3932A1278990`;
- sentinel adapter path
  `benchmark/runs/interactive-autocorrect/ACX-0022/synthetic/phase-a-attempt-1/sentinel-seeded/sentinel-seeded.onnx_adapter`,
  2,305,600 bytes, SHA-256
  `CE5B6EA31B32E856EFFE33B7730C73F846658A09C97A1E2BB32FE38A3E74A5A9`.
- zero PEFT `adapter_config.json`, exact path
  `benchmark/runs/interactive-autocorrect/ACX-0022/synthetic/phase-a-attempt-1/control-zero/peft/adapter_config.json`,
  950 bytes, SHA-256
  `E90D19D207944D867ABB6157E8010637ADBFF84A9048DE802740A222CB126E12`;
- zero PEFT `adapter_model.safetensors`, exact path
  `benchmark/runs/interactive-autocorrect/ACX-0022/synthetic/phase-a-attempt-1/control-zero/peft/adapter_model.safetensors`,
  4,602,248 bytes, SHA-256
  `32AFEA8E6ED4AA89718E3E272EF3BCFC04CEC040090AFD3FD6B30BE29ED9ED8D`.

Every reused file is rehashed immediately before use. A mismatch invalidates
reuse; it is not repaired in place.

## Phase-A export after a passing import gate

Only after the import gate and a second independent GO may ACX-0023 preregister
one Qwen3-0.6B CPU int4 block-128 export. The builder receives:

- the exact local source path, with remote resolution disabled;
- the exact retained zero PEFT adapter;
- `int4` precision, `cpu` execution provider, block size 128, and accuracy
  level 4, making ACX-0022 Phase A's GenAI 0.13 CPU default explicit rather than
  introducing a second experimental delta;
- the exact ordered 112-node `int4_nodes_to_exclude` list inherited from
  ACX-0022 and regenerated from the 0.6B config;
- a new absent output directory retained for the full campaign.

Post-export inspection must establish exactly 112 excluded LoRA `MatMul` nodes
and exactly 112 float16 initializers with the 0.6B shapes, no other LoRA node or
initializer, and the expected int4 contract for every non-excluded base MatMul.
Only then may the unchanged .NET 1.23 consumer run the inherited Phase-A
adapter-manager lifecycle and isolation probe.

## Required validation before preregistration

- independent static audit of this delta and inheritance boundary;
- focused metadata-policy and contract tests;
- full `Deckle.Autocorrect.Tests`;
- global Debug x64 build with zero warnings and errors;
- clean tracked source head;
- exact raw plan with source, environment, script, NPZ, adapter, and prospective
  command hashes;
- append-only planned event before any builder execution beyond the import gate.

## Runtime implementation audit

The first implementation audit returned NO-GO with six blockers: declared-only
artifact identity, exception-agnostic runtime negatives, shape-unsafe numerical
separation, incomplete timing/resource retention, incomplete failure cleanup,
and a fake whose output followed the requested state label instead of the active
adapter.

The revised implementation now:

- hashes and sizes an exhaustive model-directory/artifact manifest immediately
  before runtime use and requires a separately retained ONNX graph inspection;
- inspects exactly 112 float16 LoRA MatMul paths and initializers plus 197 exact
  int4 base MatMul paths before the runtime command is eligible;
- freezes the eight malformed runtime artifacts and the exact native exception
  family, with duplicate-name and two unload lifecycle failures in the main run;
- provides a separate fresh-process cross-model-manager command and report;
- requires an exact logits shape, finite float16 values, base/control logit and
  candidate-score deltas at most 0.001, stable base/control candidate winner,
  same-state repeatability at most 0.001, and sentinel separation at least 0.01
  and ten times the repeatability floor;
- retains wall, process-CPU, and current-thread allocation samples for repeated
  phases, plus 50 ms process-memory samples, 250 ms quiescent endpoints, peaks,
  full-GC block starts, unflushed-cache labeling, and a no-model sampler control;
- freezes one fresh-process ordinal from one through five so five serial plans
  can retain chronological model-load samples without calling an in-process
  reload a fresh process;
- tracks loaded names and attempts every cleanup stage in dependency order even
  after an earlier cleanup failure, while retaining structured fatal reports;
- derives fake outputs from the actually active adapter and records activation
  order.

The focused build passed with zero warnings and errors. Twenty focused tests
passed, and the synthetic ONNX verifier accepted the exact 112/197 contract then
rejected an altered operation. These are implementation checks only. A second
independent static GO is still mandatory before preregistration or export.

The second implementation audit remained NO-GO. It found that the graph verifier
still masked duplicate names and did not prove int4 attributes, weight wiring, or
zero LoRA initializers; the eight malformed artifacts lacked semantic mutation
evidence; the candidate oracle read two raw last-row logits instead of the real
multi-token teacher-forced score; repeated timing blocks lacked their own full-GC
boundary and model-load CPU/allocation were serialized as synthetic zero; and
fatal/partial-construction cleanup evidence remained incomplete. The earlier
active-adapter fake defect was closed.

The second runtime revision now:

- requires graph-verification schema 2: the complete float `MatMul` and
  `MatMulNBits` name sets must be exact and duplicate-free; every int4 node must
  expose bits 4, block size 128, accuracy level 4, exact K/N dimensions, exact
  packed-weight and scale initializers, and exact outputs; every LoRA A/B branch
  must share the base activation, feed A into B, and join the base output through
  its named Add; all 112 float16 LoRA initializers must contain positive zero;
- parses the model directory's retained `genai_config.json`, resolves
  `model.decoder.filename` as a contained relative path, and requires that exact
  file to be the hashed graph accepted by the structural verifier;
- adds a converter-side generator/verifier for eight semantically distinct
  negatives: absent path, exact unreadable half-prefix, wrong model version,
  one wrong target name, one rank-seven tensor, one float32 tensor, one missing
  tensor, and one extra layer-28 tensor. A separately hashed verification manifest
  is a mandatory plan artifact and is rebound to every runtime-negative path;
- replaces last-row raw logits with the production scorer's teacher-forced
  construction: the plan freezes exact prompt and completion token arrays plus
  each discriminant start/end span; each fresh generator scores every token in
  that span by log-softmax, retains summed log probability, token count,
  normalized score, and winner, and requires both normalized and summed
  base/control deltas at most 0.001;
- requires a full GC exactly once before each repeated adapter load/unload,
  activation, and per-state first-forward block. The model-load timing sample now
  retains the resource sampler's measured operation wall time, process CPU, and
  current-thread allocation rather than literal zero values;
- attempts every constructor, request, runtime, cross-model, and unload cleanup
  independently, retains cleanup stage plus exception type, and retains a fatal
  stage. The cross-model negative now compares the same adapted output before and
  after the foreign-manager attempt under the 0.001 tolerance.

The revised probe and tests compile with zero warnings and errors. Twenty-four
focused Qwen-adapter tests pass; the graph-verifier self-test rejects duplicate,
extra-MatMul, attribute, LoRA-wiring, int4-weight-wiring, and non-zero-initializer
mutations; and the negative-artifact self-test generates and independently reads
all eight exact mutation families. These remain implementation and synthetic-
artifact checks only. No export, model load, inference, or runtime measurement is
authorized until the new stable snapshot receives an independent static GO and
is then preregistered.

The third implementation audit remained NO-GO. It found that an otherwise exact
LoRA A-to-B-to-Add branch could be disconnected from every graph output; frozen
candidate token arrays and score spans were not rebound to the literal candidate
strings through the runtime tokenizer; the cross-model report could mask a later
fatal exception behind the expected rejection and leaked a partially configured
`Config` if provider clearing failed; the BOS correction had invalidated the fake
fixture; and structurally incomplete JSON could escape as `NullReferenceException`.

The third runtime revision now:

- requires the base, A, and B outputs to have their exact sole LoRA consumers,
  requires every Add output to have a consumer, and proves a live path from every
  Add to a declared graph output; the verifier self-test now disconnects one Add
  branch and requires rejection;
- freezes both exact candidate literals, re-encodes `candidate + "\n"` through the
  runtime tokenizer, strips BOS exactly as production does, and recomputes the
  production `CandidateCompletionPlan` before accepting any frozen token or span;
- retains the expected cross-model rejection separately from any later fatal
  exception, reports both fatal type and stage, and protects partial `Config`
  disposal while preserving the original exception;
- updates the fake to model the 151643 BOS token and exact prompt/completion
  tokenization, with shape-consistent logits;
- rejects null identities, hashes, tensor/module lists, adapter/candidate/negative
  elements, and other incomplete plan JSON through ordinary fail-closed verdicts.

The graph-verifier and eight-negative self-tests pass. The focused probe and test
projects compile with zero warnings and errors, and all 26 focused Qwen-adapter
tests pass. These remain implementation and synthetic-artifact checks only. No
export, model load, inference, or runtime measurement is authorized until this
stable snapshot receives an independent static GO and is then preregistered.

The fourth implementation audit closed all five preceding blockers but returned
NO-GO on retained-logit geometry. The runner upcast and retained the complete
`[1, sequence, 151936]` tensor for 74 observations, including 60 timed forwards,
then compared them pairwise. At the frozen prompt geometry this would retain
several GiB, perform tens of billions of scalar differences, and invalidate the
timing and memory observations.

The fourth runtime revision keeps full tensor bytes only for two temporary
float16 comparison captures: the initial zero-control and seeded-sentinel
outputs. Their exact maximum absolute delta is computed once inside a helper;
only compact metadata leaves that helper, so the arrays are unreachable before
the repeated timing blocks and their mandatory full GC. Every other forward
retains only full-tensor SHA-256, shape, dtype, finiteness, element count, and
timings. Base/control equivalence and same-state repeatability now require exact
full-tensor byte identity, which is stricter than the frozen 0.001 ceiling; the
seeded control/sentinel comparison retains its exact numerical maximum and 0.01
floor. The fake records and tests require exactly two retained-comparison calls.

The focused test project still compiles with zero warnings and errors, and all
26 focused Qwen-adapter tests pass after this bounded-retention change. This is
still implementation evidence only; no model export, model load, inference, or
runtime measurement has run.

The fifth implementation audit closed F9's memory and complexity blocker but
found one residual report-semantic defect: a late control or sentinel fingerprint
drift invalidated the run while the initial control/sentinel delta remained
populated under a field named as the minimum across all pairs. The fifth revision
now nulls that separation evidence unless every compact control and sentinel
observation has exact full-tensor identity with its reference. A dedicated fake
injects exactly one late sentinel hash drift and requires both an invalid verdict
and null separation evidence. The final independent audit returned GO with no
remaining runtime-tooling blocker. All 27 focused Qwen-adapter tests and all 875
ordinary `Deckle.Autocorrect.Tests` pass; the global Debug x64 build completed
with zero warnings and errors. These results authorize preregistration
preparation only. They do not authorize or establish export, model load,
inference, adapter compatibility, latency, memory savings, or task quality.

## Claim boundary

Before export, ACX-0023 can establish only that the separately frozen builder
toolchain resolves and imports. A later valid Phase A can establish only CPU
adapter-ready export structure and the inherited managed API/lifecycle behavior
for Qwen3-0.6B. It cannot establish Qwen3-1.7B or DirectML compatibility, shared
native sessions, memory savings, latency fitness, task quality, autocorrection
quality, field behavior, end-to-end behavior, or production readiness. No
production model, reference, asset, setting, or behavior changes.

## Primary references

- https://github.com/QwenLM/Qwen3-VL
- https://huggingface.co/docs/transformers/model_doc/qwen3_vl
- https://onnxruntime.ai/docs/genai/tutorials/finetune.html
- https://onnxruntime.ai/docs/genai/reference/adapter.html
