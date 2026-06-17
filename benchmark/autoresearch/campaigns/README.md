---
name: readme-autoresearch-campaigns
description: "How to store individual autoresearch campaign folders."
type: module-readme
module: benchmark/autoresearch/campaigns
---

# `campaigns/`

Create one folder per optimization loop. Each campaign should carry its goal,
baseline, metric command, metric extraction rule, candidate scope, run log, and
results table.

Do not put ASR corpus studies here. If a campaign tunes an ASR prompt or judge,
store the ASR assets under `benchmark/asr/` and keep only the autoresearch loop
metadata here.
