# model-compare — Claude models × prompt variants on the whole-dictation rewrite

Which Claude model, and which wording of the system prompt, keeps a dictation's
meaning best when the « Lissage » profile rewrites it whole? The study runs a
model × prompt matrix on real dictations from Deckle's telemetry corpus, has a
stronger reasoning call list the meaning changes in each rewrite, and
aggregates both.

Generations go through `src/Deckle.Llm`'s `ClaudeEngine`, the production path
(effort low, thinking at the SDK default, unless the campaign sets a reasoning
level). The judge calls the Anthropic SDK
directly at effort high, because finding a flipped negation in a long
dictation is reasoning work. The one-shot commands (`generate`, `judge`,
`summary`) run sequentially; a campaign (`run`) runs one lane per model.

## Campaigns: the whole matrix, repeated, judged in batches, exported

The second study measures the three production profiles on the models they
could run on, with every cell repeated so latency, cost and verdicts come with
their spread. A campaign file under `campaigns/` declares it:

```
{
  "name": "profiles-v2",
  "models": ["claude-haiku-5-5", "claude-sonnet-5-5", "claude-opus-5-5"],
  "iterations": 20,
  "take_per_bucket": 2,
  "cells": [ { "profile": "Lissage", "buckets": ["short", "medium"], "judge_note": "..." }, ... ],
  "dictations": {},
  "judge": { "model": "claude-opus-5-5", "batch": 5 }
}
```

- **cells**: each profile runs on the corpus buckets the app would send it
  (the app picks a profile by word count), never on sizes it is not meant
  for. `judge_note` tells the judge what the profile is allowed to do with
  order, so Arrangement's regrouping by theme is not read as an omission plus
  an addition. A cell runs the live prompt of its `profile` unless it names a
  `prompt_file`: then that wording runs under the same profile, recorded as
  the file's name (or the cell's `name`), with its own `judge_note`, so
  several wordings of one profile compare in one run
  (`campaigns/lissage-lean.json` runs the live Lissage beside two shorter
  candidates). Two cells never share a prompt key.
- **dictations**: `take_per_bucket` dictations per bucket, the ones closest
  to the bucket's median word count (ties to the most recent), so a « short »
  cell measures a typical short dictation and not the three-word one that
  happens to be last. `"dictations": {"short": ["<id>", ...]}` pins ids
  instead.
- **judge**: `batch` rewrites of the same cell (same dictation, profile and
  model, on separate iterations) go to the judge in one call with the raw read
  once; the judge is told to judge each against the raw and never against the
  others. Its answer is one judgement per rewrite, matched by number.

```
& $exe check
& $exe run --campaign campaigns/profiles-v2.json [--out results/<stamp>] [--only generate|judge|export]
& $exe export --in results/<stamp>
```

`check` opens the API with the vault key and lists the models served, billing
nothing. `run` freezes the resolved matrix (dictation ids, the live prompts
byte for byte with a hash) into `results/<stamp>/campaign.json`, generates,
judges and exports. It is resumable: rerun the same `--out` and every call
already paid is skipped (a row with an error is redone); `--only` runs one
step. One lane per model runs in parallel, sequential inside a lane, so a
model's latency column is never inflated by its own queue.

### Reasoning levels

`"reasoning": ["auto", "off", "medium"]` runs every model at each listed
level (`auto`, `off`, `low`, `medium`, `high`). Each (model, level) pair is an
arm with its own lane, and its rows record it as their model,
`claude-haiku-5-5@off`, so the judge batches, the summary and the export keep
the levels apart with no other change; the export adds a `reasoning` field
beside `model`. `auto` keeps the bare model name and sends what the app sends
today (effort low, adaptive thinking), so its rows compare with those of a
campaign that named no level. On Claude, `low` sends the same request as
`auto`. `off` turns thinking off where the model allows it (`disabled` on
Haiku 5.5, `between_tools` on Sonnet 5.5) and falls back to effort low on
Opus 5.5, which always thinks. Without a `reasoning` key a campaign runs as
before.

Thinking is billed as output whether or not its text comes back, so
`eval_tokens` counts it: set it beside `words_out` to see how much of a
rewrite's output was reasoning. `campaigns/reasoning-lissage.json` compares
three levels on Haiku and Sonnet before the app's default moves.

