# ADR-0011 — Corpus normalisé comme dataset ML

**Status** — accepted le 2026-05-23

## Contexte

Le corpus de transcription dans Deckle a vécu sa première année comme un journal d'enregistrements paramétré par le profil de réécriture LLM qui suivait. La ligne JSONL et le WAV partageaient un même slug `<profile-slug>-<profile-id>`, et le bloc émetteur dans `TranscriptionEngine.Transcribe` était gaté par `if (CorpusEnabled && profile is not null)`. Ce design a deux conséquences gênantes en pratique.

Première conséquence — quand l'utilisateur transcrit sans qu'un profil rewrite ne soit résolu (LLM désactivé, aucun profil bound aux hotkeys primaire/secondaire, aucune auto-rule qui matche la durée ou le nombre de mots), aucune trace n'est produite, même quand `CorpusEnabled` et `RecordAudioCorpus` sont tous deux à `true`. Le bug d'origine de ce chantier — état disque vérifié à 0 WAV jamais écrit sous `%LOCALAPPDATA%\Deckle\telemetry\` malgré un `RecordAudioCorpus` actif depuis des mois — vient de là.

Deuxième conséquence — le bucket par profil rewrite couple le dataset audio au cycle de vie des prompts. Un rename de profil change le slug du dossier ; une suppression de profil oriente les analyses passées vers un dossier orphelin ; une variation du prompt sans rename rend invisible le fait que le sous-dataset n'est plus homogène. Les artefacts résiduels sur disque (`affinage-0e58b77568f4/`, `arrangement-f3c815d6259d/`, `lissage-87e3eb39805f/` avec leurs `corpus.jsonl` legacy mais aucun sous-dossier `audio/`) en témoignent : la structure choisie n'a jamais produit le dataset qu'on espérait.

Le besoin réel auquel le corpus répond — un dataset ML utilisable pour calibrer le pipeline ASR, comparer les modèles Whisper, mesurer dans le temps la qualité de chaque profil rewrite, et préparer l'arrivée d'un second backend ASR (Voxtral, cf. [ADR-0010](./0010-backend-asr-pluggable-via-iasrbackend.md)) — demande une architecture qui sépare l'observation des deux couches indépendantes du pipeline (transcription brute / réécriture LLM) et qui dédoublonne l'audio par invocation, pas par profil.

## Options considérées

- **A. Garder le schéma actuel, retirer seulement le gate `profile is not null`.** Corrige le bug visible. Mais le couplage WAV ↔ profil rewrite reste, les analyses ASR sans rewrite atterrissent dans un bucket fourre-tout, et la préparation pour Voxtral instruction-nommée n'avance pas. Bandage local sans gain structurel.

- **B. Un seul event corpus enrichi qui agrège ASR + rewrite avec un champ `rewrite_profile_id` nullable.** Une ligne JSONL par transcription, qu'il y ait eu rewrite ou pas. Élégant en apparence. Mais : impossible d'éviter de répéter le texte ASR si on veut explorer plusieurs profils rewrite sur la même entrée, schéma JSONL avec moitié des champs vides quand `rewrite` est absent, pas de séparation propre des analyses par couche, mêmes problèmes de bucketing.

- **C. Deux events séparés et un audio plat dédupliqué par `transcription_id`.** `CorpusAsrRecorded` capture la sortie ASR (Whisper aujourd'hui, Voxtral demain en mode mot-pour-mot ou en mode instruction nommée). `CorpusRewriteRecorded` capture la sortie réécriture LLM. Quand un rewrite tourne, on émet les deux events avec le même `transcription_id`. L'audio vit dans un dossier `audio/` plat sous `telemetry/`, une seule fois par transcription, référencé par basename depuis chaque ligne JSONL. Le routage du JSONL bucket les sorties ASR par tier de longueur (cinq tiers `very-short` 0-30 / `short` 30-200 / `medium` 200-1000 / `long` 1000-3000 / `very-long` >3000 sur le `word_count`) et bucket les sorties rewrite par profil (`rewrite-<sluggified-name>-<profile-id>`).

## Décision

Option C retenue. Le corpus devient un dataset ML normalisé, séparé en trois axes orthogonaux : un fichier audio par transcription (gaté par `RecordAudioCorpus`), une ligne JSONL ASR par transcription bucketée par couche d'inférence et par tier de longueur (gaté par `CorpusEnabled`), une ligne JSONL rewrite par transcription quand un profil tourne, bucketée par profil (même gate `CorpusEnabled`).

L'arborescence cible sous `<UserDataRoot>/telemetry/` est `audio/<transcription_id>.wav` pour l'audio, `corpus/raw/<tier>/corpus.jsonl` pour l'ASR en mode brut (Whisper aujourd'hui, futur Voxtral universel mot-pour-mot), `corpus/voxtral-<sluggified-instruction-name>/<tier>/corpus.jsonl` pour le futur mode Voxtral instruction-nommée, `corpus/rewrite-<sluggified-name>-<profile-id>/corpus.jsonl` pour les sorties LLM. Le `transcription_id` est un Guid court (format `N`, 32 hex sans tirets) généré une fois par invocation du pipeline et porté par toutes les lignes JSONL et par le nom du WAV. Combiné au `SessionId` process-scope déjà émis par `DeckleEventSource`, il donne une jointure stable.

Le tier ASR est calculé sur le `word_count` du texte transcrit, avec cinq seuils figés en code (`very-short` 0-30, `short` 30-200, `medium` 200-1000, `long` 1000-3000, `very-long` >3000). Les sorties rewrite ne sont délibérément pas tier-bucketées — la propriété sémantique du bucket rewrite est la nature du profil qui a produit le texte, pas sa longueur. Le `word_count` du texte rewrite reste enregistré comme colonne du JSONL pour les analyses, juste pas utilisé pour le routage disque.

Quand un profil rewrite tourne, la couche ASR est par construction en mode brut — pas de combo Voxtral-instruction-nommée + rewrite-LLM dans un même pipeline. `CorpusAsrRecorded` part alors en `raw/<tier>/` et `CorpusRewriteRecorded` part en `rewrite-<name>-<id>/`, les deux portant le même `transcription_id`.

Côté nomenclature, deux nouveaux events sur `DeckleWhispSource` (`CorpusAsrRecorded`, `CorpusRewriteRecorded`), un nouveau listener `RoutedJsonlEventListener` dans `Deckle.Diagnostics.Listeners` qui résout le path par event via un `Func<EventEntry,string>`, et deux helpers dans `Deckle.Transcription.Corpus` (`CorpusTier` pour les seuils, `PromptTemplateHash` pour un SHA256 16 hex du template effectif d'un profil — permet d'invalider les analyses si l'utilisateur retouche un prompt sans changer l'ID).

## Conséquences

Devient plus facile : exploiter le corpus comme dataset ASR sans avoir à dédoublonner les WAVs côté analyse ; comparer Whisper vs futur backend Voxtral sur le même corpus brut ; mesurer la qualité d'un profil rewrite sur des séquences ASR identiques (jointure par `transcription_id`) ; brancher le futur mode Voxtral instruction-nommée sans casser le schéma existant — un nouveau bucket `voxtral-<instruction>/` à côté de `raw/`. Plus facile aussi de produire des analyses tier-stratifiées sans regrouper en post-traitement.

Devient plus difficile : le `TelemetryListenerBootstrap` héberge maintenant un listener routé en plus des trois plats existants, ce qui ajoute une classe à maintenir. Les paths JSONL contiennent des composants dérivés du nom de profil rewrite — la sanitation via `CorpusPaths.Sanitize` doit être appliquée systématiquement côté producer. La séparation des deux events demande de garder en cohérence le `transcription_id` à travers les deux émissions — discipline locale, mais une seule pipeline concernée.

Devient impossible : retrouver une jointure WAV ↔ profil pour les artefacts legacy générés avant cette refonte. Les dossiers `affinage-*`, `arrangement-*`, `lissage-*` et le `corpus.jsonl` à la racine de `telemetry/` restent sur disque tels quels — aucune migration de contenu. Justification : ces données n'ont pas de `transcription_id`, et donc aucune façon d'être reliées à un futur WAV dans le nouveau modèle. Mieux vaut ignorer qu'inventer une fausse jointure. L'utilisateur peut supprimer manuellement s'il veut libérer l'espace.

L'ancien event `CorpusRecorded` reste émis et écouté le temps de la transition. Le retrait définitif (event sur le provider + listener sur le bootstrap) est la dernière étape du chantier, une fois le nouveau pipeline validé en live. Aucune dépendance externe au `corpus.jsonl` legacy à la racine — c'est un fichier interne, l'outillage de benchmark Python tape déjà sur les sous-dossiers de profil.
