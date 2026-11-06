# LLM settings bug notes

## A custom Ollama endpoint was lost when an Ollama-only settings file migrated

- **Trigger:** first launch after the providers were introduced, with a
  settings file from the Ollama-only era that held a custom `ollamaEndpoint`
  and no `providers` key.
- **Symptom (inferred from the code and reproduced by a test, not observed on
  the maintainer's machine):** the Ollama provider pointed at the default
  `http://localhost:11434/api/generate`, not at the user's endpoint.
- **Cause:** `RepairProviders` carries the legacy endpoint only when
  `Providers.Count == 0`. System.Text.Json keeps a property's initializer when
  its key is absent, so a file without `providers` deserialized with the three
  default providers already in place, the repair saw a migrated file and
  skipped the copy. The legacy field was then rewritten with the custom value,
  which hid the loss.
- **Violated invariant:** "empty providers" must mean "the file had none".
  The trap lives as a comment at `LlmSettings.OnDeserializing`.
- **Recurrence cue:** a migration that tests a collection property for
  emptiness while that property has a non-empty initializer.
- **Guard:** `LlmSettingsFileTests.CustomOllamaEndpointSurvivesTheMigrationOfAnOllamaOnlyFile`.
  Files already migrated by an earlier build keep the default endpoint; the
  custom value may still sit in their legacy `ollamaEndpoint` field.
