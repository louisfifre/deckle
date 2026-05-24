# CLAUDE.md — Deckle.Diagnostics.Telemetry

Module enfant de `Deckle.Diagnostics` qui porte la **persistance structurée** des télémétries Deckle. Configure au boot les `JsonlEventListener` du parent avec leurs chemins de fichier et leurs prédicats de filtrage, et expose les settings utilisateur de consentement qui gardent ces listeners (latency, microphone, corpus, application log).

Le module dépend de `Deckle.Diagnostics` (interfaces + JsonlEventListener) et `Deckle.Core` (AppPaths pour les chemins de fichier). Aucune dépendance vers `Deckle.Logging` legacy.

## Responsabilités

`TelemetrySettings` porte les toggles de consentement utilisateur. Les valeurs par défaut sont prudentes — la posture est fermée tant que l'utilisateur n'a pas opt-in.

- **`LatencyEnabled`** — bool, on par défaut sur installation dev, off en preview release. Gate l'écriture de `latency.jsonl`.
- **`MicrophoneTelemetry`** — bool, off par défaut (RGPD : un récap RMS du micro n'est pas du contenu vocal mais reste une mesure du micro de l'utilisateur).
- **`CorpusEnabled`** — bool, off par défaut. Gate l'écriture des deux événements normalisés du corpus (`CorpusAsrRecorded` vers `<UserDataRoot>/telemetry/corpus/<bucket>/<tier>/corpus.jsonl`, `CorpusRewriteRecorded` vers `<UserDataRoot>/telemetry/corpus/<bucket>/corpus.jsonl`). Schéma posé par ADR-0011 : la couche ASR est tier-bucketée par longueur (`raw/very-short/`, `raw/short/`, …) et le rewrite est plat-bucketé par profil (`rewrite-<name>-<id>/`). Gate aussi le legacy `corpus.jsonl` à la racine le temps que le pipeline finisse de migrer.
- **`RecordAudioCorpus`** — bool, off par défaut. Gate l'écriture du WAV brut sous `<UserDataRoot>/telemetry/audio/<transcription_id>.wav`, dossier plat dédupliqué par invocation (le même WAV est référencé par les deux lignes JSONL ASR et rewrite). Coût disque non négligeable, consentement à demander.

`TelemetrySettingsService` est le singleton de persistance per-module. Stockage sous `<UserDataRoot>/modules/diagnostics-telemetry/settings.json`. Pattern aligné sur les autres `*SettingsService`.

`TelemetryListenerBootstrap` est l'API d'inscription des listeners. L'App appelle `TelemetryListenerBootstrap.Configure(...)` au boot après `TelemetrySettingsService.Instance` ; le bootstrap instancie un `JsonlEventListener` par fichier de destination (un général pour `app.jsonl`, trois spécialisés pour latency / microphone / corpus) avec le bon prédicat sur l'event name canonique. Chaque listener vérifie sa gate via `TelemetrySettingsService.Instance.Current` à chaque émission — un changement de toggle propage immédiatement sans redémarrage.

## Dialogs de consentement

Les dialogs de demande de consentement (« Activer la télémétrie de latence ? », « Enregistrer le corpus benchmark ? ») vivent ici. Surface XAML standard ContentDialog, ouverte depuis la page Settings concernée. Le pattern reproduit ce que `Deckle.Settings` faisait pour la persistance legacy ; la migration est progressive, les nouveaux dialogs côtoient les anciens jusqu'à la vague 6.

## Frontière avec `Deckle.Diagnostics.Logging`

Le journal applicatif live (LogWindow + filtres SelectorBar + gate `ApplicationLogToDisk`) vit dans `Deckle.Diagnostics.Logging`. Les télémétries structurées vivent ici. Les deux modules dépendent indépendamment du parent ; ils ne se référencent pas entre eux. Le canal `app.jsonl` est nominalement un *journal applicatif* (donc côté Logging) mais sa **persistance** passe par un `JsonlEventListener` — d'où sa configuration côté Telemetry. La frontière propre est : Logging décide *si* on écrit (gate), Telemetry configure *comment* on écrit (path + listener).
