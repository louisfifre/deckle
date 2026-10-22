# Arborescence — Deckle
_Généré depuis `git ls-files` — ne pas éditer à la main._

```
├── .agents/
│   └── skills/
│       ├── deckle-bug-note/
│       │   └── SKILL.md  — deckle-bug-note Use when a confirmed bug has its cause established and its workstream is still…
│       ├── deckle-commits/
│       │   └── SKILL.md  — deckle-commits Use when you commit in Deckle, split a workstream into commits or read its hist…
│       ├── deckle-interface/
│       │   └── SKILL.md  — deckle-interface Use when you build or change anything a person sees in a Deckle window, from a…
│       ├── deckle-logging/
│       │   └── SKILL.md  — deckle-logging Use when you add or change an observation point in Deckle code, or write code t…
│       ├── deckle-modularite/
│       │   └── SKILL.md  — deckle-modularite Use when code is about to grow a module, add a new one, or split a file or a su…
│       ├── deckle-nomenclature/
│       │   └── SKILL.md  — deckle-nomenclature Use when you give a name to anything in the Deckle repository or change one, a…
│       ├── deckle-settings-ux/
│       │   ├── references/
│       │   │   └── controls-and-behaviour.md  — the control for each nature of value and the finer magnitude, path, fold, reset…
│       │   └── SKILL.md  — deckle-settings-ux Use when you add a setting, place one on a page or rework a settings surface, s…
│       ├── deckle-testing/
│       │   └── SKILL.md  — deckle-testing Use when you write a Deckle test or extend coverage, whatever its category, to…
│       ├── deckle-versioning/
│       │   └── SKILL.md  — deckle-versioning Use when you record a Deckle version, touch CHANGELOG.md or prepare release not…
│       ├── microsoft-docs/
│       │   └── SKILL.md  — microsoft-docs Find official Microsoft documentation and code samples. Use for Microsoft techn…
│       ├── ux-copy/
│       │   ├── references/
│       │   │   ├── patterns.md
│       │   │   ├── voice-and-tone.md
│       │   │   └── windows-fluent.md
│       │   └── SKILL.md  — ux-copy Write or review interface wording — CTAs, errors, empty states, confirmations,…
│       ├── ux-designer/
│       │   ├── rules/
│       │   │   ├── accessibility.md
│       │   │   ├── information-architecture.md
│       │   │   ├── interaction-design.md
│       │   │   ├── research.md
│       │   │   └── visual-design.md
│       │   └── SKILL.md  — ux-designer Design or review user research, flows, information architecture, wireframes, pr…
│       └── winui-app/
│           ├── agents/
│           │   └── openai.yaml
│           ├── assets/
│           │   └── winui.png
│           ├── references/
│           │   ├── _sections.md
│           │   ├── accessibility-input-and-localization.md
│           │   ├── build-run-and-launch-verification.md
│           │   ├── community-toolkit-controls-and-helpers.md
│           │   ├── controls-layout-and-adaptive-ui.md
│           │   ├── foundation-environment-audit-and-remediation.md
│           │   ├── foundation-setup-and-project-selection.md
│           │   ├── foundation-template-first-recovery.md
│           │   ├── foundation-winui-app-structure.md
│           │   ├── motion-animations-and-polish.md
│           │   ├── performance-diagnostics-and-responsiveness.md
│           │   ├── sample-source-map.md
│           │   ├── shell-navigation-and-windowing.md
│           │   ├── styling-theming-materials-and-icons.md
│           │   ├── testing-debugging-and-review-checklists.md
│           │   └── windows-app-sdk-lifecycle-notifications-and-deployment.md
│           ├── config.yaml
│           ├── LICENSE.txt
│           └── SKILL.md  — winui-app Develop, review, troubleshoot, or bootstrap WinUI 3 and Windows App SDK C# apps…
├── .claude/
│   └── skills
├── .github/
│   └── workflows/
│       └── update-readme-stats.yml
├── .vscode/
│   └── launch.json
├── benchmark/
│   ├── autoresearch/
│   │   ├── campaigns/
│   │   │   ├── interactive-autocorrect/
│   │   │   │   ├── PROTOCOL.md  — interactive-autocorrect-protocol Metric, latency, integrity, and data contract for the interactive autocorrect c…
│   │   │   │   └── README.md  — interactive-autocorrect-research Reproducible campaign for safe, local, interactive whole-sentence autocorrectio…
│   │   │   └── README.md  — readme-autoresearch-campaigns How to store individual autoresearch campaign folders.
│   │   └── README.md  — readme-autoresearch Generic autoresearch workspace for measurable iterative generation, editing, ju…
│   ├── lib/
│   │   ├── monitor/
│   │   │   ├── gpu_monitor.ps1
│   │   │   └── joiner.py
│   │   ├── __init__.py
│   │   ├── _base_compat.py
│   │   ├── env.py
│   │   ├── event_log.py
│   │   └── paths.py
│   ├── rewrite/
│   │   └── paragraph-gate/
│   │       ├── .gitignore
│   │       ├── ParagraphGate.csproj
│   │       ├── Program.cs
│   │       ├── README.md
│   │       └── samples.jsonl
│   ├── viewers/
│   │   ├── __init__.py
│   │   └── build_html.py
│   ├── Directory.Build.props
│   ├── Directory.Packages.props
│   └── README.md  — readme-benchmark Index for Deckle benchmark workspaces — routes autoresearch, paragraph-rewrite…
├── docs/
│   ├── adr/
│   │   ├── 0000-template.md  — Fill-in template for a Deckle ADR — copy it to start one, record no decision he…
│   │   ├── 0001-anytype-headless-service-single-http-mcp-host.md  — Anytype runs as a Deckle-orchestrated headless service behind one HTTP MCP host…
│   │   ├── 0002-anytype-app-supervised-headless-backend.md  — Deckle supervises Anytype's headless backend from the resident app, in the inte…
│   │   ├── 0003-anytype-specialized-spaces-versioned-plans.md  — Deckle provisions specialized Anytype spaces from versioned composed plans and…
│   │   ├── 0004-reconcile-anytype-backend-by-listener-owner.md  — Deckle reconciles one externally versioned Anytype provider by proving which tr…
│   │   └── 0005-serve-anytype-mcp-stateless-with-explicit-recovery.md  — The resident Anytype gateway uses stateless MCP requests and advertises an expl…
│   ├── research/
│   │   ├── taskbar-briefs-2026-10-18/
│   │   │   ├── brief-csharp-socle.md
│   │   │   ├── brief-native-foundation.md
│   │   │   ├── brief-tier2-xaml.md
│   │   │   ├── brief-tier3-symbols.md
│   │   │   └── taskbar-research-2026-10-17.md
│   │   ├── taskbar-probes/
│   │   │   ├── bar-2026-10-18-native-small-left.webp
│   │   │   ├── clock-uia.ps1
│   │   │   ├── explorer-restart.ps1
│   │   │   ├── feature-flags.ps1
│   │   │   ├── late-inject-repro.ps1
│   │   │   ├── logon-capture.ps1
│   │   │   ├── native-test.ps1
│   │   │   ├── pe-keys2.ps1
│   │   │   ├── README.md
│   │   │   ├── taskbar-accent.ps1
│   │   │   ├── taskbar-icon-size-native-vertical.patch
│   │   │   ├── taskbar-probe.ps1
│   │   │   ├── taskbar-strings.ps1
│   │   │   ├── tray-uia.ps1
│   │   │   ├── windhawk-build-mod.ps1
│   │   │   └── windhawk-install-mod.ps1
│   │   ├── research--cerveau-llm--2026-10-13.md  — Cadrage vérifié (2026-10-13) — un cerveau LLM dans Deckle.Llm : les crédits API…
│   │   ├── research--settings-ux--2026-10-13.md  — Recherche vérifiée (2026-10-13), la direction Settings de Louis passée au cribl…
│   │   └── research--taskbar-sans-windhawk--2026-10-08.md  — Recherche vérifiée (2026-10-08) — remplacer Windhawk par un module Deckle pour…
│   ├── inventaire-exposables.csv
│   ├── inventaire-exposables.md
│   └── transcription-audio-chain.md  — How a dictation travels from the hotkey to the pasted text — vocabulary, three…
├── logo/
│   ├── primary.png
│   ├── primary.svg
│   └── secondary.png
├── scripts/
│   ├── commands/
│   │   ├── maintenance/
│   │   │   ├── check-script-headers.ps1  — Check script headers Lists the scripts the menu hides and the headers that are voided. A voided, inv…
│   │   │   ├── clean.ps1  — Clean build outputs Remove generated build output from a Deckle worktree without touching anything…
│   │   │   ├── inspect-context.ps1  — Context inventory Inventory the repository's tracked Markdown context, grouped by loading mode, w…
│   │   │   ├── README.md  — Maintenance Statistics, cleanup, AI sessions and checks of the repository.
│   │   │   ├── reset-agent-state.ps1  — Inspect or reset local AI sessions Build a cleanup plan for the local Codex and Claude Code session data of the cu…
│   │   │   ├── stats.ps1  — Repository statistics Walk every project under src\ and build one file inventory per Deckle module, w…
│   │   │   ├── stop-build-servers.ps1  — Stop .NET build servers Stop the MSBuild and Roslyn build servers left behind by dotnet builds. They sh…
│   │   │   ├── survey-anytype-v2.ps1
│   │   │   ├── sweep-exposables.ps1  — Sweep exposable source values Text sweep of the exposables carried by the source tree. An exposable is a code…
│   │   │   └── validate-resources.ps1  — Validate WinUI resources Read-only audit of the WinUI resource inventories. Reports missing resource key…
│   │   ├── release/
│   │   │   ├── build-anytype-backend.ps1  — Build the Anytype backend Rebuilds one anytype-cli release tag from source for Windows with the tantivy-g…
│   │   │   ├── publish-app.ps1  — Prepare or publish the app release Builds the two artifacts a Deckle GitHub Release carries: the installer stub an…
│   │   │   ├── publish-native-runtime.ps1  — Prepare or publish the native runtime Packages a versioned zip of the native DLLs that Deckle's first-run wizard down…
│   │   │   └── README.md  — Release Prepare and publish the app and the native runtime.
│   │   ├── setup/
│   │   │   ├── bootstrap-dev-env.ps1  — Bootstrap the dev environment Brings a fresh Windows 11 machine up to speed for building and running Deckle.…
│   │   │   ├── install-hooks.ps1  — Install git hooks Install Deckle's repository and global git hooks. Run once after a clone: hook…
│   │   │   ├── README.md  — Setup Set up the machine and the repository.
│   │   │   └── setup-assets.ps1  — Set up runtime assets Populates the native and models folders under the user data root with the whisp…
│   │   ├── build-run.ps1  — Build the app Build Deckle for the chosen configuration (x64) and, unless told not to, launch…
│   │   ├── changelog.ps1  — Update the changelog Generate the changelog and release notes from the git history. The Conventional…
│   │   ├── cut-version.ps1
│   │   ├── fetch-autocorrect-data.ps1
│   │   ├── launch-app.ps1  — Launch the freshest built app Replace the running Deckle instance with the freshest already-built executable…
│   │   ├── record-release.ps1
│   │   ├── record-version.ps1  — Record an internal version Record internal progress without publishing a release. A version record bumps t…
│   │   └── update-readme-stats.ps1  — Update the README pulse Regenerate the small "Development pulse" section in README.md from the local Gi…
│   ├── hooks/
│   │   ├── pre-commit
│   │   ├── update-tree.ps1  — Regenerate TREE.md from git ls-files Serves the pre-commit hook and direct manual runs. Annotates Markdown files wit…
│   │   └── validate-commit-attribution.ps1  — Enforce Deckle's sole maintainer identity and reject agent attribution markers.
│   ├── lib/
│   │   ├── agent-state-maintenance/
│   │   │   ├── sanitization.ps1
│   │   │   └── sqlite-cleanup.ps1
│   │   ├── deckle-preview/
│   │   │   ├── catalog.ps1
│   │   │   ├── discovered-catalog.ps1
│   │   │   ├── flows.ps1
│   │   │   └── statistics-preparation.ps1
│   │   ├── launcher/
│   │   │   ├── action-log.ps1
│   │   │   ├── action-results.ps1
│   │   │   ├── actions.ps1
│   │   │   ├── context.ps1
│   │   │   ├── maintenance-results.ps1
│   │   │   ├── menu-layout.ps1
│   │   │   ├── menus.ps1
│   │   │   ├── statistics-menus.ps1
│   │   │   └── statistics-plans.ps1
│   │   ├── menu/
│   │   │   ├── chrome.ps1
│   │   │   ├── grid-layout.ps1
│   │   │   ├── grid-picker.ps1
│   │   │   ├── grid-status.ps1
│   │   │   ├── input.ps1
│   │   │   ├── list-picker.ps1
│   │   │   ├── session.ps1
│   │   │   ├── status-view.ps1
│   │   │   └── text-input.ps1
│   │   ├── script-discovery/
│   │   │   ├── catalog.ps1
│   │   │   ├── front-matter.ps1
│   │   │   ├── header.ps1
│   │   │   └── key-value-lines.ps1
│   │   ├── terminal-interaction/
│   │   │   ├── contracts.ps1
│   │   │   ├── execution-layout.ps1
│   │   │   ├── header-layout.ps1
│   │   │   ├── host.ps1
│   │   │   ├── interaction.ps1
│   │   │   ├── layout.ps1
│   │   │   ├── navigation-layout.ps1
│   │   │   ├── navigation.ps1
│   │   │   ├── preparation-layout.ps1
│   │   │   ├── preparation.ps1
│   │   │   ├── rendering.ps1
│   │   │   └── theme.ps1
│   │   ├── action-summary.ps1
│   │   ├── agent-state-maintenance.psm1
│   │   ├── agent-state-maintenance.strings.psd1
│   │   ├── build-server-cleanup.ps1
│   │   ├── context-inventory.psm1
│   │   ├── deckle-process.ps1
│   │   ├── menu.psm1
│   │   ├── native-console.ps1
│   │   ├── native-runtime-release.psm1
│   │   ├── release-history.psm1
│   │   ├── release-validation.psm1
│   │   ├── repository-inventory.psm1
│   │   ├── resource-inventory.psm1
│   │   ├── script-discovery.psm1
│   │   ├── script-output.ps1
│   │   ├── source-metrics.psm1
│   │   └── terminal-interaction.psm1
│   ├── tests/
│   │   ├── BugExamples/
│   │   │   ├── build-only-stops-resident.json
│   │   │   ├── dotnet-max-node-response-precedence.json
│   │   │   ├── repository-portability.json
│   │   │   └── terminal-cursor-positioning.json
│   │   ├── commands/
│   │   │   ├── changelog.tests.ps1
│   │   │   ├── ChangelogBugNotes.md
│   │   │   ├── check-script-headers.tests.ps1
│   │   │   ├── CommitAttributionBugNotes.md
│   │   │   ├── install-hooks.tests.ps1
│   │   │   ├── publish-native-runtime.tests.ps1
│   │   │   ├── update-readme-stats.tests.ps1
│   │   │   └── versioning-workflow.tests.ps1
│   │   ├── fixtures/
│   │   │   └── script-discovery/
│   │   │       ├── tools/
│   │   │       │   ├── deep/
│   │   │       │   │   └── zeta.ps1  — Zeta tool
│   │   │       │   ├── epsilon.ps1  — Epsilon tool
│   │   │       │   └── README.md  — Tooling Tools that are rarely used.
│   │   │       ├── zone/
│   │   │       │   └── eta.ps1  — Eta zone task
│   │   │       ├── alpha.ps1  — Alpha task Runs the alpha task over two lines.
│   │   │       ├── array-set.ps1  — Array set Takes a multi-value scope filter.
│   │   │       ├── beta.ps1  — Beta build Builds the beta target in the chosen configuration.
│   │   │       ├── delta.ps1  — Delta care Takes care of delta.
│   │   │       ├── dot-word.ps1
│   │   │       ├── gamma.ps1  — Gamma package Packs or ships the gamma package.
│   │   │       ├── glued-requires.ps1
│   │   │       ├── no-header.ps1
│   │   │       ├── theta.ps1  — Axe delta Removes delta for good.
│   │   │       └── two-axes.ps1
│   │   ├── launcher/
│   │   │   ├── action-log.tests.ps1
│   │   │   ├── action-results.tests.ps1
│   │   │   ├── ActionResultBugNotes.md
│   │   │   ├── actions.tests.ps1
│   │   │   ├── context.tests.ps1
│   │   │   ├── maintenance-results.tests.ps1
│   │   │   ├── menu-layout.tests.ps1
│   │   │   ├── menus.tests.ps1
│   │   │   ├── statistics-menus.tests.ps1
│   │   │   └── statistics-plans.tests.ps1
│   │   ├── lib/
│   │   │   ├── action-summary.tests.ps1
│   │   │   ├── agent-skills-layout.tests.ps1
│   │   │   ├── agent-state-maintenance.tests.ps1
│   │   │   ├── context-inventory.tests.ps1
│   │   │   ├── deckle-process.tests.ps1
│   │   │   ├── native-console.tests.ps1
│   │   │   ├── native-runtime-release.tests.ps1
│   │   │   ├── release-history.tests.ps1
│   │   │   ├── release-validation.tests.ps1
│   │   │   ├── repository-inventory.tests.ps1
│   │   │   ├── resource-inventory.tests.ps1
│   │   │   ├── script-discovery.tests.ps1
│   │   │   ├── script-layout.tests.ps1
│   │   │   ├── script-output.tests.ps1
│   │   │   └── source-metrics.tests.ps1
│   │   ├── menu/
│   │   │   ├── chrome.tests.ps1
│   │   │   ├── grid-picker.tests.ps1
│   │   │   ├── grid-status.tests.ps1
│   │   │   ├── input.tests.ps1
│   │   │   ├── list-picker.tests.ps1
│   │   │   ├── status-view.tests.ps1
│   │   │   └── text-input.tests.ps1
│   │   └── terminal-interaction/
│   │       ├── contracts.tests.ps1
│   │       ├── discovered-catalog.tests.ps1
│   │       ├── flows.tests.ps1
│   │       ├── host.tests.ps1
│   │       ├── HostBugNotes.md
│   │       ├── layout.tests.ps1
│   │       ├── LayoutBugNotes.md
│   │       ├── navigation.tests.ps1
│   │       ├── NavigationBugNotes.md
│   │       ├── preparation.tests.ps1
│   │       ├── RenderingBugNotes.md
│   │       ├── SemanticBugNotes.md
│   │       └── theme.tests.ps1
│   ├── build-locked.ps1  — Run one dotnet build or test under the machine-wide Deckle build lock. AGENTS.md forbids two builds at once on this machine, whatever the session. Par…
│   ├── deckle-preview.ps1
│   ├── deckle.ps1
│   ├── GLOSSARY.md  — Deckle Scripts interaction vocabulary. It names the launcher's choices, action…
│   ├── README.md  — readme-scripts Dev workflows entry point for Deckle: the deckle.ps1 menu, the commands under c…
│   ├── resource-validation.allowlist.json
│   └── TERMINAL-INTERACTION-SPEC.md  — Behavior and composition contracts of the reusable terminal launcher beneath De…
├── src/
│   ├── Deckle.Anytype/
│   │   ├── Api/
│   │   │   ├── V2/
│   │   │   │   ├── Ops/
│   │   │   │   │   ├── AnytypeV2Block.cs
│   │   │   │   │   ├── AnytypeV2BlockOps.cs
│   │   │   │   │   ├── AnytypeV2BlockRef.cs
│   │   │   │   │   ├── AnytypeV2ObjectOps.cs
│   │   │   │   │   ├── AnytypeV2Op.cs
│   │   │   │   │   ├── AnytypeV2OpBatch.cs
│   │   │   │   │   ├── AnytypeV2Placement.cs
│   │   │   │   │   ├── AnytypeV2PropertyChanges.cs
│   │   │   │   │   ├── AnytypeV2PropertyFormat.cs
│   │   │   │   │   └── AnytypeV2TypeOps.cs
│   │   │   │   ├── AnytypeV2Client.cs
│   │   │   │   ├── AnytypeV2Exception.cs
│   │   │   │   ├── AnytypeV2Issue.cs
│   │   │   │   ├── AnytypeV2ObjectDocument.cs
│   │   │   │   ├── AnytypeV2ObjectReader.cs
│   │   │   │   ├── AnytypeV2ObjectReadOptions.cs
│   │   │   │   ├── AnytypeV2PropertyMap.cs
│   │   │   │   ├── AnytypeV2Request.cs
│   │   │   │   ├── AnytypeV2Response.cs
│   │   │   │   ├── AnytypeV2Searcher.cs
│   │   │   │   ├── AnytypeV2WriteOptions.cs
│   │   │   │   ├── AnytypeV2Writer.cs
│   │   │   │   └── AnytypeV2WriteReceipt.cs
│   │   │   ├── AnytypeApiClient.Chats.cs
│   │   │   ├── AnytypeApiClient.cs
│   │   │   ├── AnytypeApiClient.Files.cs
│   │   │   ├── AnytypeApiClient.Schema.cs
│   │   │   ├── AnytypeCredentials.cs
│   │   │   ├── AnytypeRequestGate.cs
│   │   │   └── SpaceWriteLock.cs
│   │   ├── Backend/
│   │   │   ├── BackendEndpointTrust.cs
│   │   │   ├── BackendHealthProbe.cs
│   │   │   ├── BackendInstallation.cs
│   │   │   ├── BackendListenerOwner.cs
│   │   │   ├── BackendProcess.cs
│   │   │   ├── BackendProcessSpec.cs
│   │   │   ├── BackendProviderPublicationLease.cs
│   │   │   ├── BackendProviderStore.cs
│   │   │   ├── BackendReconciler.cs
│   │   │   ├── BackendReconciliationLease.cs
│   │   │   ├── BackendSupervisor.cs
│   │   │   └── BackendTime.cs
│   │   ├── Dialogues/
│   │   │   └── DialogueGestures.cs
│   │   ├── Gestures/
│   │   │   ├── BlockSections.cs
│   │   │   ├── CollectionMembershipGestures.cs
│   │   │   ├── DeletionApproval.cs
│   │   │   ├── DocumentGestures.cs
│   │   │   ├── LinkedNames.cs
│   │   │   ├── LiveTagResolver.cs
│   │   │   ├── ManagementGestures.cs
│   │   │   ├── MarkdownBody.cs
│   │   │   ├── ObjectListing.cs
│   │   │   ├── ProjectGestures.cs
│   │   │   ├── QueryGestures.cs
│   │   │   ├── ReadGestures.cs
│   │   │   ├── Resolution.cs
│   │   │   ├── SchemaAdminGestures.cs
│   │   │   ├── SchemaApiJson.cs
│   │   │   ├── SchemaIcons.cs
│   │   │   ├── SchemaManifest.cs
│   │   │   ├── SchemaModels.cs
│   │   │   ├── SchemaPlanner.cs
│   │   │   ├── SchemaSnapshotReader.cs
│   │   │   ├── SelectValueGestures.cs
│   │   │   ├── SessionGestures.cs
│   │   │   └── TaskGestures.cs
│   │   ├── Schema/
│   │   │   ├── AnytypeSpaceAliases.cs
│   │   │   └── DevSpace.cs
│   │   ├── Deckle.Anytype.csproj
│   │   ├── DeckleAnytypeSource.cs
│   │   └── GLOSSARY.md  — glossary-deckle-anytype Anytype integration vocabulary, Deckle.Anytype.Mcp included. It separates the b…
│   ├── Deckle.Anytype.Mcp/
│   │   ├── Http/
│   │   │   ├── McpClients.cs
│   │   │   ├── McpClientTokens.cs
│   │   │   ├── McpHostLogBridge.cs
│   │   │   ├── McpHttpHost.cs
│   │   │   ├── McpJsonSchemaContract.cs
│   │   │   ├── McpRequestRateLimit.cs
│   │   │   └── McpSurfaceProtocolAdapter.cs
│   │   ├── Tools/
│   │   │   ├── AnytypeUtilityToolCatalog.cs
│   │   │   ├── DialogueToolCatalog.cs
│   │   │   ├── ManagementToolCatalog.cs
│   │   │   ├── SchemaAdminToolCatalog.cs
│   │   │   ├── ToolCatalog.cs
│   │   │   ├── ToolDescriptor.cs
│   │   │   └── ToolExecutionContract.cs
│   │   ├── Deckle.Anytype.Mcp.csproj
│   │   ├── DeckleAnytypeMcpSource.cs
│   │   ├── McpSurface.cs
│   │   ├── McpToolset.cs
│   │   └── ToolProfile.cs
│   ├── Deckle.App/
│   │   ├── Assets/
│   │   │   ├── Fonts/
│   │   │   │   └── BitcountSingle.ttf
│   │   │   ├── Icons/
│   │   │   │   ├── recording--indicator--false--16px.ico
│   │   │   │   ├── recording--indicator--false--16px.png
│   │   │   │   ├── recording--indicator--false--32px.ico
│   │   │   │   ├── recording--indicator--false--32px.png
│   │   │   │   ├── recording--indicator--true--16px.ico
│   │   │   │   ├── recording--indicator--true--16px.png
│   │   │   │   ├── recording--indicator--true--32px.ico
│   │   │   │   └── recording--indicator--true--32px.png
│   │   │   └── Sounds/
│   │   │       └── speech.wav
│   │   ├── Diagnostics/
│   │   │   ├── AppDiagnosticsBootstrap.cs
│   │   │   ├── AppHudFeedbackSink.cs
│   │   │   ├── DeckleAppSource.Ambient.cs
│   │   │   ├── DeckleAppSource.Anytype.cs
│   │   │   ├── DeckleAppSource.Autocorrect.cs
│   │   │   ├── DeckleAppSource.BootAndStatus.cs
│   │   │   ├── DeckleAppSource.CommandLine.cs
│   │   │   ├── DeckleAppSource.Crash.cs
│   │   │   ├── DeckleAppSource.CrashDumps.cs
│   │   │   ├── DeckleAppSource.cs
│   │   │   ├── DeckleAppSource.DictationRecovery.cs
│   │   │   ├── DeckleAppSource.Hotkeys.cs
│   │   │   ├── DeckleAppSource.Input.cs
│   │   │   ├── DeckleAppSource.Lifecycle.cs
│   │   │   ├── DeckleAppSource.Modules.cs
│   │   │   ├── DeckleAppSource.Setup.cs
│   │   │   ├── DeckleAppSource.Shutdown.cs
│   │   │   ├── DeckleAppSource.Surfaces.cs
│   │   │   ├── LaunchContextProbe.cs
│   │   │   ├── LogEntry.cs
│   │   │   ├── LogEntryRow.xaml
│   │   │   ├── LogEntryRow.xaml.cs
│   │   │   ├── LogEntryTemplateSelector.cs
│   │   │   └── NetworkStatusEmitter.cs
│   │   ├── Engine/
│   │   │   ├── AppAmbientEngineHost.cs
│   │   │   └── AppTranscriptionEngineHost.cs
│   │   ├── Strings/
│   │   │   └── en-US/
│   │   │       └── Resources.resw
│   │   ├── AnytypeNotifications.cs
│   │   ├── App.Ambient.cs
│   │   ├── App.Anytype.cs
│   │   ├── App.Autocorrect.cs
│   │   ├── App.Autocorrect.Lifecycle.cs
│   │   ├── App.FileTranscription.cs
│   │   ├── App.Hotkeys.Actions.cs
│   │   ├── App.Hotkeys.cs
│   │   ├── App.Input.cs
│   │   ├── App.Install.cs
│   │   ├── App.Lifetime.cs
│   │   ├── app.manifest
│   │   ├── App.MouseWheel.cs
│   │   ├── App.Orchestration.cs
│   │   ├── App.ParagraphRewrite.cs
│   │   ├── App.PrecisionScroll.cs
│   │   ├── App.Relocate.cs
│   │   ├── App.Startup.CommandLine.cs
│   │   ├── App.Startup.CrashDumps.cs
│   │   ├── App.Startup.DictationRecovery.cs
│   │   ├── App.Startup.Foundation.cs
│   │   ├── App.Startup.Modules.cs
│   │   ├── App.Startup.Settings.cs
│   │   ├── App.Startup.Shell.cs
│   │   ├── App.TaskbarCover.cs
│   │   ├── App.Theme.cs
│   │   ├── App.Trackpad.cs
│   │   ├── App.TranscriptPanel.cs
│   │   ├── App.TrayToggles.cs
│   │   ├── App.Update.cs
│   │   ├── App.Windows.cs
│   │   ├── App.xaml
│   │   ├── App.xaml.cs
│   │   ├── AppModules.cs
│   │   ├── AutocorrectNotifications.cs
│   │   ├── CrashSupervisor.cs
│   │   ├── Deckle.App.csproj
│   │   ├── global.json
│   │   ├── LogWindow.Chrome.cs
│   │   ├── LogWindow.Interaction.cs
│   │   ├── LogWindow.Model.cs
│   │   ├── LogWindow.Notices.cs
│   │   ├── LogWindow.xaml
│   │   ├── LogWindow.xaml.cs
│   │   ├── Program.cs
│   │   └── SecondaryWindowPlacement.cs
│   ├── Deckle.Audio/
│   │   ├── Internal/
│   │   │   ├── CaptureAnomalyEpisode.cs
│   │   │   ├── CaptureDataWatchdog.cs
│   │   │   ├── MediaFoundationInterop.cs
│   │   │   ├── Pcm16Buffer.cs
│   │   │   ├── PcmBuffer.cs
│   │   │   ├── PcmConversion.cs
│   │   │   ├── WaveDeviceInterop.cs
│   │   │   ├── WaveInBufferRing.cs
│   │   │   └── WaveInLoop.cs
│   │   ├── Preprocessing/
│   │   │   ├── Compressor.cs
│   │   │   ├── HighPassFilter.cs
│   │   │   ├── Limiter.cs
│   │   │   ├── MicLevelCheck.cs
│   │   │   ├── NoiseGate.cs
│   │   │   ├── PreprocessingSettings.cs
│   │   │   └── TranscriptionPreprocessor.cs
│   │   ├── Strings/
│   │   │   └── en-US/
│   │   │       └── Resources.resw
│   │   ├── Telemetry/
│   │   │   ├── MicrophoneCalibrationCalculator.cs
│   │   │   ├── MicrophoneTelemetryCalculator.cs
│   │   │   └── MicrophoneTelemetryPayload.cs
│   │   ├── AudioFileDecoder.cs
│   │   ├── AudioFileDecodeResult.cs
│   │   ├── AudioLevelMapper.cs
│   │   ├── CaptureFrame.cs
│   │   ├── CaptureResult.cs
│   │   ├── CaptureSettings.cs
│   │   ├── CaptureSettingsService.cs
│   │   ├── Deckle.Audio.csproj
│   │   ├── DeckleAudioSource.cs
│   │   ├── GLOSSARY.md  — glossary-deckle-audio Audio vocabulary. It separates display level from transcription pre-processing,…
│   │   ├── IAudioRecordingHost.cs
│   │   ├── IMicrophoneCapture.cs
│   │   ├── ListeningTake.cs
│   │   ├── ListeningTester.cs
│   │   ├── MicLevelTester.cs
│   │   ├── MicrophoneCapture.cs
│   │   ├── ProbeResult.cs
│   │   ├── RecordingPage.xaml
│   │   ├── RecordingPage.xaml.cs
│   │   ├── RecordingSettingsModule.cs
│   │   ├── RecordingTelemetryViewModel.cs
│   │   ├── RecordingViewModel.cs
│   │   ├── RecordingViewModel.ListeningTest.cs
│   │   ├── RecordingViewModel.Settings.cs
│   │   ├── SettingsSearch.cs
│   │   └── SpeakerOutput.cs
│   ├── Deckle.Autocorrect/
│   │   ├── Data/
│   │   │   ├── lexicon-en-globish.tsv.gz
│   │   │   ├── lexicon-en.tsv.gz
│   │   │   ├── lexicon-fr.tsv.gz
│   │   │   ├── pack-en-it.manifest.json
│   │   │   ├── pack-en-it.tsv.gz
│   │   │   ├── pack-fr-it.manifest.json
│   │   │   ├── pack-fr-it.tsv.gz
│   │   │   ├── pair-bigrams-fr.tsv.gz
│   │   │   └── verbs-fr.tsv.gz
│   │   ├── Engine/
│   │   │   ├── AutocorrectEngine.CaretRecovery.cs
│   │   │   ├── AutocorrectEngine.Commit.cs
│   │   │   ├── AutocorrectEngine.cs
│   │   │   ├── AutocorrectEngine.Input.cs
│   │   │   ├── AutocorrectEngine.Lifecycle.cs
│   │   │   ├── AutocorrectEngine.Telemetry.cs
│   │   │   ├── AutocorrectPolicySet.cs
│   │   │   ├── BackgroundRerankLane.cs
│   │   │   ├── BigramPairDisambiguator.cs
│   │   │   ├── CandidateSearchObservation.cs
│   │   │   ├── CasePattern.cs
│   │   │   ├── ClosedSentenceJudgment.cs
│   │   │   ├── CompositeAmbiguityProbe.cs
│   │   │   ├── CompositeCorrectionPolicy.cs
│   │   │   ├── ConservativeTypoCorrector.cs
│   │   │   ├── ConservativeTypoCorrector.Morphology.cs
│   │   │   ├── ConservativeTypoCorrector.Sentence.cs
│   │   │   ├── CorrectionDecision.cs
│   │   │   ├── CorrectionTrace.cs
│   │   │   ├── DiacriticsRestorer.cs
│   │   │   ├── ElisionCorrector.cs
│   │   │   ├── FrenchSentenceReranker.cs
│   │   │   ├── GenderVariantProbe.cs
│   │   │   ├── GrammarCorrector.cs
│   │   │   ├── IAmbiguityProbe.cs
│   │   │   ├── IPairDisambiguator.cs
│   │   │   ├── ISentenceReranker.cs
│   │   │   ├── ISentenceScorer.cs
│   │   │   ├── MistouchFamilyCorrector.cs
│   │   │   ├── QwertyAdjacency.cs
│   │   │   ├── RestorerOptions.cs
│   │   │   ├── SentenceCorpus.cs
│   │   │   ├── SentenceProposalGate.cs
│   │   │   ├── SentenceProposalVerifier.cs
│   │   │   ├── SentenceRerankCoordinator.cs
│   │   │   ├── TrainerReport.cs
│   │   │   ├── TypoOptions.cs
│   │   │   └── WordShape.cs
│   │   ├── Injection/
│   │   │   ├── InjectionPlan.cs
│   │   │   ├── ITextInjector.cs
│   │   │   └── TextInjector.cs
│   │   ├── Interop/
│   │   │   └── KeyboardStateInterop.cs
│   │   ├── Learning/
│   │   │   ├── IPersonalLexicon.cs
│   │   │   ├── MistouchFamilyStore.cs
│   │   │   ├── PersonalDictionary.cs
│   │   │   ├── PersonalDictionaryData.cs
│   │   │   ├── PersonalWordAdmission.cs
│   │   │   └── SurfaceProfileStore.cs
│   │   ├── Lexicon/
│   │   │   ├── AccentFolding.cs
│   │   │   ├── AccentIndex.cs
│   │   │   ├── AccentVariant.cs
│   │   │   ├── AutocorrectLexiconArtifacts.cs
│   │   │   ├── DomainActivation.cs
│   │   │   ├── DomainPack.cs
│   │   │   ├── DomainPackManifest.cs
│   │   │   ├── EffectiveLexicon.cs
│   │   │   ├── FrequencyLexicon.cs
│   │   │   ├── GlobalEnglishLexicon.cs
│   │   │   ├── IFrequencyLexicon.cs
│   │   │   ├── LexicalDomain.cs
│   │   │   ├── SystemLanguages.cs
│   │   │   └── VerbMorphology.cs
│   │   ├── Recovery/
│   │   │   ├── CaretParagraphContext.cs
│   │   │   ├── CaretSentenceContext.cs
│   │   │   └── CaretTextReader.cs
│   │   ├── Strings/
│   │   │   └── en-US/
│   │   │       └── Resources.resw
│   │   ├── Surfaces/
│   │   │   ├── FocusedSurface.cs
│   │   │   ├── ISurfaceProber.cs
│   │   │   └── SurfaceProber.cs
│   │   ├── Tracking/
│   │   │   ├── KeyDecoder.cs
│   │   │   ├── Keystroke.cs
│   │   │   ├── TypedWordTracker.cs
│   │   │   ├── TypingStream.cs
│   │   │   ├── WordBoundaries.cs
│   │   │   └── WordCommit.cs
│   │   ├── Ui/
│   │   │   ├── AppsEnrolledPage.xaml
│   │   │   ├── AppsEnrolledPage.xaml.cs
│   │   │   ├── AutocorrectPage.xaml
│   │   │   ├── AutocorrectPage.xaml.cs
│   │   │   ├── LexicalDomainsPage.xaml
│   │   │   └── LexicalDomainsPage.xaml.cs
│   │   ├── ViewModels/
│   │   │   ├── AppsEnrolledViewModel.cs
│   │   │   ├── AutocorrectAppRow.cs
│   │   │   ├── AutocorrectExcludedWordRow.cs
│   │   │   ├── AutocorrectTelemetryViewModel.cs
│   │   │   ├── AutocorrectViewModel.cs
│   │   │   ├── AutocorrectViewModel.Settings.cs
│   │   │   ├── DomainLanguageRow.cs
│   │   │   ├── LexicalDomainsViewModel.cs
│   │   │   └── LexicalDomainTab.cs
│   │   ├── AutocorrectSettings.cs
│   │   ├── AutocorrectSettingsModule.cs
│   │   ├── AutocorrectSettingsService.cs
│   │   ├── Deckle.Autocorrect.csproj
│   │   ├── DeckleAutocorrectSource.cs
│   │   ├── GLOSSARY.md  — glossary-deckle-autocorrect Autocorrect vocabulary. It covers where correction may act (surfaces, enrollmen…
│   │   └── SettingsSearch.cs
│   ├── Deckle.Autocorrect.Lab/
│   │   ├── Mining/
│   │   │   ├── MistouchFamilyReport.cs
│   │   │   └── MistouchMiner.cs
│   │   ├── PackReports/
│   │   │   ├── pack-en-it.judgments.tsv
│   │   │   ├── pack-en-it.md
│   │   │   ├── pack-fr-it.frequencies.tsv
│   │   │   ├── pack-fr-it.judgments.tsv
│   │   │   └── pack-fr-it.md
│   │   ├── Profiling/
│   │   │   ├── SurfaceProfiler.cs
│   │   │   └── SurfaceProfileReport.cs
│   │   ├── Replay/
│   │   │   ├── CorpusReader.cs
│   │   │   ├── MarginCalibration.cs
│   │   │   ├── ReplayRunner.cs
│   │   │   ├── SentenceAlignment.cs
│   │   │   ├── SentenceReplay.cs
│   │   │   └── TruthOverlay.cs
│   │   ├── DataSet.cs
│   │   ├── Deckle.Autocorrect.Lab.csproj
│   │   ├── DomainPackBuilder.cs
│   │   ├── HarvestData.cs
│   │   ├── HarvestFilter.cs
│   │   ├── HarvestStore.cs
│   │   ├── LexiconBuilder.Artifacts.cs
│   │   ├── LexiconBuilder.cs
│   │   ├── LexiconBuilder.English.cs
│   │   ├── LexiconBuilder.French.cs
│   │   ├── LexiconBuilder.Globish.cs
│   │   ├── MorphalouReader.cs
│   │   ├── PairModelTrainer.cs
│   │   ├── RestorationEvaluator.cs
│   │   └── RestorationReport.cs
│   ├── Deckle.Autocorrect.Mlm/
│   │   ├── CamembertAssets.cs
│   │   ├── CamembertMlmScorer.cs
│   │   ├── CamembertReranker.cs
│   │   ├── CamembertSentenceReranker.cs
│   │   ├── Deckle.Autocorrect.Mlm.csproj
│   │   └── MlmProbe.cs
│   ├── Deckle.Autocorrect.Onnx/
│   │   ├── CandidateCompletionPlan.cs
│   │   ├── Deckle.Autocorrect.Onnx.csproj
│   │   ├── OnnxSentenceScorer.BatchExperiment.cs
│   │   ├── OnnxSentenceScorer.cs
│   │   ├── OnnxSentenceScorer.Model.cs
│   │   ├── OnnxSentenceScorer.Prompt.cs
│   │   ├── OnnxSentenceScorer.Scoring.cs
│   │   ├── OnnxSentenceScoringProfile.cs
│   │   └── OnnxSlotReranker.cs
│   ├── Deckle.Autocorrect.Probe/
│   │   ├── AnticipationLeadOracleCommand.cs
│   │   ├── AnticipationTransactionJoinCommand.cs
│   │   ├── AutocorrectBenchmark.cs
│   │   ├── AutocorrectBenchmarkCommand.cs
│   │   ├── AutocorrectBenchmarkCorpus.cs
│   │   ├── AutocorrectItPackCorpus.cs
│   │   ├── BenchmarkKeyboardSession.cs
│   │   ├── CaretContextProbeCommand.cs
│   │   ├── CorrectionBenchmarkCase.cs
│   │   ├── CorrectionBenchmarkCommand.cs
│   │   ├── CorrectionBenchmarkCorpus.cs
│   │   ├── CorrectionBenchmarkResult.cs
│   │   ├── CorrectionBenchmarkSummary.cs
│   │   ├── Deckle.Autocorrect.Probe.csproj
│   │   ├── ModelPathResolver.cs
│   │   ├── ProbeArguments.cs
│   │   ├── Program.cs
│   │   ├── QwenAdapterArtifactGate.cs
│   │   ├── QwenAdapterCompatibilityCommand.cs
│   │   ├── QwenAdapterCompatibilityPlan.cs
│   │   ├── QwenAdapterCompatibilityProtocol.cs
│   │   ├── QwenAdapterCompatibilityRuntime.cs
│   │   ├── QwenAdapterCrossModelCommand.cs
│   │   ├── QwenAdapterGraphContract.cs
│   │   ├── QwenAdapterManifestGate.cs
│   │   ├── QwenAdapterResourceSampler.cs
│   │   ├── SentenceBatchExperimentCommand.cs
│   │   ├── SentenceBatchExperimentFixture.cs
│   │   ├── SentenceBatchTokenizationCommand.cs
│   │   ├── SentenceCalibrationCommand.cs
│   │   ├── SentenceCalibrationFixture.cs
│   │   ├── SentenceCanonicalLatencyCommand.cs
│   │   ├── SentenceDecisionInventory.cs
│   │   ├── SentenceDecisionInventoryCommand.cs
│   │   ├── SentenceDecisionInventoryEvaluation.cs
│   │   ├── SentenceOrderAblationCommand.cs
│   │   ├── SentenceOrderAblationFixture.cs
│   │   ├── SentenceProfileCommand.cs
│   │   ├── SentenceProfileFixture.cs
│   │   ├── SentenceUnanimityBundle.cs
│   │   ├── SentenceUnanimityBundleCommand.cs
│   │   ├── SentenceUnanimityBundleEvaluation.cs
│   │   ├── SentenceUnanimityMorphology.cs
│   │   ├── SingleProbeCommand.cs
│   │   └── StaleWorkProbeCommand.cs
│   ├── Deckle.Catalog/
│   │   ├── Composer/
│   │   │   ├── CubicBezier.cs
│   │   │   ├── CurveEditor.cs
│   │   │   ├── DefaultValueText.cs
│   │   │   ├── GestureHold.cs
│   │   │   ├── HotkeyChordText.cs
│   │   │   ├── HotkeyKeyNames.cs
│   │   │   ├── InfoBarLayout.cs
│   │   │   ├── IPathControl.cs
│   │   │   ├── MagnitudeGrain.cs
│   │   │   ├── PreviewBlock.cs
│   │   │   ├── Readout.cs
│   │   │   ├── Setting.cs
│   │   │   ├── SettingArgs.cs
│   │   │   ├── SettingDescriptor.cs
│   │   │   ├── SettingKind.cs
│   │   │   ├── SettingsComposer.Chrome.cs
│   │   │   ├── SettingsComposer.cs
│   │   │   ├── SettingsComposer.Curve.cs
│   │   │   ├── SettingsComposer.Folds.cs
│   │   │   ├── SettingsComposer.Hotkey.cs
│   │   │   ├── SettingsComposer.Inputs.cs
│   │   │   ├── SettingsComposer.Numeric.cs
│   │   │   ├── SettingsPageHeader.cs
│   │   │   └── SettingsSection.cs
│   │   ├── Hotkeys/
│   │   │   ├── HotkeyChord.cs
│   │   │   └── HotkeyModifiers.cs
│   │   ├── Themes/
│   │   │   ├── Icons.xaml
│   │   │   └── SettingsPageHeaderBrushes.xaml
│   │   ├── ConfirmationService.cs
│   │   ├── Deckle.Catalog.csproj
│   │   ├── DiagnosticsRegistry.cs
│   │   ├── Glyphs.cs
│   │   ├── Loc.cs
│   │   ├── SettingSearchEntry.cs
│   │   ├── SettingsModuleDescriptor.cs
│   │   ├── SettingsNavigation.cs
│   │   └── TelemetryConsent.cs
│   ├── Deckle.Chrono/
│   │   ├── ChronoFormatter.cs
│   │   ├── ChronoTimer.cs
│   │   ├── Deckle.Chrono.csproj
│   │   └── DeckleChronoSource.cs
│   ├── Deckle.Composition/
│   │   ├── Core/
│   │   │   ├── HudComposition.Animations.cs
│   │   │   ├── HudComposition.Config.cs
│   │   │   ├── HudComposition.ConicClonePreview.cs
│   │   │   ├── HudComposition.cs
│   │   │   ├── HudComposition.DigitReveal.cs
│   │   │   ├── HudComposition.Factories.cs
│   │   │   ├── HudComposition.NakedPreview.cs
│   │   │   ├── HudComposition.Painting.cs
│   │   │   ├── HudComposition.ProcessingStroke.cs
│   │   │   ├── HudComposition.UtteranceCountdownVisual.cs
│   │   │   ├── HudMaterial.cs
│   │   │   ├── ProcessingVariant.cs
│   │   │   ├── UtteranceCountdownConfig.cs
│   │   │   └── UtteranceCountdownCourse.cs
│   │   ├── Primitives/
│   │   │   └── ColorSpace.cs
│   │   └── Deckle.Composition.csproj
│   ├── Deckle.Core/
│   │   ├── Design/
│   │   │   ├── Dpi.cs
│   │   │   └── Spacing.cs
│   │   ├── Interop/
│   │   │   ├── NativeMethods.Audio.cs
│   │   │   ├── NativeMethods.cs
│   │   │   ├── NativeMethods.Input.cs
│   │   │   ├── NativeMethods.Shell.cs
│   │   │   ├── NativeMethods.WindowHost.cs
│   │   │   ├── NativeMethods.Windowing.cs
│   │   │   ├── Structs.cs
│   │   │   ├── UIAutomation.cs
│   │   │   ├── UIAutomation.Text.cs
│   │   │   ├── Win32Clipboard.cs
│   │   │   └── Win32Util.cs
│   │   ├── Io/
│   │   │   ├── Downloader.cs
│   │   │   └── ProvisioningResult.cs
│   │   ├── Paths/
│   │   │   ├── AppPaths.cs
│   │   │   └── DefaultDataRoot.cs
│   │   ├── CorpusPaths.cs
│   │   ├── Deckle.Core.csproj
│   │   └── JsonSettingsStore.cs
│   ├── Deckle.Diagnostics/
│   │   ├── Sinks/
│   │   │   ├── BoundedWriteQueue.cs
│   │   │   ├── HudFeedbackSink.cs
│   │   │   ├── JsonlAppendStream.cs
│   │   │   ├── JsonlLineSerializer.cs
│   │   │   ├── JsonlRotationPolicy.cs
│   │   │   ├── JsonlSchema.cs
│   │   │   ├── JsonlSink.cs
│   │   │   ├── LogWindowSink.cs
│   │   │   └── RoutedJsonlSink.cs
│   │   ├── CrashDumpCapture.cs
│   │   ├── CrashRelaunchBudget.cs
│   │   ├── CrashRelaunchPolicy.cs
│   │   ├── Deckle.Diagnostics.csproj
│   │   ├── DeckleCancellationSource.cs
│   │   ├── DeckleDiagnosticsSource.cs
│   │   ├── DeckleEventSource.cs
│   │   ├── DeckleNetworkSource.cs
│   │   ├── DeckleResourceSource.cs
│   │   ├── DeckleSettingsUxSource.cs
│   │   ├── DeckleThemeSource.cs
│   │   ├── DeckleThreadingSource.cs
│   │   ├── DeckleWindowingSource.cs
│   │   ├── DispatchEventListener.cs
│   │   ├── EventEntry.cs
│   │   ├── GLOSSARY.md  — glossary-deckle-diagnostics Observability vocabulary for the Diagnostics family, .Logging and .Telemetry in…
│   │   ├── IFlushableLogSink.cs
│   │   ├── IHudFeedbackSink.cs
│   │   ├── ILogSink.cs
│   │   ├── ILogWindowSink.cs
│   │   ├── Keywords.cs
│   │   ├── LogLineFormatter.cs
│   │   ├── ObservationKind.cs
│   │   ├── OperationalLogAdmission.cs
│   │   ├── WindowingProbe.cs
│   │   └── WorkId.cs
│   ├── Deckle.Diagnostics.Logging/
│   │   ├── Strings/
│   │   │   └── en-US/
│   │   │       └── Resources.resw
│   │   ├── Ui/
│   │   │   ├── Collections/
│   │   │   │   └── RangeObservableCollection.cs
│   │   │   └── Controls/
│   │   │       ├── LogFilterBar.xaml
│   │   │       └── LogFilterBar.xaml.cs
│   │   ├── AmbientCaptureGate.cs
│   │   ├── ApplicationLogSink.cs
│   │   ├── Deckle.Diagnostics.Logging.csproj
│   │   ├── DiagnosticsPage.xaml
│   │   ├── DiagnosticsPage.xaml.cs
│   │   ├── DiagnosticsSettingsModule.cs
│   │   ├── DiagnosticsViewModel.cs
│   │   ├── DiagnosticsViewModel.Settings.cs
│   │   ├── LogFilterDimension.cs
│   │   ├── LogFilterSelection.cs
│   │   ├── LogFilterToken.cs
│   │   ├── LoggingSettings.cs
│   │   ├── LoggingSettingsService.cs
│   │   ├── LogTransfer.cs
│   │   ├── LogWindowFilterSession.cs
│   │   ├── SettingsSearch.cs
│   │   └── TranscriptionActivityScope.cs
│   ├── Deckle.Diagnostics.Telemetry/
│   │   ├── Deckle.Diagnostics.Telemetry.csproj
│   │   ├── TelemetryListenerBootstrap.cs
│   │   ├── TelemetrySettings.cs
│   │   └── TelemetrySettingsService.cs
│   ├── Deckle.Home/
│   │   ├── Schema/
│   │   │   ├── README.md
│   │   │   ├── schema-annotations.json
│   │   │   └── schema-manifest.json
│   │   ├── Terms/
│   │   │   └── terms.fr.json
│   │   ├── Deckle.Home.csproj
│   │   ├── DeckleHomeSource.cs
│   │   ├── GLOSSARY.md  — glossary-deckle-home Home inventory vocabulary. It defines the inventory code and the Home surface,…
│   │   ├── HomeBodyEditor.cs
│   │   ├── HomeCode.cs
│   │   ├── HomeCollectionWriter.cs
│   │   ├── HomeGestures.cs
│   │   ├── HomeMcp.cs
│   │   ├── HomeObjects.cs
│   │   ├── HomePropertyWriter.cs
│   │   ├── HomeRelationEdits.cs
│   │   ├── HomeRequests.cs
│   │   ├── HomeRuntimeProvider.cs
│   │   ├── HomeSchema.cs
│   │   ├── HomeSchemaRuntime.cs
│   │   ├── HomeSchemaTargetData.cs
│   │   ├── HomeSearch.cs
│   │   ├── HomeTerms.cs
│   │   ├── HomeToolCatalog.cs
│   │   ├── HomeTypedToolCatalog.cs
│   │   └── OPERATIONS.md
│   ├── Deckle.Hud/
│   │   ├── Chrono/
│   │   │   ├── GLOSSARY.md  — glossary-deckle-hud-chrono Chrono HUD element vocabulary. It names the face's eight elements and the accen…
│   │   │   ├── HudChrono.Clock.cs
│   │   │   ├── HudChrono.Reveal.cs
│   │   │   ├── HudChrono.RevealMask.cs
│   │   │   ├── HudChrono.Stroke.cs
│   │   │   ├── HudChrono.UtteranceCountdown.cs
│   │   │   ├── HudChrono.xaml
│   │   │   ├── HudChrono.xaml.cs
│   │   │   └── UtteranceCountdownState.cs
│   │   ├── Model/
│   │   │   ├── HudPalette.cs
│   │   │   ├── HudState.cs
│   │   │   └── MessageKind.cs
│   │   ├── Settings/
│   │   │   ├── ConfigOverrides.cs
│   │   │   ├── HudSettings.cs
│   │   │   └── HudSettingsService.cs
│   │   ├── Transcript/
│   │   │   ├── TranscriptHistory.cs
│   │   │   ├── TranscriptPanelLayout.cs
│   │   │   ├── TranscriptPanelWindow.Proximity.cs
│   │   │   ├── TranscriptPanelWindow.xaml
│   │   │   └── TranscriptPanelWindow.xaml.cs
│   │   ├── Windows/
│   │   │   ├── HudOverlayManager.cs
│   │   │   ├── HudOverlayWindow.xaml
│   │   │   ├── HudOverlayWindow.xaml.cs
│   │   │   ├── HudWindow.Fade.cs
│   │   │   ├── HudWindow.Proximity.cs
│   │   │   ├── HudWindow.State.cs
│   │   │   ├── HudWindow.Windowing.cs
│   │   │   ├── HudWindow.xaml
│   │   │   ├── HudWindow.xaml.cs
│   │   │   ├── ProximityFade.cs
│   │   │   ├── SystemAnimationPreference.cs
│   │   │   └── WindowSlideAnimator.cs
│   │   ├── AnimationGate.cs
│   │   ├── Deckle.Hud.csproj
│   │   ├── DeckleHudSource.cs
│   │   ├── HudMessage.xaml
│   │   ├── HudMessage.xaml.cs
│   │   └── ProximityRollupAggregator.cs
│   ├── Deckle.Inference.Onnx/
│   │   ├── Deckle.Inference.Onnx.csproj
│   │   └── OnnxModelSession.cs
│   ├── Deckle.Input/
│   │   ├── Injection/
│   │   │   ├── MouseInjector.cs
│   │   │   └── PrecisionTouchpadInjector.cs
│   │   ├── Interop/
│   │   │   ├── HidInterop.cs
│   │   │   ├── LowLevelMouseHookInterop.cs
│   │   │   ├── PrecisionTouchpadSystemParameters.cs
│   │   │   ├── RawInputInterop.cs
│   │   │   ├── SendInputInterop.cs
│   │   │   ├── SyntheticTouchpadInterop.cs
│   │   │   └── WinEventInterop.cs
│   │   ├── Keyboard/
│   │   │   ├── FocusEventCoalescer.cs
│   │   │   ├── IKeyboardInputHost.cs
│   │   │   ├── IWheelInterceptor.cs
│   │   │   ├── KeyboardInputHost.cs
│   │   │   ├── KeyboardInputHost.FocusHooks.cs
│   │   │   ├── KeyboardInputHost.Health.cs
│   │   │   ├── KeyboardInputHost.LifecycleAndWindow.cs
│   │   │   ├── KeyboardInputHost.MouseHook.cs
│   │   │   ├── KeyboardInputHost.RawInput.cs
│   │   │   ├── KeyboardInputHost.Rollup.cs
│   │   │   ├── KeyboardInputHost.WheelObservations.cs
│   │   │   ├── KeyboardKeyEvent.cs
│   │   │   ├── MouseInteractionRouter.cs
│   │   │   ├── MouseWheelEvent.cs
│   │   │   ├── RepeatingIncident.cs
│   │   │   ├── WheelEventQueue.cs
│   │   │   └── WheelObservationBuffer.cs
│   │   ├── Telemetry/
│   │   │   ├── ContactDeviceRegistry.cs
│   │   │   ├── ContactFrameRecorder.cs
│   │   │   └── WheelEventRecorder.cs
│   │   ├── Touchpad/
│   │   │   ├── ContactFrame.cs
│   │   │   ├── ContactFrameAssembler.cs
│   │   │   ├── TouchpadCapabilities.cs
│   │   │   ├── TouchpadContact.cs
│   │   │   ├── TouchpadDevice.cs
│   │   │   ├── TouchpadParser.cs
│   │   │   └── TouchpadReport.cs
│   │   ├── Deckle.Input.csproj
│   │   ├── DeckleInputSource.cs
│   │   ├── GLOSSARY.md  — glossary-deckle-input Input-layer vocabulary. It defines the contact frame, the unit assembled from o…
│   │   ├── MouseWheelSettings.cs
│   │   ├── MouseWheelSettingsService.cs
│   │   └── RawInputHost.cs
│   ├── Deckle.Input.PrecisionScroll/
│   │   ├── Engine/
│   │   │   ├── PrecisionScrollEngine.cs
│   │   │   ├── PrecisionScrollGesture.cs
│   │   │   └── WheelTickQueue.cs
│   │   ├── Strings/
│   │   │   └── en-US/
│   │   │       └── Resources.resw
│   │   ├── Ui/
│   │   │   ├── PrecisionScrollPage.xaml
│   │   │   └── PrecisionScrollPage.xaml.cs
│   │   ├── ViewModels/
│   │   │   ├── PrecisionScrollViewModel.cs
│   │   │   ├── PrecisionScrollViewModel.Settings.cs
│   │   │   └── PrecisionScrollViewModel.Tuning.cs
│   │   ├── Deckle.Input.PrecisionScroll.csproj
│   │   ├── DecklePrecisionScrollSource.cs
│   │   ├── PrecisionScrollSettings.cs
│   │   ├── PrecisionScrollSettingsModule.cs
│   │   ├── PrecisionScrollSettingsService.cs
│   │   ├── PrecisionScrollTuning.cs
│   │   └── SettingsSearch.cs
│   ├── Deckle.Input.Trackpad/
│   │   ├── Acts/
│   │   │   ├── ConnectionRepair.cs
│   │   │   ├── repair-trackpad-connection.ps1  — Repairs the Apple Magic Trackpad 2 Bluetooth pairing on Windows. The Apple Precision Touchpad driver (appleprecisiontrackpadbluetooth.inf, signe…
│   │   │   └── WindowsGestureNeutralizer.cs
│   │   ├── Engine/
│   │   │   ├── ThreeFingerDragRecognizer.cs
│   │   │   └── TrackpadEngine.cs
│   │   ├── Strings/
│   │   │   └── en-US/
│   │   │       └── Resources.resw
│   │   ├── Ui/
│   │   │   ├── TrackpadPage.xaml
│   │   │   └── TrackpadPage.xaml.cs
│   │   ├── ViewModels/
│   │   │   ├── TrackpadTelemetryViewModel.cs
│   │   │   ├── TrackpadViewModel.cs
│   │   │   └── TrackpadViewModel.Settings.cs
│   │   ├── Deckle.Input.Trackpad.csproj
│   │   ├── DeckleTrackpadSource.cs
│   │   ├── GLOSSARY.md  — glossary-deckle-input-trackpad Trackpad gesture vocabulary. It defines the recognizer, the state machine that…
│   │   ├── SettingsSearch.cs
│   │   ├── TrackpadSettings.cs
│   │   ├── TrackpadSettingsModule.cs
│   │   └── TrackpadSettingsService.cs
│   ├── Deckle.Install/
│   │   ├── Deckle.Install.csproj
│   │   ├── InstallPaths.cs
│   │   ├── ReleaseResolver.cs
│   │   ├── RunningProcesses.cs
│   │   ├── Shortcut.cs
│   │   ├── UninstallEntry.cs
│   │   └── UserEnvironment.cs
│   ├── Deckle.Installer/
│   │   ├── Install/
│   │   │   ├── CliArgs.cs
│   │   │   ├── InstallFlow.cs
│   │   │   └── Uninstaller.cs
│   │   ├── Io/
│   │   │   └── Downloader.cs
│   │   ├── Ui/
│   │   │   ├── MessageDialog.cs
│   │   │   └── ProgressWindow.cs
│   │   ├── app.manifest
│   │   ├── Deckle.Installer.csproj
│   │   ├── InstallLog.cs
│   │   └── Program.cs
│   ├── Deckle.Lighting/
│   │   ├── Hue/
│   │   │   ├── HueBridge.cs
│   │   │   ├── HueBridgeClient.cs
│   │   │   ├── HueBridgeClient.Dtos.cs
│   │   │   ├── HueBridgeClient.EventStream.cs
│   │   │   ├── HueBridgeClient.Identity.cs
│   │   │   ├── HueBridgeClient.Pairing.cs
│   │   │   ├── HueBridgeClient.V2.cs
│   │   │   ├── HueClientKeyStore.cs
│   │   │   ├── HueColorMath.cs
│   │   │   ├── HueCredentialVault.cs
│   │   │   ├── HueDiscovery.cs
│   │   │   ├── HueEntertainmentArea.cs
│   │   │   ├── HueEntertainmentFrameBuilder.cs
│   │   │   ├── HueEntertainmentLightOutput.cs
│   │   │   ├── HueEntertainmentTransport.cs
│   │   │   ├── HueEventStreamEpisode.cs
│   │   │   ├── HueEventStreamModels.cs
│   │   │   ├── HueGroup.cs
│   │   │   ├── HueLight.cs
│   │   │   ├── HueLightOutputFactory.cs
│   │   │   ├── HueLocalDiscovery.cs
│   │   │   ├── HueLocalDiscovery.Interop.cs
│   │   │   ├── HueProjectedState.cs
│   │   │   └── HueRestLightOutput.cs
│   │   ├── Deckle.Lighting.csproj
│   │   ├── DeckleLightingSource.cs
│   │   ├── DeckleLightingSource.Discovery.cs
│   │   ├── DeckleLightingSource.Entertainment.cs
│   │   ├── DeckleLightingSource.GroupsAndColor.cs
│   │   ├── DeckleLightingSource.InventoryAndEventStream.cs
│   │   ├── DeckleLightingSource.Pairing.cs
│   │   ├── ILightOutput.cs
│   │   ├── LightColor.cs
│   │   └── LightDescriptor.cs
│   ├── Deckle.Lighting.Ambient/
│   │   ├── Engine/
│   │   │   ├── AmbientBlackLatch.cs
│   │   │   ├── AmbientBrightnessCurve.cs
│   │   │   ├── AmbientColorPipeline.cs
│   │   │   ├── AmbientColorSmoother.cs
│   │   │   ├── AmbientEngine.CaptureEvents.cs
│   │   │   ├── AmbientEngine.cs
│   │   │   ├── AmbientEngine.GroupPush.cs
│   │   │   ├── AmbientEngine.Heartbeat.cs
│   │   │   ├── AmbientEngine.HueEvents.cs
│   │   │   ├── AmbientEngine.Lifecycle.cs
│   │   │   ├── AmbientEngine.MultiLightPush.cs
│   │   │   ├── AmbientEngine.PushHealth.cs
│   │   │   ├── AmbientEngine.PushLoop.cs
│   │   │   ├── AmbientEngineState.cs
│   │   │   ├── AmbientHeartbeatWindow.cs
│   │   │   ├── AmbientHueChangeAttributor.cs
│   │   │   ├── AmbientPushCadence.cs
│   │   │   ├── AmbientPushGate.cs
│   │   │   ├── AmbientZoneSampler.cs
│   │   │   ├── HueEndpointResolver.cs
│   │   │   ├── HuePairingService.cs
│   │   │   ├── LightPlacementResolver.cs
│   │   │   ├── LightZone.cs
│   │   │   ├── LightZoneSuggester.cs
│   │   │   └── SrgbColor.cs
│   │   ├── Strings/
│   │   │   └── en-US/
│   │   │       └── Resources.resw
│   │   ├── Ui/
│   │   │   ├── Controls/
│   │   │   │   ├── AmbientPreviewCard.xaml
│   │   │   │   ├── AmbientPreviewCard.xaml.cs
│   │   │   │   ├── LightZonesCard.xaml
│   │   │   │   └── LightZonesCard.xaml.cs
│   │   │   ├── AmbientPage.Hue.cs
│   │   │   ├── AmbientPage.xaml
│   │   │   └── AmbientPage.xaml.cs
│   │   ├── ViewModels/
│   │   │   ├── AmbientViewModel.cs
│   │   │   └── AmbientViewModel.Settings.cs
│   │   ├── AmbientCaptureRate.cs
│   │   ├── AmbientPresets.cs
│   │   ├── AmbientPresetStore.cs
│   │   ├── AmbientSettings.cs
│   │   ├── AmbientSettingsModule.cs
│   │   ├── AmbientSettingsService.cs
│   │   ├── AmbientStartSettings.cs
│   │   ├── AmbientTuning.cs
│   │   ├── Deckle.Lighting.Ambient.csproj
│   │   ├── DeckleAmbientSource.CaptureAndSampling.cs
│   │   ├── DeckleAmbientSource.cs
│   │   ├── DeckleAmbientSource.Lifecycle.cs
│   │   ├── DeckleAmbientSource.LightPlacement.cs
│   │   ├── DeckleAmbientSource.Pairing.cs
│   │   ├── DeckleAmbientSource.Push.cs
│   │   ├── DeckleAmbientSource.UiAndSettings.cs
│   │   ├── IAmbientEngineHost.cs
│   │   └── SettingsSearch.cs
│   ├── Deckle.Llm/
│   │   ├── Engines/
│   │   │   ├── ClaudeEngine.cs
│   │   │   ├── OllamaEngine.cs
│   │   │   └── OpenAiCompatibleEngine.cs
│   │   ├── Generation/
│   │   │   ├── GenerationDeadline.cs
│   │   │   ├── GenerationRequest.cs
│   │   │   ├── GenerationResult.cs
│   │   │   ├── IGenerationEngine.cs
│   │   │   └── PromptTemplates.cs
│   │   ├── Providers/
│   │   │   ├── IGenerationEngineResolver.cs
│   │   │   ├── LlmProvider.cs
│   │   │   ├── LlmProviderEngines.cs
│   │   │   ├── LlmProviderKind.cs
│   │   │   ├── LlmProviderSecrets.cs
│   │   │   └── ProviderModelCatalog.cs
│   │   ├── Deckle.Llm.csproj
│   │   ├── DeckleLlmSource.Batches.cs
│   │   ├── DeckleLlmSource.cs
│   │   ├── DeckleLlmSource.FeedbackAndUi.cs
│   │   ├── DeckleLlmSource.OllamaService.cs
│   │   ├── DeckleLlmSource.Persistence.cs
│   │   ├── DeckleLlmSource.Providers.cs
│   │   ├── DeckleLlmSource.Rewrite.cs
│   │   ├── DeckleLlmSource.RuntimeStatus.cs
│   │   └── OllamaService.cs
│   ├── Deckle.Llm.Rewrite/
│   │   ├── Batching/
│   │   │   ├── DictationBatch.cs
│   │   │   ├── DictationBatcher.cs
│   │   │   ├── DictationBatchResult.cs
│   │   │   ├── DictationBatchRewriter.cs
│   │   │   └── SentenceBoundary.cs
│   │   ├── Engine/
│   │   │   ├── ParagraphRewrite.cs
│   │   │   ├── RewriteFailureMessage.cs
│   │   │   ├── RewriteService.cs
│   │   │   └── SentenceRewrite.cs
│   │   ├── Gate/
│   │   │   ├── DiffAlignment.cs
│   │   │   ├── DiffGateVerdict.cs
│   │   │   ├── GateLexicon.cs
│   │   │   ├── GateToken.cs
│   │   │   └── RewriteDiffGate.cs
│   │   ├── Interaction/
│   │   │   ├── ParagraphDraft.cs
│   │   │   ├── ParagraphRewriteCoordinator.cs
│   │   │   └── ParagraphRewriteOffer.cs
│   │   ├── Settings/
│   │   │   └── RewriteAvailability.cs
│   │   ├── Strings/
│   │   │   └── en-US/
│   │   │       └── Resources.resw
│   │   ├── Ui/
│   │   │   ├── BoolToVisibilityConverter.cs
│   │   │   ├── LlmActiveProfileSection.xaml
│   │   │   ├── LlmActiveProfileSection.xaml.cs
│   │   │   ├── LlmDictationSection.xaml
│   │   │   ├── LlmDictationSection.xaml.cs
│   │   │   ├── LlmGeneralSection.xaml
│   │   │   ├── LlmGeneralSection.xaml.cs
│   │   │   ├── LlmModelsSection.xaml
│   │   │   ├── LlmModelsSection.xaml.cs
│   │   │   ├── LlmOllamaContext.cs
│   │   │   ├── LlmPage.xaml
│   │   │   ├── LlmPage.xaml.cs
│   │   │   ├── LlmProfilesSection.xaml
│   │   │   ├── LlmProfilesSection.xaml.cs
│   │   │   ├── LlmProvidersSection.xaml
│   │   │   ├── LlmProvidersSection.xaml.cs
│   │   │   ├── ProfileViewModel.cs
│   │   │   ├── ProviderViewModel.cs
│   │   │   ├── RewriteChangeView.cs
│   │   │   ├── RewriteOfferWindow.xaml
│   │   │   └── RewriteOfferWindow.xaml.cs
│   │   ├── ViewModels/
│   │   │   ├── LlmDictationViewModel.cs
│   │   │   ├── LlmDictationViewModel.Settings.cs
│   │   │   ├── LlmGeneralViewModel.cs
│   │   │   └── LlmGeneralViewModel.Settings.cs
│   │   ├── Deckle.Llm.Rewrite.csproj
│   │   ├── GLOSSARY.md  — glossary-deckle-llm-rewrite Rewrite vocabulary. It draws the Correction / Rewrite boundary, which decides w…
│   │   ├── LlmSettings.cs
│   │   ├── LlmSettingsMigrations.cs
│   │   ├── LlmSettingsMigrations.Providers.cs
│   │   ├── LlmSettingsModule.cs
│   │   ├── LlmSettingsService.cs
│   │   └── SettingsSearch.cs
│   ├── Deckle.Modules/
│   │   ├── Deckle.Modules.csproj
│   │   ├── DeckleModulesSource.cs
│   │   ├── ModuleDescriptor.cs
│   │   ├── ModuleGraph.cs
│   │   ├── ModuleIds.cs
│   │   ├── ModulePresence.cs
│   │   ├── ModuleRegistry.cs
│   │   ├── PresenceChoice.cs
│   │   └── PresenceFile.cs
│   ├── Deckle.Notifications/
│   │   ├── Catalog/
│   │   │   ├── NotificationAction.cs
│   │   │   ├── NotificationCatalog.cs
│   │   │   └── NotificationDescriptor.cs
│   │   ├── Channels/
│   │   │   └── Toast/
│   │   │       ├── ToastActivation.cs
│   │   │       └── ToastChannel.cs
│   │   ├── Dispatch/
│   │   │   ├── INotificationChannel.cs
│   │   │   ├── NotificationDispatcher.cs
│   │   │   └── NotificationResponse.cs
│   │   ├── Deckle.Notifications.csproj
│   │   └── DeckleNotificationsSource.cs
│   ├── Deckle.Orchestration/
│   │   ├── Detection/
│   │   │   ├── AppSessionTracker.cs
│   │   │   ├── DetectionStatus.cs
│   │   │   ├── ForegroundChange.cs
│   │   │   ├── ForegroundChangeMapping.cs
│   │   │   ├── IForegroundMonitor.cs
│   │   │   ├── IProcessStartSource.cs
│   │   │   ├── NullForegroundMonitor.cs
│   │   │   ├── OrchestrationRuntime.cs
│   │   │   ├── ProcessExitWatch.cs
│   │   │   ├── ProcessStarted.cs
│   │   │   ├── WatcherForegroundMonitor.cs
│   │   │   └── WmiProcessStartSource.cs
│   │   ├── Display/
│   │   │   ├── DisplayTarget.cs
│   │   │   ├── DisplayTargets.cs
│   │   │   └── HdrStateSwitch.cs
│   │   ├── Interop/
│   │   │   ├── DisplayConfigNativeMethods.cs
│   │   │   └── ProcessNativeMethods.cs
│   │   ├── Processes/
│   │   │   └── RunningApps.cs
│   │   ├── Registry/
│   │   │   ├── AppDisplayName.cs
│   │   │   ├── AppEntry.cs
│   │   │   ├── AppKind.cs
│   │   │   ├── AppMatcher.cs
│   │   │   ├── AppRules.cs
│   │   │   ├── AppSource.cs
│   │   │   └── HdrRule.cs
│   │   ├── Rules/
│   │   │   ├── AppliedProfile.cs
│   │   │   ├── HdrRuleEffect.cs
│   │   │   ├── IRuleEffect.cs
│   │   │   └── ProfileTransaction.cs
│   │   ├── Scan/
│   │   │   ├── ExecutableLocator.cs
│   │   │   ├── FolderScanner.cs
│   │   │   └── RegistryScan.cs
│   │   ├── Steam/
│   │   │   ├── KeyValuesNode.cs
│   │   │   ├── KeyValuesReader.cs
│   │   │   ├── SteamLibrary.cs
│   │   │   └── SteamManifest.cs
│   │   ├── Strings/
│   │   │   └── en-US/
│   │   │       └── Resources.resw
│   │   ├── Ui/
│   │   │   ├── EntryItemTemplateSelector.cs
│   │   │   ├── OrchestrationPage.xaml
│   │   │   ├── OrchestrationPage.xaml.cs
│   │   │   ├── RunningProcessDialog.xaml
│   │   │   └── RunningProcessDialog.xaml.cs
│   │   ├── ViewModels/
│   │   │   ├── AppEntryRow.cs
│   │   │   ├── EntryItems.cs
│   │   │   ├── MonitorHdrRow.cs
│   │   │   ├── OrchestrationViewModel.cs
│   │   │   └── ScanFolderRow.cs
│   │   ├── Deckle.Orchestration.csproj
│   │   ├── DeckleOrchestrationSource.cs
│   │   ├── OrchestrationSettings.cs
│   │   ├── OrchestrationSettingsModule.cs
│   │   ├── OrchestrationSettingsService.cs
│   │   └── SettingsSearch.cs
│   ├── Deckle.Playground/
│   │   ├── Models/
│   │   │   ├── CorrectionApplicationEvidence.cs
│   │   │   ├── CorrectionApplicationFixture.cs
│   │   │   ├── CorrectionApplicationTrial.cs
│   │   │   ├── MicrophonePreviewFailure.cs
│   │   │   ├── MicrophonePreviewFeed.cs
│   │   │   ├── TuningModel.cs
│   │   │   └── UtteranceCycle.cs
│   │   ├── Strings/
│   │   │   └── en-US/
│   │   │       └── Resources.resw
│   │   ├── ViewModels/
│   │   │   └── HudViewModel.cs
│   │   ├── Views/
│   │   │   ├── Ambient/
│   │   │   │   ├── AmbientPage.HdrTuning.cs
│   │   │   │   ├── AmbientPage.Hue.cs
│   │   │   │   ├── AmbientPage.LightZones.cs
│   │   │   │   ├── AmbientPage.Preview.cs
│   │   │   │   ├── AmbientPage.ScreenCapture.cs
│   │   │   │   ├── AmbientPage.xaml
│   │   │   │   └── AmbientPage.xaml.cs
│   │   │   ├── Autocorrect/
│   │   │   │   ├── CorrectionApplicationPage.xaml
│   │   │   │   ├── CorrectionApplicationPage.xaml.cs
│   │   │   │   └── RichEditCorrectionSurface.cs
│   │   │   ├── Hud/
│   │   │   │   ├── HudPage.Expanders.cs
│   │   │   │   ├── HudPage.Gauge.cs
│   │   │   │   ├── HudPage.Microphone.cs
│   │   │   │   ├── HudPage.RowFactories.cs
│   │   │   │   ├── HudPage.SectionState.cs
│   │   │   │   ├── HudPage.Simulation.cs
│   │   │   │   ├── HudPage.xaml
│   │   │   │   └── HudPage.xaml.cs
│   │   │   ├── HomePage.xaml
│   │   │   ├── HomePage.xaml.cs
│   │   │   ├── PlaygroundWindow.xaml
│   │   │   └── PlaygroundWindow.xaml.cs
│   │   ├── Deckle.Playground.csproj
│   │   ├── DecklePlaygroundSource.cs
│   │   ├── PlaygroundNotifications.cs
│   │   └── PlaygroundShell.cs
│   ├── Deckle.Presets/
│   │   ├── Deckle.Presets.csproj
│   │   ├── IPresetStore.cs
│   │   ├── Preset.cs
│   │   └── PresetFamily.cs
│   ├── Deckle.Security/
│   │   ├── Deckle.Security.csproj
│   │   ├── GLOSSARY.md  — glossary-deckle-security Security vocabulary. It defines the trusted-session boundary, captured content,…
│   │   ├── ISecretVault.cs
│   │   ├── SecretVault.cs
│   │   └── SecretVaultException.cs
│   ├── Deckle.Settings/
│   │   ├── Controls/
│   │   │   ├── FolderLocationLine.cs
│   │   │   ├── TunableRow.xaml
│   │   │   ├── TunableRow.xaml.cs
│   │   │   ├── TuningCard.xaml
│   │   │   └── TuningCard.xaml.cs
│   │   ├── Dialogs/
│   │   │   ├── ApplicationLogConsentDialog.cs
│   │   │   ├── AudioCorpusConsentDialog.cs
│   │   │   ├── AutocorrectDecisionsConsentDialog.cs
│   │   │   ├── AutocorrectTextConsentDialog.cs
│   │   │   ├── CorpusConsentDialog.cs
│   │   │   └── MicrophoneTelemetryConsentDialog.cs
│   │   ├── Modules/
│   │   │   └── SettingsModuleRegistry.cs
│   │   ├── Pages/
│   │   │   ├── GeneralPage.xaml
│   │   │   ├── GeneralPage.xaml.cs
│   │   │   ├── HomePage.xaml
│   │   │   ├── HomePage.xaml.cs
│   │   │   ├── HotkeysPage.xaml
│   │   │   └── HotkeysPage.xaml.cs
│   │   ├── Persistence/
│   │   │   ├── AppSettings.cs
│   │   │   ├── SettingsBackupService.cs
│   │   │   ├── SettingsBootstrap.cs
│   │   │   └── SettingsService.cs
│   │   ├── Strings/
│   │   │   └── en-US/
│   │   │       └── Resources.resw
│   │   ├── Themes/
│   │   │   └── RailBrushes.xaml
│   │   ├── ViewModels/
│   │   │   ├── GeneralViewModel.cs
│   │   │   └── GeneralViewModel.Settings.cs
│   │   ├── Deckle.Settings.csproj
│   │   ├── DeckleSettingsSource.Backup.cs
│   │   ├── DeckleSettingsSource.cs
│   │   ├── DeckleSettingsSource.Migration.cs
│   │   ├── DeckleSettingsSource.Navigation.cs
│   │   ├── DeckleSettingsSource.Persistence.cs
│   │   ├── DeckleSettingsSource.Search.cs
│   │   ├── GLOSSARY.md  — glossary-deckle-settings Settings vocabulary. It defines the exposable, the family of adjustable values…
│   │   ├── SettingSearchHit.cs
│   │   ├── SettingsHost.cs
│   │   ├── SettingsSearch.cs
│   │   ├── SettingsSearchIndex.cs
│   │   ├── SettingSuggestion.cs
│   │   ├── SettingsWindow.Search.cs
│   │   ├── SettingsWindow.TitleBarProbe.cs
│   │   ├── SettingsWindow.xaml
│   │   ├── SettingsWindow.xaml.cs
│   │   └── TelemetryConsentWiring.cs
│   ├── Deckle.Setup/
│   │   ├── Relocation/
│   │   │   ├── DataRootRelocator.cs
│   │   │   ├── DataRootTree.cs
│   │   │   └── WindowsDataRootRelocation.cs
│   │   ├── Strings/
│   │   │   └── en-US/
│   │   │       └── Resources.resw
│   │   ├── Update/
│   │   │   └── UpdateService.cs
│   │   ├── ByteSizeFormatter.cs
│   │   ├── ChoicesPage.xaml
│   │   ├── ChoicesPage.xaml.cs
│   │   ├── Deckle.Setup.csproj
│   │   ├── DeckleSetupSource.Choices.cs
│   │   ├── DeckleSetupSource.cs
│   │   ├── DeckleSetupSource.Deploy.cs
│   │   ├── DeckleSetupSource.Provisioning.cs
│   │   ├── DeckleSetupSource.Relocation.cs
│   │   ├── DeckleSetupSource.Summary.cs
│   │   ├── DeckleSetupSource.Update.cs
│   │   ├── DeckleSetupSource.Wizard.cs
│   │   ├── DeployPage.xaml
│   │   ├── DeployPage.xaml.cs
│   │   ├── FoldersPage.xaml
│   │   ├── FoldersPage.xaml.cs
│   │   ├── InstallingPage.xaml
│   │   ├── InstallingPage.xaml.cs
│   │   ├── InstallItem.cs
│   │   ├── InstallPlan.cs
│   │   ├── ModulesPage.xaml
│   │   ├── ModulesPage.xaml.cs
│   │   ├── RelocatePage.xaml
│   │   ├── RelocatePage.xaml.cs
│   │   ├── SetupContext.cs
│   │   ├── SetupNotifications.cs
│   │   ├── SetupStep.cs
│   │   ├── SetupWindow.xaml
│   │   ├── SetupWindow.xaml.cs
│   │   ├── SummaryPage.xaml
│   │   ├── SummaryPage.xaml.cs
│   │   ├── UpdateDownloadPage.xaml
│   │   ├── UpdateDownloadPage.xaml.cs
│   │   └── UpdatePredecessor.cs
│   ├── Deckle.Shell/
│   │   ├── AutostartService.cs
│   │   ├── CursorMovementSignal.cs
│   │   ├── Deckle.Shell.csproj
│   │   ├── DeckleShellSource.cs
│   │   ├── DispatcherQueueExtensions.cs
│   │   ├── ElevatedStartupService.cs
│   │   ├── GlobalHotkeyBindings.cs
│   │   ├── HotkeyManager.cs
│   │   ├── IconAssets.cs
│   │   ├── MessageOnlyHost.cs
│   │   ├── ResizeCoalescer.cs
│   │   ├── ResizeGesture.cs
│   │   ├── StartupService.cs
│   │   ├── TrayIconManager.cs
│   │   └── TrayIconRegistration.cs
│   ├── Deckle.Shell.Foreground/
│   │   ├── Interop/
│   │   │   └── ForegroundNativeMethods.cs
│   │   ├── Deckle.Shell.Foreground.csproj
│   │   ├── DeckleShellForegroundSource.cs
│   │   ├── ForegroundState.cs
│   │   ├── ForegroundWatcher.cs
│   │   ├── ForegroundWatcher.Probe.cs
│   │   └── FullscreenGeometry.cs
│   ├── Deckle.Shell.TaskbarCover/
│   │   ├── Interop/
│   │   │   └── TaskbarCoverNativeMethods.cs
│   │   ├── Strings/
│   │   │   └── en-US/
│   │   │       └── Resources.resw
│   │   ├── Ui/
│   │   │   ├── TaskbarCoverPage.xaml
│   │   │   └── TaskbarCoverPage.xaml.cs
│   │   ├── ViewModels/
│   │   │   ├── TaskbarCoverViewModel.cs
│   │   │   └── TaskbarCoverViewModel.Settings.cs
│   │   ├── ApproachDistance.cs
│   │   ├── CoverBand.cs
│   │   ├── CoverGeometry.cs
│   │   ├── CoverStacking.cs
│   │   ├── CoverSuppression.cs
│   │   ├── Deckle.Shell.TaskbarCover.csproj
│   │   ├── DeckleShellTaskbarCoverSource.cs
│   │   ├── SettingsSearch.cs
│   │   ├── TaskbarCoverHost.Approach.cs
│   │   ├── TaskbarCoverHost.CallbacksAndMessagePump.cs
│   │   ├── TaskbarCoverHost.cs
│   │   ├── TaskbarCoverHost.GeometryAndFullscreen.cs
│   │   ├── TaskbarCoverHost.LifecycleAndWindow.cs
│   │   ├── TaskbarCoverHost.Visibility.cs
│   │   ├── TaskbarCoverSettings.cs
│   │   ├── TaskbarCoverSettingsModule.cs
│   │   ├── TaskbarCoverSettingsService.cs
│   │   ├── TaskbarLocator.cs
│   │   └── TaskbarPlacement.cs
│   ├── Deckle.Shell.TrayMenu/
│   │   ├── Interop/
│   │   │   └── TrayMenuNativeMethods.cs
│   │   ├── Themes/
│   │   │   └── Generic.xaml
│   │   ├── Deckle.Shell.TrayMenu.csproj
│   │   ├── DeckleShellTrayMenuSource.cs
│   │   ├── TrayCarrierWindow.cs
│   │   ├── TrayMenuEntry.cs
│   │   ├── TrayMenuHost.cs
│   │   ├── TrayMenuHost.Items.cs
│   │   ├── TrayMenuHost.Show.cs
│   │   ├── TrayMenuPlacement.cs
│   │   ├── TraySwitchMenuItem.cs
│   │   ├── TrayToggle.cs
│   │   └── TrayWindowOwnership.cs
│   ├── Deckle.Shell.WindowChrome/
│   │   ├── CaptionInsetCorrection.cs
│   │   └── Deckle.Shell.WindowChrome.csproj
│   ├── Deckle.Speech/
│   │   ├── ChatterboxSpeechBackend.cs
│   │   ├── Deckle.Speech.csproj
│   │   ├── DeckleSpeechSource.cs
│   │   ├── ISpeechBackend.cs
│   │   ├── SpeechEngine.cs
│   │   ├── SpeechSettings.cs
│   │   └── SpeechSettingsService.cs
│   ├── Deckle.Transcription/
│   │   ├── Corpus/
│   │   │   ├── CorpusTier.cs
│   │   │   ├── PromptTemplateHash.cs
│   │   │   └── WavCorpusWriter.cs
│   │   ├── Curves/
│   │   │   └── UnitBezier.cs
│   │   ├── Engine/
│   │   │   ├── FileTranscriptionQueue.cs
│   │   │   ├── IAsrBackend.cs
│   │   │   ├── PhantomUtteranceFilter.cs
│   │   │   ├── PipelineProduction.cs
│   │   │   ├── PreparedFileProducer.cs
│   │   │   ├── RewriteProfileSelection.cs
│   │   │   ├── SampleLevels.cs
│   │   │   ├── TextMetrics.cs
│   │   │   ├── TrailingNoisePad.cs
│   │   │   ├── TranscriptFileWriter.cs
│   │   │   ├── TranscriptionDelivery.cs
│   │   │   ├── TranscriptionEngine.BatchRewrite.cs
│   │   │   ├── TranscriptionEngine.cs
│   │   │   ├── TranscriptionEngine.FilePipeline.cs
│   │   │   ├── TranscriptionEngine.Finalize.cs
│   │   │   ├── TranscriptionEngine.Finalize.Observability.cs
│   │   │   ├── TranscriptionEngine.Finalize.Rewrite.cs
│   │   │   ├── TranscriptionEngine.Lifecycle.cs
│   │   │   ├── TranscriptionEngine.MonolithicPipeline.cs
│   │   │   ├── TranscriptionEngine.Pipeline.cs
│   │   │   ├── TranscriptionEngine.Recovery.cs
│   │   │   ├── TranscriptionEngine.StateMachine.cs
│   │   │   ├── TranscriptionEngine.StreamingPipeline.cs
│   │   │   ├── TranscriptionEngine.Telemetry.cs
│   │   │   └── TranscriptionEngine.Utterances.cs
│   │   ├── Recovery/
│   │   │   └── DictationRecoveryStore.cs
│   │   ├── Setup/
│   │   │   └── ModelEntry.cs
│   │   ├── Streaming/
│   │   │   ├── CutCurveValues.cs
│   │   │   ├── CutPresets.cs
│   │   │   ├── EnergySegmenter.cs
│   │   │   ├── EnergySegmenterSettings.cs
│   │   │   ├── SegmentedTranscriptionSession.cs
│   │   │   ├── TranscribedUtterance.cs
│   │   │   ├── Utterance.cs
│   │   │   ├── UtteranceProgress.cs
│   │   │   └── UtteranceProgressMeter.cs
│   │   ├── Strings/
│   │   │   └── en-US/
│   │   │       └── Resources.resw
│   │   ├── ViewModels/
│   │   │   ├── CutPresetStore.cs
│   │   │   ├── WhisperTelemetryViewModel.cs
│   │   │   ├── WhisperViewModel.cs
│   │   │   ├── WhisperViewModel.DictationExperience.cs
│   │   │   ├── WhisperViewModel.Engine.cs
│   │   │   ├── WhisperViewModel.Settings.cs
│   │   │   └── WhisperViewModel.Streaming.cs
│   │   ├── Deckle.Transcription.csproj
│   │   ├── DeckleWhispSource.cs
│   │   ├── DeckleWhispSource.Decode.cs
│   │   ├── DeckleWhispSource.Delivery.cs
│   │   ├── DeckleWhispSource.FileTranscription.cs
│   │   ├── DeckleWhispSource.PipelineCompletion.cs
│   │   ├── DeckleWhispSource.Preprocessing.cs
│   │   ├── DeckleWhispSource.Recovery.cs
│   │   ├── DeckleWhispSource.Segments.cs
│   │   ├── DeckleWhispSource.Streaming.cs
│   │   ├── DeckleWhispSource.Telemetry.cs
│   │   ├── DeckleWhispSource.Transcribe.cs
│   │   ├── DeckleWhispSource.Ui.cs
│   │   ├── DeckleWhispSource.WarmupModel.cs
│   │   ├── DecodingPage.xaml
│   │   ├── DecodingPage.xaml.cs
│   │   ├── GLOSSARY.md  — glossary-deckle-transcription Transcription vocabulary. It names the two entry points (dictation, file transc…
│   │   ├── ITranscriptionEngineHost.cs
│   │   ├── SettingsSearch.cs
│   │   ├── StreamingPage.xaml
│   │   ├── StreamingPage.xaml.cs
│   │   ├── TranscriptionSettings.cs
│   │   ├── TranscriptionSettingsService.cs
│   │   ├── WhisperPage.xaml
│   │   ├── WhisperPage.xaml.cs
│   │   └── WhisperSettingsModule.cs
│   ├── Deckle.Transcription.Whisper/
│   │   ├── Engine/
│   │   │   ├── WhisperContextParamsMapper.cs
│   │   │   └── WhisperParamsMapper.cs
│   │   ├── Pinvoke/
│   │   │   ├── WhisperPInvoke.cs
│   │   │   └── WhisperStructs.cs
│   │   ├── Setup/
│   │   │   ├── NativeRuntime.cs
│   │   │   ├── SpeechModelResolver.cs
│   │   │   └── SpeechModels.cs
│   │   ├── Deckle.Transcription.Whisper.csproj
│   │   ├── DeckleWhisperSource.cs
│   │   ├── KnownHallucinations.cs
│   │   ├── RepetitionDetector.cs
│   │   ├── WhisperBackend.cs
│   │   ├── WhisperBackend.Model.cs
│   │   ├── WhisperBackend.NativeLogging.cs
│   │   ├── WhisperBackend.Transcribe.cs
│   │   ├── WhisperComputeDeviceObserver.cs
│   │   └── WhisperNativeLogCompactor.cs
│   ├── Deckle.Vad/
│   │   ├── Deckle.Vad.csproj
│   │   ├── DeckleVadSource.cs
│   │   ├── SileroSpeechTimestamps.cs
│   │   ├── SileroVad.cs
│   │   ├── SileroVadModel.cs
│   │   ├── SileroVadOptions.cs
│   │   ├── SpeechSegment.cs
│   │   ├── SpeechTrimResult.cs
│   │   └── VadService.cs
│   └── Deckle.Vision/
│       ├── CapturedFrame.cs
│       ├── CaptureMonitor.cs
│       ├── CapturePacer.cs
│       ├── CaptureStopReasons.cs
│       ├── Deckle.Vision.csproj
│       ├── DeckleVisionSource.cs
│       ├── FrameAnalysisHint.cs
│       ├── FrameFailure.cs
│       ├── FrameSampler.cs
│       ├── FrameSampler.Process.cs
│       ├── FrameSampler.Readback.cs
│       ├── FrameSampler.Resources.cs
│       ├── IFrameAnalyzer.cs
│       ├── SampledFrame.cs
│       ├── ScreenCaptureInterop.cs
│       ├── ScreenCaptureInterop.D3D11.cs
│       ├── ScreenCaptureInterop.Direct3D.cs
│       ├── ScreenCaptureInterop.Duplication.cs
│       ├── ScreenCaptureInterop.Hdr.cs
│       ├── ScreenCaptureService.cs
│       ├── ScreenCaptureService.Dispose.cs
│       ├── ScreenCaptureService.Lifecycle.cs
│       ├── ScreenCaptureService.Loop.cs
│       └── ScreenCaptureService.Recovery.cs
├── tests/
│   ├── Deckle.Anytype.Mcp.Tests/
│   │   ├── AnytypeUtilityToolCatalogTests.cs
│   │   ├── Deckle.Anytype.Mcp.Tests.csproj
│   │   ├── DialogueToolCatalogTests.cs
│   │   ├── FakeSecretVault.cs
│   │   ├── ManagementToolCatalogTests.cs
│   │   ├── McpClientTokensTests.cs
│   │   ├── McpHttpHostTests.cs
│   │   ├── McpToolsetTests.cs
│   │   ├── SchemaAdminToolCatalogTests.cs
│   │   └── ToolCatalogTests.cs
│   ├── Deckle.Anytype.Tests/
│   │   ├── Fixtures/
│   │   │   └── Experiences/
│   │   │       ├── deckle.en-US.experience.fixture.json
│   │   │       └── deckle.en-US.experience.zip
│   │   ├── AnytypeApiClientReplayTests.cs
│   │   ├── AnytypeCredentialsTests.cs
│   │   ├── AnytypeRequestGateTests.cs
│   │   ├── AnytypeSpaceAliasesTests.cs
│   │   ├── AnytypeUtilityGesturesTests.cs
│   │   ├── AnytypeV2ClientTests.cs
│   │   ├── AnytypeV2ExceptionTests.cs
│   │   ├── AnytypeV2OpsTests.cs
│   │   ├── AnytypeV2PropertyMapTests.cs
│   │   ├── AnytypeV2ReadWriteTests.cs
│   │   ├── AnytypeV2SearcherTests.cs
│   │   ├── BackendEndpointTrustTests.cs
│   │   ├── BackendListenerOwnerTests.cs
│   │   ├── BackendProviderStoreBugNotes.md
│   │   ├── BackendProviderStoreTests.cs
│   │   ├── BackendReconcilerTests.cs
│   │   ├── BackendReconciliationLeaseTests.cs
│   │   ├── BackendSupervisionBugNotes.md
│   │   ├── BackendSupervisorTests.cs
│   │   ├── BackendTestFakes.cs
│   │   ├── Deckle.Anytype.Tests.csproj
│   │   ├── DevSpaceBugNotes.md
│   │   ├── DevSpaceTests.cs
│   │   ├── DialogueGesturesTests.cs
│   │   ├── DocumentGesturesTests.cs
│   │   ├── FakeV2Handler.cs
│   │   ├── LiveTagResolverTests.cs
│   │   ├── ManagementGesturesTests.cs
│   │   ├── MarkdownBodyTests.cs
│   │   ├── NameResolverTests.cs
│   │   ├── OperationRecoveryBugNotes.md
│   │   ├── ProjectGesturesBugNotes.md
│   │   ├── ProjectGesturesTests.cs
│   │   ├── QueryGesturesTests.cs
│   │   ├── ReadGesturesBugNotes.md
│   │   ├── ReadGesturesTests.cs
│   │   ├── SchemaAdminGesturesTests.cs
│   │   ├── SessionGesturesBugNotes.md
│   │   ├── SessionGesturesTests.cs
│   │   ├── SpaceWriteLockTests.cs
│   │   ├── TaskGesturesBugNotes.md
│   │   └── TaskGesturesTests.cs
│   ├── Deckle.Audio.Tests/
│   │   ├── AudioRecordingHostDefaultsTests.cs
│   │   ├── CaptureAnomalyEpisodeTests.cs
│   │   ├── CaptureDataWatchdogTests.cs
│   │   ├── CaptureSettingsMigrationTests.cs
│   │   ├── Deckle.Audio.Tests.csproj
│   │   ├── DeckleAudioSourceTests.cs
│   │   ├── ListeningTakeTests.cs
│   │   ├── MicLevelCheckTests.cs
│   │   ├── MicrophoneErrorClassificationTests.cs
│   │   ├── MicrophoneTelemetryCalculatorTests.cs
│   │   ├── OperationalObservabilityCollection.cs
│   │   ├── Pcm16BufferTests.cs
│   │   ├── PcmBufferTests.cs
│   │   ├── PcmConversionTests.cs
│   │   ├── TranscriptionPreprocessorTests.cs
│   │   ├── WaveInBufferRingBugNotes.md
│   │   └── WaveInBufferRingTests.cs
│   ├── Deckle.Autocorrect.Tests/
│   │   ├── AccentFoldingTests.cs
│   │   ├── AccentIndexTests.cs
│   │   ├── AnticipationLeadOracleTests.cs
│   │   ├── AnticipationTransactionJoinTests.cs
│   │   ├── AssemblyInfo.cs
│   │   ├── AutocorrectBenchmarkTests.cs
│   │   ├── AutocorrectDecisionMapTests.cs
│   │   ├── AutocorrectEngineBackspaceTests.cs
│   │   ├── AutocorrectEngineCaretRecoveryTests.cs
│   │   ├── AutocorrectEngineCorpusTests.cs
│   │   ├── AutocorrectEngineCorrectionTests.cs
│   │   ├── AutocorrectEngineGateTests.cs
│   │   ├── AutocorrectEngineHarness.cs
│   │   ├── AutocorrectEngineLearningTests.cs
│   │   ├── AutocorrectEngineLifecycleTests.cs
│   │   ├── AutocorrectEngineMistouchTests.cs
│   │   ├── AutocorrectEngineObservabilityTests.cs
│   │   ├── AutocorrectKeyboardQualityTests.cs
│   │   ├── AutocorrectLexiconArtifactsTests.cs
│   │   ├── AutocorrectSentenceTypingScenarioTests.cs
│   │   ├── AutocorrectSettingsTests.cs
│   │   ├── AutocorrectTypingScenarioTests.cs
│   │   ├── AutocorrectTypingStreamReplayMaintenanceTests.cs
│   │   ├── BackgroundRerankLaneTests.cs
│   │   ├── BigramPairDisambiguatorTests.cs
│   │   ├── BuildDataMaintenanceTests.cs
│   │   ├── CamembertRerankerIntegrationTests.cs
│   │   ├── CandidateCompletionPlanTests.cs
│   │   ├── CaretParagraphContextTests.cs
│   │   ├── CaretSentenceContextTests.cs
│   │   ├── CasePatternTests.cs
│   │   ├── CompositeAmbiguityProbeTests.cs
│   │   ├── ConservativeTypoCorrectorTests.cs
│   │   ├── CorpusReaderTests.cs
│   │   ├── CorrectionBenchmarkSummaryTests.cs
│   │   ├── CorrectionTraceTests.cs
│   │   ├── Deckle.Autocorrect.Tests.csproj
│   │   ├── DiacriticsRestorerTests.cs
│   │   ├── DomainActivationTests.cs
│   │   ├── DomainPackMaintenanceTests.cs
│   │   ├── DomainPackManifestTests.cs
│   │   ├── EffectiveLexiconTests.cs
│   │   ├── ElisionCorrectorTests.cs
│   │   ├── FrenchSentenceRerankerTests.cs
│   │   ├── FrequencyLexiconTests.cs
│   │   ├── GenderVariantProbeTests.cs
│   │   ├── GrammarCorrectorTests.cs
│   │   ├── HarvestDataTests.cs
│   │   ├── HarvestFilterTests.cs
│   │   ├── InjectionPlanTests.cs
│   │   ├── KeyDecoderTests.cs
│   │   ├── MarginCalibrationTests.cs
│   │   ├── MistouchFamilyCorrectorTests.cs
│   │   ├── MistouchFamilyStoreTests.cs
│   │   ├── MistouchMinerTests.cs
│   │   ├── MistouchMiningMaintenanceTests.cs
│   │   ├── MorphalouReaderTests.cs
│   │   ├── MorphologyCorrectionTests.cs
│   │   ├── OnnxJudgeSerialCollection.cs
│   │   ├── OnnxSentenceScorerTests.cs
│   │   ├── OnnxSlotRerankerTests.cs
│   │   ├── PairModelTrainerTests.cs
│   │   ├── PersonalDictionaryTests.cs
│   │   ├── PersonalWordAdmissionTests.cs
│   │   ├── QwenAdapterCompatibilityProtocolTests.cs
│   │   ├── QwenAdapterCompatibilityTests.cs
│   │   ├── ReplayRunnerTests.cs
│   │   ├── RestorationEvaluatorTests.cs
│   │   ├── SentenceAlignmentTests.cs
│   │   ├── SentenceBatchExperimentTests.cs
│   │   ├── SentenceCalibrationTests.cs
│   │   ├── SentenceCanonicalLatencyTests.cs
│   │   ├── SentenceCorpusTests.cs
│   │   ├── SentenceDecisionInventoryTests.cs
│   │   ├── SentenceOrderAblationTests.cs
│   │   ├── SentenceProfileTests.cs
│   │   ├── SentenceProposalGateTests.cs
│   │   ├── SentenceProposalReplayMaintenanceTests.cs
│   │   ├── SentenceProposalVerifierTests.cs
│   │   ├── SentenceReplayMaintenanceTests.cs
│   │   ├── SentenceReplayTests.cs
│   │   ├── SentenceRerankCoordinatorTests.cs
│   │   ├── SentenceUnanimityBundleTests.cs
│   │   ├── SimulatedTextSurface.cs
│   │   ├── SurfaceProfileMaintenanceTests.cs
│   │   ├── SurfaceProfilerTests.cs
│   │   ├── SurfaceProfileStoreTests.cs
│   │   ├── TruthOverlayTests.cs
│   │   ├── TypedWordTrackerTests.cs
│   │   ├── TypingStreamTests.cs
│   │   ├── VerbMorphologyTests.cs
│   │   ├── VerifiedCaretSentenceTests.cs
│   │   └── WordBoundariesTests.cs
│   ├── Deckle.Catalog.Tests/
│   │   ├── CubicBezierTests.cs
│   │   ├── Deckle.Catalog.Tests.csproj
│   │   ├── DefaultValueTextTests.cs
│   │   ├── GestureHoldTests.cs
│   │   ├── HotkeyChordTests.cs
│   │   ├── HotkeyChordTextTests.cs
│   │   ├── MagnitudeGrainBugNotes.md
│   │   ├── MagnitudeGrainIntegerTests.cs
│   │   ├── MagnitudeGrainTests.cs
│   │   ├── SettingFactoryTests.cs
│   │   └── SettingsExpanderItemsBugNotes.md
│   ├── Deckle.Chrono.Tests/
│   │   ├── ChronoFormatterTests.cs
│   │   ├── ChronoTimerTests.cs
│   │   ├── Deckle.Chrono.Tests.csproj
│   │   └── DeckleChronoSourceTests.cs
│   ├── Deckle.Core.Tests/
│   │   ├── Deckle.Core.Tests.csproj
│   │   ├── DpiTests.cs
│   │   ├── JsonSettingsStoreTests.cs
│   │   ├── SpacingTests.cs
│   │   ├── Win32UtilTests.cs
│   │   └── WindowDescriptionBugNotes.md
│   ├── Deckle.Diagnostics.Logging.Tests/
│   │   ├── Deckle.Diagnostics.Logging.Tests.csproj
│   │   ├── LogFilterSelectionTests.cs
│   │   ├── LoggingSettingsTests.cs
│   │   ├── LogTransferTests.cs
│   │   ├── OperationalSinkRoutingTests.cs
│   │   ├── RangeObservableCollectionBugNotes.md
│   │   ├── RangeObservableCollectionRegressionTests.cs
│   │   └── RangeObservableCollectionTests.cs
│   ├── Deckle.Diagnostics.Telemetry.Tests/
│   │   ├── Deckle.Diagnostics.Telemetry.Tests.csproj
│   │   └── TelemetryListenerBootstrapTests.cs
│   ├── Deckle.Diagnostics.Tests/
│   │   ├── BoundedWriteQueueRegressionTests.cs
│   │   ├── CrashDumpCaptureTests.cs
│   │   ├── CrashRelaunchPolicyBugNotes.md
│   │   ├── CrashRelaunchPolicyRegressionTests.cs
│   │   ├── CrashRelaunchPolicyTests.cs
│   │   ├── Deckle.Diagnostics.Tests.csproj
│   │   ├── DeckleCancellationSourceTests.cs
│   │   ├── DeckleNetworkSourceTests.cs
│   │   ├── DeckleResourceSourceTests.cs
│   │   ├── DeckleSettingsUxSourceTests.cs
│   │   ├── DeckleThemeSourceTests.cs
│   │   ├── DeckleThreadingSourceTests.cs
│   │   ├── DeckleWindowingSourceTests.cs
│   │   ├── DiagnosticsProviderCollection.cs
│   │   ├── DispatchEventListenerBugNotes.md
│   │   ├── DispatchEventListenerIncidentTests.cs
│   │   ├── DispatchEventListenerRegressionTests.cs
│   │   ├── DispatchEventListenerTests.cs
│   │   ├── JsonlLineSerializerRegressionTests.cs
│   │   ├── JsonlLineSerializerTests.cs
│   │   ├── JsonlSinkBugNotes.md
│   │   ├── JsonlSinkLifecycleTests.cs
│   │   ├── JsonlSinkRegressionTests.cs
│   │   ├── JsonlSinkRotationTests.cs
│   │   ├── LogLineFormatterTests.cs
│   │   ├── OperationalLogAdmissionCollection.cs
│   │   ├── OperationalLogAdmissionTests.cs
│   │   └── WorkIdTests.cs
│   ├── Deckle.Home.Tests/
│   │   ├── Deckle.Home.Tests.csproj
│   │   ├── FakeHomeAnytypeServer.cs
│   │   ├── HomeBodyTests.cs
│   │   ├── HomeCodeTests.cs
│   │   ├── HomeGesturesBugNotes.md
│   │   ├── HomeGesturesTests.cs
│   │   ├── HomeMcpTests.cs
│   │   ├── HomeRelationTests.cs
│   │   ├── HomeSchemaAlignmentTests.cs
│   │   ├── HomeSearchTests.cs
│   │   ├── HomeToolCatalogTests.cs
│   │   └── HomeTypedToolCatalogTests.cs
│   ├── Deckle.Hud.Tests/
│   │   ├── AnimationGateTests.cs
│   │   ├── ConicPaletteTests.cs
│   │   ├── Deckle.Hud.Tests.csproj
│   │   ├── DeckleHudSourceTests.cs
│   │   ├── HudSettingsOverridesTests.cs
│   │   ├── ProximityFadeTests.cs
│   │   ├── ProximityRollupAggregatorTests.cs
│   │   ├── TranscriptHistoryTests.cs
│   │   ├── TranscriptPanelBugNotes.md
│   │   ├── TranscriptPanelLayoutTests.cs
│   │   ├── UtteranceCountdownBugNotes.md
│   │   ├── UtteranceCountdownCourseTests.cs
│   │   ├── UtteranceCountdownGeometryTests.cs
│   │   └── UtteranceCountdownStateTests.cs
│   ├── Deckle.Input.PrecisionScroll.Tests/
│   │   ├── Fixtures/
│   │   │   └── precision-scroll-baseline.json
│   │   ├── Deckle.Input.PrecisionScroll.Tests.csproj
│   │   ├── DecklePrecisionScrollSourceTests.cs
│   │   ├── PerceptualBaselineTests.cs
│   │   ├── PrecisionScrollGestureTests.cs
│   │   ├── PrecisionScrollTuningTests.cs
│   │   └── WheelTickQueueTests.cs
│   ├── Deckle.Input.Tests/
│   │   ├── ContactDeviceRegistryTests.cs
│   │   ├── ContactFrameAssemblerTests.cs
│   │   ├── Deckle.Input.Tests.csproj
│   │   ├── DeckleInputSourceTests.cs
│   │   ├── FocusEventCoalescerTests.cs
│   │   ├── LowLevelMouseHookInteropTests.cs
│   │   ├── MouseInteractionRouterTests.cs
│   │   ├── MouseWheelTimestampTests.cs
│   │   ├── RepeatingIncidentTests.cs
│   │   ├── SyntheticTouchpadFrameBuilderTests.cs
│   │   └── WheelObservationBufferTests.cs
│   ├── Deckle.Input.Trackpad.Tests/
│   │   ├── Deckle.Input.Trackpad.Tests.csproj
│   │   ├── DeckleTrackpadSourceTests.cs
│   │   └── ThreeFingerDragRecognizerTests.cs
│   ├── Deckle.Install.Tests/
│   │   ├── Deckle.Install.Tests.csproj
│   │   ├── InstallPathsTests.cs
│   │   ├── ReleaseResolverBugNotes.md
│   │   ├── ReleaseResolverRegressionTests.cs
│   │   ├── ReleaseResolverTests.cs
│   │   └── RunningProcessesBugNotes.md
│   ├── Deckle.Lighting.Ambient.Tests/
│   │   ├── AmbientBrightnessBugNotes.md
│   │   ├── AmbientBrightnessCurveTests.cs
│   │   ├── AmbientCaptureRateTests.cs
│   │   ├── AmbientColorSmootherTests.cs
│   │   ├── AmbientHeartbeatWindowTests.cs
│   │   ├── AmbientHueChangeAttributorTests.cs
│   │   ├── AmbientNearBlackTests.cs
│   │   ├── AmbientPresetsTests.cs
│   │   ├── AmbientPushCadenceTests.cs
│   │   ├── AmbientPushGateTests.cs
│   │   ├── AmbientStartSettingsTests.cs
│   │   ├── Deckle.Lighting.Ambient.Tests.csproj
│   │   ├── DeckleAmbientSourceTests.cs
│   │   ├── HueEndpointResolverTests.cs
│   │   └── LightPlacementResolverTests.cs
│   ├── Deckle.Lighting.Tests/
│   │   ├── BugExamples/
│   │   │   └── dns-sd-pending-status-lifetime.json
│   │   ├── Deckle.Lighting.Tests.csproj
│   │   ├── DeckleLightingSourceTests.cs
│   │   ├── HueBridgeClientIdentityTests.cs
│   │   ├── HueCredentialVaultTests.cs
│   │   ├── HueEntertainmentFrameBuilderTests.cs
│   │   ├── HueEntertainmentLightOutputTests.cs
│   │   ├── HueEventStreamEpisodeTests.cs
│   │   ├── HueLightOutputFactoryTests.cs
│   │   ├── HueLocalDiscoveryRegressionTests.cs
│   │   ├── HueLocalDiscoveryTests.cs
│   │   └── LightingObservabilityCollection.cs
│   ├── Deckle.Llm.Rewrite.Tests/
│   │   ├── Deckle.Llm.Rewrite.Tests.csproj
│   │   ├── DictationBatcherTests.cs
│   │   ├── DictationBatchRewriterTests.cs
│   │   ├── LlmSettingsMigrationsTests.cs
│   │   ├── ParagraphDraftTests.cs
│   │   ├── ParagraphRewriteCoordinatorTests.cs
│   │   ├── RewriteDiffGateTests.cs
│   │   ├── RewriteFailureMessageTests.cs
│   │   ├── RewriteServiceFeedbackTests.cs
│   │   ├── SentenceBoundaryTests.cs
│   │   └── SentenceRewriteTests.cs
│   ├── Deckle.Modules.Tests/
│   │   ├── Deckle.Modules.Tests.csproj
│   │   ├── ModuleGraphTests.cs
│   │   ├── PresenceChoiceTests.cs
│   │   └── PresenceFileTests.cs
│   ├── Deckle.Notifications.Tests/
│   │   ├── Deckle.Notifications.Tests.csproj
│   │   ├── DeckleNotificationsSourceTests.cs
│   │   ├── Descriptors.cs
│   │   ├── FakeNotificationChannel.cs
│   │   ├── NotificationCatalogTests.cs
│   │   └── NotificationDispatcherTests.cs
│   ├── Deckle.Orchestration.Tests/
│   │   ├── Fixtures/
│   │   │   ├── appmanifest_480.acf
│   │   │   └── libraryfolders.vdf
│   │   ├── AppMatcherTests.cs
│   │   ├── AppRulesTests.cs
│   │   ├── AppSessionTrackerTests.cs
│   │   ├── Deckle.Orchestration.Tests.csproj
│   │   ├── DeckleOrchestrationSourceTests.cs
│   │   ├── DisplayConfigLayoutTests.cs
│   │   ├── ExecutableLocatorTests.cs
│   │   ├── Fixture.cs
│   │   ├── ForegroundChangeMappingTests.cs
│   │   ├── KeyValuesReaderTests.cs
│   │   ├── ProfileTransactionTests.cs
│   │   ├── ScanOnDiskTests.cs
│   │   └── SteamLibraryTests.cs
│   ├── Deckle.Playground.Tests/
│   │   ├── CorrectionApplicationEvidenceTests.cs
│   │   ├── CorrectionApplicationTrialTests.cs
│   │   ├── Deckle.Playground.Tests.csproj
│   │   ├── HudTargetControlsTests.cs
│   │   ├── HudTuningBugNotes.md
│   │   ├── HudTuningSectionsBugNotes.md
│   │   ├── HudViewModelTests.cs
│   │   ├── IlReferences.cs
│   │   ├── MicrophonePreviewFailureTests.cs
│   │   ├── MicrophonePreviewFeedTests.cs
│   │   ├── TuningModelTests.cs
│   │   └── UtteranceCycleTests.cs
│   ├── Deckle.Presets.Tests/
│   │   ├── Deckle.Presets.Tests.csproj
│   │   └── PresetFamilyTests.cs
│   ├── Deckle.Security.Tests/
│   │   ├── Deckle.Security.Tests.csproj
│   │   └── SecretVaultTests.cs
│   ├── Deckle.Setup.Tests/
│   │   ├── DataRootRelocatorTests.cs
│   │   ├── DataRootTreeBugNotes.md
│   │   ├── DataRootTreeRegressionTests.cs
│   │   ├── DataRootTreeTests.cs
│   │   ├── Deckle.Setup.Tests.csproj
│   │   ├── LocMirrorBugNotes.md
│   │   ├── PayloadDeploymentTests.cs
│   │   ├── SetupStringMirrorTests.cs
│   │   ├── UpdateHandoffBugNotes.md
│   │   └── UpdatePredecessorTests.cs
│   ├── Deckle.Shell.Foreground.Tests/
│   │   ├── Deckle.Shell.Foreground.Tests.csproj
│   │   └── FullscreenGeometryTests.cs
│   ├── Deckle.Shell.TaskbarCover.Tests/
│   │   ├── CoverGeometryTests.cs
│   │   ├── CoverStackingTests.cs
│   │   ├── CoverSuppressionTests.cs
│   │   ├── Deckle.Shell.TaskbarCover.Tests.csproj
│   │   ├── TaskbarCoverBugNotes.md
│   │   └── TaskbarPlacementTests.cs
│   ├── Deckle.Shell.Tests/
│   │   ├── Deckle.Shell.Tests.csproj
│   │   ├── DispatcherQueueExtensionsTests.cs
│   │   ├── GlobalHotkeyBindingsTests.cs
│   │   ├── ResizeGestureTests.cs
│   │   ├── TrayIconBugNotes.md
│   │   └── TrayIconRegistrationTests.cs
│   ├── Deckle.Shell.TrayMenu.Tests/
│   │   ├── Deckle.Shell.TrayMenu.Tests.csproj
│   │   ├── TrayMenuBugNotes.md
│   │   ├── TrayMenuPlacementTests.cs
│   │   └── TrayWindowOwnershipTests.cs
│   ├── Deckle.Speech.Tests/
│   │   ├── Deckle.Speech.Tests.csproj
│   │   ├── SpeechEngineTests.cs
│   │   └── SpeechSettingsTests.cs
│   ├── Deckle.TestSupport/
│   │   ├── Deckle.TestSupport.csproj
│   │   ├── EventArgsExtensions.cs
│   │   ├── LoopbackHttpListenerLease.cs
│   │   ├── SharedFileReader.cs
│   │   ├── TestEventListener.cs
│   │   ├── WindowsAppSdkBootstrap.cs
│   │   └── WindowsAppSdkModuleInitializer.cs
│   ├── Deckle.Transcription.Tests/
│   │   ├── CutPresetsTests.cs
│   │   ├── Deckle.Transcription.Tests.csproj
│   │   ├── DeckleLlmSourceTests.cs
│   │   ├── DeckleWhispSourceTests.cs
│   │   ├── DictationEndCaptureBugNotes.md
│   │   ├── DictationRecoveryBugNotes.md
│   │   ├── DictationRecoveryStoreTests.cs
│   │   ├── EnergySegmenterTests.cs
│   │   ├── FileBatchLifecycleTests.cs
│   │   ├── FileTranscriptionQueueTests.cs
│   │   ├── FileTranscriptionResultTests.cs
│   │   ├── LiveTranscriptionPipelineTests.cs
│   │   ├── OperationalObservabilityCollection.cs
│   │   ├── PhantomUtteranceBugNotes.md
│   │   ├── PhantomUtteranceFilterTests.cs
│   │   ├── PreparedFileChannelTests.cs
│   │   ├── RewriteAvailabilityTests.cs
│   │   ├── RewriteProfileSelectionTests.cs
│   │   ├── SegmentedTranscriptionSessionTests.cs
│   │   ├── StreamingBackendAudioTests.cs
│   │   ├── TrailingNoisePadTests.cs
│   │   ├── TranscriptFileWriterDiskTests.cs
│   │   ├── TranscriptFileWriterTests.cs
│   │   ├── TranscriptionDeliveryTests.cs
│   │   ├── TranscriptionObservabilityContractTests.cs
│   │   ├── TranscriptionSettingsMigrationTests.cs
│   │   ├── UnitBezierTests.cs
│   │   ├── UtteranceProgressMeterTests.cs
│   │   ├── UtteranceProgressTests.cs
│   │   └── VadServiceIncidentTests.cs
│   ├── Deckle.Transcription.Whisper.Tests/
│   │   ├── Deckle.Transcription.Whisper.Tests.csproj
│   │   ├── HallucinationBugNotes.md
│   │   ├── KnownHallucinationsTests.cs
│   │   ├── NativeRuntimeTests.cs
│   │   ├── OperationalObservabilityCollection.cs
│   │   ├── RepetitionDetectorTests.cs
│   │   ├── SpeechModelFilesTests.cs
│   │   ├── SpeechModelResolverTests.cs
│   │   ├── WhisperComputeDeviceObserverTests.cs
│   │   ├── WhisperContextBugNotes.md
│   │   ├── WhisperContextParamsMapperTests.cs
│   │   ├── WhisperParamsMapperTests.cs
│   │   └── WhisperReferenceAudioSystemTests.cs
│   ├── Deckle.Vad.Tests/
│   │   ├── Deckle.Vad.Tests.csproj
│   │   └── SileroSpeechTimestampsTests.cs
│   ├── Deckle.Vision.Tests/
│   │   ├── CapturePacerTests.cs
│   │   ├── ContentPeakReleaseTests.cs
│   │   ├── Deckle.Vision.Tests.csproj
│   │   └── DeckleVisionSourceTests.cs
│   ├── Directory.Build.props
│   └── GLOSSARY.md  — glossary-deckle-tests Test taxonomy. It names the four categories inside the automatic scope (unit, i…
├── .editorconfig
├── .gitattributes
├── .gitignore
├── AGENTS.md  — Root agent-instructions for Deckle — identity, hard rules, posture, and where t…
├── CHANGELOG.md
├── CLAUDE.md
├── CONTRIBUTING.md
├── deckle.code-workspace
├── Deckle.Tests.sln
├── Directory.Build.props
├── Directory.Build.rsp
├── Directory.Build.targets
├── Directory.Packages.props
├── GLOSSARY-MAP.md  — glossary-map-deckle Index of Deckle's bounded-context glossaries — where each module's GLOSSARY.md…
├── LICENSE
├── NOTICE.md
├── README.md
├── release-history.json
└── TREE.md
```