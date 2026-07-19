# paragraph-gate — prompt-sample eval of the retaille service + diff gate

Slice 1 measurement for « Retaille de paragraphe » (framed 2026-07-19): does a
Ministral-class model at temperature 0, behind the real `ParagraphRewrite`
prompt, produce rewrites `RewriteDiffGate` accepts, at an offer-compatible
latency?

The study references `src/Deckle.Llm.Rewrite` and exercises the production
prompt, engine and gate end to end — nothing is reimplemented here.

## Run

From this folder, with a local Ollama serving a Ministral/Mistral-class model:

```
dotnet run -c Release
dotnet run -c Release -- --model ministral-8b --timeout-s 60
```

Options: `--endpoint` (default `http://localhost:11434/api/generate`),
`--model` (default: first local name containing `ministral`, then `mistral`),
`--samples` (default `samples.jsonl` here), `--timeout-s` (default 120),
`--prompt-file` (swap the system prompt without recompiling src — the
prompt-iteration loop; a winning variant is then promoted into
`ParagraphRewrite.SystemPrompt`, its single home).

One warm-up request pays the model load first and is excluded from the
aggregates: the offer question is about the warm path (the framed trigger
warms the model while a paragraph accumulates). Samples then run
sequentially — one local GPU, heavy jobs never in parallel.

## Read

Per sample: `offer` (accepted, non-empty diff), `identity` (nothing to
repair), `reject` (gate refused — violations listed), `error`. `expect` in
`samples.jsonl` is the desired outcome (`open` = no committed expectation);
misses are flagged `!` on the console.

Aggregates: outcome counts, latency p50/p95/max, rejection-ruling histogram.
Full rows land in `results/<stamp>-<model>.jsonl` (git-ignored) with the
rewritten text and every violation, for prompt/gate calibration passes.
