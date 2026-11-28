# Tests that count events of a process-wide source

Read this first when the merge gate says a test is unstable and the test uses a
`TestEventListener`.

## Why such a test races

An `EventSource` is one object per process, and a `TestEventListener` collects
every event of its provider, whoever writes it. xUnit runs the test classes of
one assembly in parallel, one test collection at a time per thread. So when a
class counts events (`Assert.Single`, `Assert.Empty`, a `Count`) while another
class of the same assembly runs code that writes the same events, the count
sometimes includes the other class's events and the test turns red.

Three ways keep two classes from overlapping:

- **The same collection.** Classes with the same `[Collection(Name)]` run one
  after the other. The collection still runs beside the rest of the assembly.
  Enough when every class that writes the counted events is in it.
- **A collection that runs alone.** `[CollectionDefinition(Name,
  DisableParallelization = true)]` runs the collection after the parallel ones,
  with nothing beside it. Needed when the writers are spread across the
  assembly, or when the tests also set process-wide state such as
  `OperationalLogAdmission`. It costs suite time: use it only for a real race.
- **No parallelism in the assembly.** `[assembly:
  CollectionBehavior(DisableTestParallelization = true)]`, in `AssemblyInfo.cs`.

A name given in `[Collection("...")]` without a `[CollectionDefinition]` class
still groups the classes, but nothing says why; each collection here now has its
definition.

## How to judge a class

1. Find the source it listens to and the events its assertions count.
2. Find the product code that writes those events, then the other test classes
   of the same assembly that run that code.
3. If one of them can run at the same time (not in the same collection, and
   neither is in a collection that runs alone), it is a race: put the writer in
   the counter's collection, or make the collection run alone.
4. A filter on a value only this test writes (a fresh `Guid`, an entry id)
   keeps the count safe without a collection.

## Verdicts, 28 November 2026

The classes counting events of a process-wide source, and the named collections
that had no definition.

### Isolated

| Assembly | What raced | Fix |
| --- | --- | --- |
| Deckle.FolderMap.Tests | `MapEventsTests` counts the Warning `MapInventoryFailed` in collection "foldermap provider singleton"; `MapInventoryTests` builds a map of a missing folder, which writes that event, in a different collection, "folders provider singleton". | One collection, `FolderMapProviderCollection`, for every class that builds a map. |
| Deckle.Notifications.Tests | `DeckleNotificationsSourceTests` counts `CatalogRegistered` with `Assert.Single`; `NotificationCatalogTests`, in no collection, registers catalogues, which writes that event. | `NotificationCatalogTests` joins `NotificationDispatcherCollection`. |

### Safe

| Assembly, class | Why |
| --- | --- |
| Deckle.App.Tests, `WindowOpenedEventTests` | `WindowOpened` is written only by the app's windows, which no test of the assembly opens. |
| Deckle.Settings.Tests, `PageOpenedEventTests` | `PageOpened` is written only by the Settings window's navigation, which no test runs. |
| Deckle.Llm.Tests, `LlmUsageLogTests` | Its filter is a profile made of a fresh `Guid`; it is the only class of the assembly that sets `OperationalLogAdmission`. |
| Deckle.Setup.Tests, `DeckleSetupSourceTests`, `SetupCancellationLogTests`, `UpdatePredecessorTests` | The events counted are written by the pages of the setup window, never run, and by `UpdatePredecessor`, which only `UpdatePredecessorTests` calls. |
| Deckle.Shell.Tests, `TrayIconFallbackObservabilityTests` | `TrayIconFallback` is written by `TrayIconManager.LoadIconFromFile`, which no other class calls. |
| Deckle.Orchestration.Tests, `OrchestrationAppNameTests`, `OrchestrationIncidentTests` | The session tests filter on the entry's fresh id. The other writers of the incident events (`RuntimeRulesTests`, `DeckleOrchestrationSourceTests`) are in `OrchestrationObservabilityCollection`, which runs alone. |
| Deckle.Diagnostics.Telemetry.Tests | One test class in the assembly. |
| Deckle.Diagnostics.Tests, the source tests of Cancellation, Resource, SettingsUx, Theme, Threading, and `LoopPulseTests` | The assembly references only Deckle.Diagnostics, whose own code never writes these sources; each is written by its own test class, and by `PathRedactionTests` for SettingsUx, which runs alone. |
| Deckle.Diagnostics.Tests, `DispatchEventListenerTests` | Its sinks keep only its private source, `Deckle-DispatchTests`. |
| Deckle.Folders.Tests, collection `FoldersProviderCollection` | No class outside it writes on Deckle-Folders: `DarktableTests` never opens darktable, and `RawReadingMaintenanceTests` is explicit. |
| Deckle.Hud.Tests, collection `HudObservabilityCollection` | Outside it, `HudMessageFitTests` reads a constant of the overlay window and writes nothing. |
| Deckle.Paste.Tests, collection `PasteProviderCollection` | No class outside it calls the store, the saver or the window log. |
| Deckle.Projects.Tests, collection `ProjectsProviderCollection` | Outside it, `ManifestParsersTests` calls `ManifestVersions.Parse`, which writes nothing; only `Read` does. |
| Deckle.Vision.Tests, collection `VisionObservabilityCollection` | Outside it, the sampler tests use the sampler's pure helpers and its path enum; no capture runs. |
| Deckle.Anytype.Tests, Deckle.Anytype.Mcp.Tests, Deckle.Contributions.Tests, Deckle.Home.Tests, Deckle.Lighting.Ambient.Tests, Deckle.Modules.Tests | Parallelism is off for the whole assembly (`AssemblyInfo.cs`). |

Not judged here: an assembly whose listening classes are all already in a
collection that runs alone (Audio, Autocorrect, Input, Lighting, Llm.Rewrite,
PrecisionScroll, Taskbar, Transcription, Parakeet, Vad, Trackpad).
