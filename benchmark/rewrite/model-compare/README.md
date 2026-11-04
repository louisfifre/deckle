# model-compare — Claude models × prompt variants on the whole-dictation rewrite

Which Claude model, and which wording of the system prompt, keeps a dictation's
meaning best when the « Lissage » profile rewrites it whole? The study runs a
model × prompt matrix on real dictations from Deckle's telemetry corpus, has a
stronger reasoning call list the meaning changes in each rewrite, and
aggregates both.

Generations go through `src/Deckle.Llm`'s `ClaudeEngine`, the production path
(effort low, thinking at the SDK default). The judge calls the Anthropic SDK
directly at effort high, because finding a flipped negation in a long
dictation is reasoning work. Everything runs sequentially.

## Inputs

- **Dictations**: `%LOCALAPPDATA%\Deckle\telemetry\corpus\raw\<bucket>\corpus.jsonl`
  (`DECKLE_DATA_ROOT` overrides the root, as in the app). Runtime data: read at
  run time, written only to the git-ignored `results/`.
- **Production rewrites** (`--include-existing`): every
  `corpus\rewrite-<slug>-<profileId>\corpus.jsonl` line for the selected ids
  joins the matrix as source `deckle`, its prompt named
  `profile:<name>@<prompt_template_hash>`, without a new API call.
- **Prompt variants**: `--prompts <file>[,<file>]`, each named by its file name
  without extension; `--profile <name>` adds the live system prompt of that
  Deckle profile from `modules\llm\settings.json` as `profile:<name>`, byte for
  byte.
- **API key**: the one Deckle stores for its `claude` provider in the DPAPI
  vault, else `ANTHROPIC_API_KEY`. Never printed, never written.

`prompts/lissage-current.md` is a snapshot dated 2026-11-03 of profile
`ae19d8acca4b`, the profile in `settings.json` staying the prompt's single
home. The production rewrites in `rewrite-lissage-ae19d8acca4b` record prompt
hash `70492c35af5b7540`, but the live profile hashed to `357907638c7f22c2`
when the snapshot was taken: the profile changed after those rewrites, so the
`deckle` rows and `lissage-current` do not share a prompt. The snapshot
separates paragraphs with LF where the Settings text box stores bare CRs; use
`--profile Lissage` for the byte-exact live prompt.
`prompts/lissage-literal.md` is the same prompt plus a closing paragraph that
keeps unknown terms as dictated, forbids adding a negation or a comparison,
and keeps the hedges.

## Run

From this folder, build once (`dotnet build /m:4`), then run the exe —
`dotnet run` would build without the repository's build flags:

```
$exe = '.\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\ModelCompare.exe'
& $exe generate --models claude-opus-5-5,claude-sonnet-5-5 `
    --prompts prompts/lissage-current.md,prompts/lissage-literal.md `
    --ids <id>,<id> --include-existing --out results/<stamp>
& $exe judge --in results/<stamp>/generations.jsonl --judge-model claude-opus-5-5
& $exe summary --in results/<stamp>
```

Options: `--ids a,b,c` or `--bucket long --take N` (the N most recent of a
bucket; `--take` defaults to 5), `--profile <name>`, `--include-existing`,
`--out` (default `results/<yyyyMMdd-HHmmss>`), `--judge-model` (default
`claude-opus-5-5`). A transient failure (429, 5xx, network) is retried once,
then recorded as `error` on the line and the run goes on.

Every call spends real money; each command prints its cost per line and in
total. The per-MTok prices live in `Shared.cs` as a dated snapshot: check them
against the console before quoting a cost.

## Read

`results/<stamp>/` holds three files:

- `generations.jsonl`: one line per generation — `id`, `bucket`, `words_in`,
  `words_out`, `model`, `prompt`, `source` (`benchmark` or `deckle`), `text`,
  tokens, `total_ms`, `cost_usd`, or `error`.
- `judgements.jsonl`: the generation's key, the judge's parsed JSON
  (`term_substitutions`, `meaning_inversions`, `hedges_dropped`, `omissions`,
  `additions`, `verdict`), or `judge_raw` when the answer did not parse, plus
  the judge's tokens and cost.
- `summary.md`: one row per (model, prompt, source) with the judge's counts
  and verdicts, the word and character ratios, marker deltas computed in code
  (negations with and without « ne », hedges, per 100 words), latency and
  costs; then every finding, span by span, grouped by model and prompt.

The judge is a lead, not ground truth: read the spans before trusting a count.
The marker deltas are deterministic but blunt — « plus » counts its
non-negative uses, and restoring a dropped « ne » is normal cleaning, which is
why the column without « ne » is the one to watch.
