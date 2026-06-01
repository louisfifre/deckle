# deckle-journal — entry examples

Three worked entries, one per epistemic status: a validated advance, an observation in progress, a methodological learning ready to promote.

**Validated technical advance.**

> ## 2026-05-27 — Refonte format artefacts agents (ADR-0013)
>
> Adoption du format canonique unifié pour tous les artefacts agents — `CLAUDE.md`, `SKILL.md`, ADRs, sheets `reference`/`research`, module READMEs. Le frontmatter YAML devient obligatoire (`name`, `description`, `type`), le vocabulaire d'H2 est fermé (Role, Context, Doctrine, Pointers, Boundaries, Examples), la convention RFC 2119 (MUST/SHOULD/MAY) cadre les paragraphes prescriptifs. Migration complète des artefacts existants livrée dans le merge `docs/refonte-format-artefacts-agents` ([c58a303](commit-sha)).
>
> Référence : [ADR-0013](docs/adr/0013-format-canonique-des-artefacts-agents.md). Le format normatif vit dans [`session-save-context/format.md`](.claude/skills/session-save-context/format.md).

**Observation in progress (hypothesis).**

> ## 2026-MM-DD — Bug intermittent sur le tray menu post-rebuild
>
> Pillule custom du tray menu disparaît sporadiquement après un rebuild complet, sans pattern reproductible identifié à ce stade. Soupçon initial : timing de chargement du `Style` custom vs activation du flyout. Hypothèse à confirmer en instrumentant le cycle de vie du flyout sur prochaine occurrence. Flagger ne suffit pas — la pillule sera ré-observée dans `Deckle.Shell` avant patch.

**Methodological learning ready to promote.**

> ## 2026-05-27 (suite) — Diagnostic vieillissant et règle « official sources first »
>
> Le pivot de stack `transformers + torch ROCm` → `torch-directml` de fin mai reposait sur un bug d'import `torch.distributed.tensor` qui avait été guardé upstream 9 mois plus tôt par [transformers PR #40038](https://github.com/huggingface/transformers/pull/40038). Le diagnostic interne avait vieilli silencieusement. La règle écrite en doctrine cross-project du `CLAUDE.md` racine (« Official sources first on a moving tech ») et appliquée par les agents recherche du 2026-05-27 a permis de retomber sur la voie viable.
>
> À promouvoir : cette règle est désormais explicite dans le `CLAUDE.md` racine, l'entrée journal devient un pointeur.