### Prompt caching

A campaign sends the same request many times, so it uses Anthropic's prompt
cache: a call stores its prompt for five minutes, and a later call that starts
with the same bytes reads it back at a fraction of the input price (a tenth on
Sonnet and Haiku, a twentieth on Opus 5.5) instead of paying it whole.

- **Rewrites**: when `iterations` is above 1, the whole request (system
  prompt and dictation) is cached. Inside a lane a cell's iterations run back
  to back, so the first writes the cache (a quarter above the input price)
  and the next ones read it. A call that read the cache skipped the prompt's
  processing, which the app never does since each dictation is new: its
  latency is shorter than production's. Each generation therefore carries
  `cache_read_tokens`, `cache_write_tokens` and `cache_hit`, and each cell
  `cache_hits` and `latency_cold_ms`, measured on the calls that read nothing;
  quote that one when comparing with the app. The app itself does not cache.
- **Judge**: its system prompt (shared by every call of a profile) and the
  raw dictation (shared by every batch of a dictation, across models) are
  cached ahead of the rewrites. A cell's batches run one after the other: the
  cache is readable only once the call writing it has answered, so batches
  sent together would all write and none would read.
- **Proposer** (`optimize`): not cached, since a whole round separates its calls.

`prompt_tokens` counts only the uncached input, as the API reports it; the
costs add the cache writes and reads at their own price.

`results/<stamp>/dashboard-data.json` is the hand-off to whoever builds the
dashboard: the frozen campaign and its prompts, the pricing snapshot, the
dictations with their text, one entry per generation (text, tokens, latency,
cost, code metrics — word and character ratios, paragraphs, `---`
separators, markdown marks, lexical retention of the speaker's 4+-letter
words, negation and hedge deltas — and its judgement with verdict, counts,
findings and judge cost), one aggregate per (profile, bucket, dictation,
model) cell (verdict counts, mean findings per category, latency mean/p50/p90
over all calls and over the cold ones, cache hits, cost totals) and the run
totals. `judge-batches.jsonl` keeps each batch
answer whole; `judgements.jsonl` flattens it one row per rewrite with the
batch's tokens, time and cost split evenly, so `summary` still reads it.

## Optimize: improve one profile's prompt under a budget

A campaign measures prompts; `optimize` improves one. A spec under
`optimize/` names the profile, the generating model, the buckets, the judge
note, the proposer model, a target score, a budget and a validation reserve:

```
& $exe optimize --spec optimize/arrangement-sonnet.json [--out results/opt-<name>]
```

Round 0 runs the live prompt (or `start_prompt_file`) on the training
dictations (`take_per_bucket` typical ones per bucket, `iterations_per_round`
times each) and scores it. Each later round asks the proposer for a revised
prompt from the best prompt so far, its score, the judge's findings merged
across that round's rewrites with their counts, and the history of earlier
candidates; the candidate runs the same matrix and becomes the best when it
scores higher. The loop stops at the target, at `max_rounds`, or when the
next round would pass `budget_usd` minus `validation_reserve_usd`. Then the
start prompt and the best one run on held-out dictations (the next typical
ones of each bucket, never seen by the loop), `validation_iterations` times
each: that comparison is the result to quote, the loop's own best being an
optimistic reading of twenty noisy rewrites.

The score: mean over the round's planned rewrites of 100 for a faithful
verdict, 50 for minor, 0 for unfaithful, 0 for a call the API refused, minus
10 for a rewrite longer than its raw or carrying markdown marks or
separators. Beside it, weighted findings per judged rewrite (3 per inversion,
2 per term substitution, 1 per dropped hedge, omission or addition).

`results/opt-<name>/` holds `dictations.json` (training and held-out ids),
`prompts/round-N.md` and `.json` (each candidate with the proposer's
rationale and cost), one campaign folder per round, `ledger.jsonl` (one line
per finished round), `validation/`, `best-prompt.md` and
`optimize-report.md`. Resumable: a restart skips finished rounds, resumes the
unfinished one and never re-asks the proposer for a candidate already on
disk. The proposer is told never to copy a finding's span nor allude to the
dictations' content: check the best prompt before it leaves the results
folder.

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
