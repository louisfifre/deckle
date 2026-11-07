---
description: "Cadrage vérifié (2026-11-06) — les seuils de niveau et de détection de voix dupliqués dans Deckle (segmenteur, avertissement micro faible, fenêtre du HUD, Silero, planchers de 250 ms), ce que chaque pipeline fait tourner, et sept propositions numérotées à accepter ou refuser, de la plus sûre à la plus engageante, relues par un critique."
---

# Un seul signal de voix. Les seuils dupliqués de la transcription et ce qu'on en fait

> Note du 2026-11-07 : la capture décrite ci-dessous (WaveInLoop, waveIn) a été remplacée par la capture PortAudio (WASAPI) en v0.87.0. L'avertissement « micro faible » vit désormais dans src/Deckle.Audio/Internal/LowAudioTracker.cs, alimenté par CaptureLoop.cs et CapturePipeline.cs. Les numéros de ligne WaveInLoop.cs cités plus bas ne correspondent plus à aucun fichier.

Commande de Louis (2026-11-06, dictée) : il y a des dB à plusieurs endroits, pour les utterances et pour la détection de voix ; on démarre l'utterance quand on parle, donc le seuil qui dit « il y a de la voix » et celui qui ouvre l'utterance doivent être le même ; ré-homogénéiser tout ça pour l'UX et pour le code, en se calant sur ce qui existe ; vérifier comment le VAD et le streaming s'imbriquent, parce que le VAD sert aussi en streaming à jeter les utterances de bruit.

Méthode : trois agents ont cartographié le code (inventaire de toutes les valeurs de niveau et de seuil dans `src/`, carte détecteur × pipeline, audit UX des réglages). J'ai recoupé dans le code chaque affirmation sur laquelle une proposition repose. Un relecteur a ensuite attaqué les propositions sur le code ; ses objections confirmées sont intégrées, et deux propositions ont changé de forme (P2 : la tuyauterie et le sens de l'hystérésis ; P5 : l'ordre). Chaque point est marqué **établi** (lu dans le code, fichier cité), **interprétation** (ce que j'en conclus) ou **non vérifié** (seule l'app en marche ou une mesure le dirait). Les chemins sont relatifs à la racine du dépôt.

Ce document remplit la première case de la tâche Anytype « Cadrer la refonte de la capture audio : un seul signal de voix pour le HUD et le segmenteur », et reprend « Parole douce », « Hystérésis du segmenteur » et « Clarifier les réglages d'enregistrement ».

## 1. Le constat : cinq réponses à « est-ce de la voix ? »

Le micro produit un RMS brut par trame de 50 ms, calculé une fois dans `WaveInLoop.EmitSubWindows` (`src/Deckle.Audio/Internal/WaveInLoop.cs`). Cinq lecteurs en tirent chacun leur propre verdict, avec leur propre nombre.

| Lecteur | Question | Valeur | Nature | Où |
|---|---|---|---|---|
| Segmenteur d'énergie | cette trame ouvre ou prolonge une utterance ? | RMS ≥ −45 dBFS, sans hystérésis | réglage `Streaming.Segmenter.ThresholdDbfs`, page Streaming | `src/Deckle.Transcription/Streaming/EnergySegmenter.cs:82,120` |
| Avertissement « micro faible » | l'utilisateur a-t-il parlé dans les 5 premières secondes ? | ≥ −45 dBFS tenu 200 ms | constante codée en dur | `WaveInLoop.cs:96-98,187-208` |
| Contour du HUD | à quelle intensité éclairer le contour ? | fenêtre −55 → −32 dBFS, continue, lissée | réglage `LevelWindow` sans carte depuis le 2026-10-14 | `src/Deckle.Audio/AudioLevelMapper.cs:53-71`, lu par `src/Deckle.Hud/Chrono/HudChrono.Stroke.cs:86-89` |
| Silero (VAD neuronal) | cette fenêtre de 32 ms est-elle de la parole ? | p ≥ 0,5 pour ouvrir, p < 0,35 pour fermer | réglage `SpeechTrim.Threshold`, fenêtre dérivée −0,15 | `src/Deckle.Vad/SileroSpeechTimestamps.cs:34-35` |
| Diagnostic de queue | parlait-on encore au Stop ? | −50 dBFS sur 600 ms | constante, journal seulement | `src/Deckle.Audio/Telemetry/MicrophoneTelemetryCalculator.cs:40` |

**Établi.** Le segmenteur et l'avertissement partagent le même −45 par coïncidence de deux littéraux : quand Louis règle le segmenteur à −38 dBFS, l'avertissement reste à −45. Il peut dire « la voix est là » pendant que le segmenteur n'ouvre aucune utterance. Les deux ne posent pas tout à fait la même question (l'un cherche la parole, l'autre un micro débranché ou trop loin), c'est pour ça que P2 ne les fond pas, il les fait lire la même valeur.

**Établi.** Le plancher du HUD (−55) est 10 dB sous le seuil du segmenteur (−45). Le contour s'éclaire donc pour un son que le segmenteur appelle silence. C'est l'incohérence que Louis voit : « ça bouge mais ça ne se met pas » (tâche « Parole douce »).

**Établi.** Le segmenteur n'a aucune hystérésis : une seule trame sous le seuil fait passer en Hangover, une seule trame au-dessus remet le compteur à zéro (`EnergySegmenter.cs:138-167`). La coupure est sûre (il faut 5 s consécutives), mais la fraction `IsPausing` que lit la jauge du HUD repart à chaque creux de parole (`EnergySegmenter.cs:228-233`, `UtteranceProgress.Fraction`). Interprétation, corrigée par le relecteur : sur une utterance courte le creux ne vaut qu'un centième de la jauge (une trame sur 100), c'est passé la rampe, quand il ne reste que 500 ms à attendre, qu'une trame vaut un dixième et que le clignotement se voit. Silero, lui, a une hystérésis (0,5 / 0,35) et un minimum de silence (100 ms).

### Les deux planchers de 250 ms

**Établi.** Deux réglages, sur la même page, répondent à « un son plus court que ça n'est pas de la parole » : « Minimum utterance (s) » du segmenteur, 0,25 s par défaut (`EnergySegmenterSettings.cs:65`, `EnergySegmenter.cs:191`), et « Minimum speech (s) » de Silero, 0,25 s par défaut (`SileroVadOptions.cs:14`). Deux cartes, deux descriptions, aucune ne dit à quel étage elle agit. Ils ne mesurent pas la même chose : le segmenteur mesure toute l'extension voisée de l'utterance, pauses intérieures comprises, en trames de 50 ms ; Silero mesure chaque plage de parole continue à l'intérieur, en fenêtres de 32 ms. Un « oui… non » passe le segmenteur comme une utterance et peut voir ses deux plages jetées par Silero. Même plancher, deux portées.

### La fenêtre du HUD n'est jamais persistée

**Établi, reproduit.** `LevelWindowSettings` est la seule classe de réglages écrite avec des champs publics et non des propriétés (`src/Deckle.Audio/CaptureSettings.cs:48-55`). `CaptureSettingsService` n'active pas `IncludeFields` (`CaptureSettingsService.cs:18-22`), et rien dans `src/` ne l'active. System.Text.Json écrit donc `"levelWindow": {}` et relit les défauts au lancement. Reproduit avec les mêmes options sur une copie de la classe : sérialisation `{}`, relecture −55 quelle que soit la valeur du fichier. Conséquences : l'auto-calibration (`TranscriptionEngine.Telemetry.cs:27-50`) n'a jamais survécu à un redémarrage, et personne n'a jamais pu l'activer par le fichier. Le mécanisme est mort depuis sa naissance ; les curseurs retirés le 2026-10-14 le faisaient vivre en mémoire seulement.

**Établi.** Le view-model Recording garde encore les quatre propriétés `LevelWindow*`, et `PushToSettings` les réécrit à chaque édition de la page (`src/Deckle.Audio/RecordingViewModel.cs:131-176,224-237`). Code sans carte, pour un réglage qui ne se persiste pas.

## 2. Ce que chaque pipeline fait tourner

| Détecteur | Dictée en streaming | Dictée monolithique | Transcription de fichier | Récupération d'une prise après crash | Où se fait le choix |
|---|---|---|---|---|---|
| Avertissement micro faible | oui | oui | non | non | `WaveInLoop.cs:201` (`RetainsAudio`) |
| Segmenteur d'énergie | oui | **non** | oui, quelle que soit la stratégie | oui | `StateMachine.cs:430` ; `FilePipeline.cs:299` ; `AudioRecovery.cs:240` |
| Silero | si `SpeechTrim.Enabled` | **jamais** | si `SpeechTrim.Enabled` | si `SpeechTrim.Enabled` | `StreamingPipeline.cs:303` ; `_vadService` n'est référencé nulle part ailleurs |
| Prétraitement DSP | par utterance, si activé | sur toute la prise, si activé | jamais | par utterance, si activé | `StreamingPipeline.cs:397` ; `FilePipeline.cs:296` ; `AudioRecovery.cs:237` |
| Pad de bruit −60 dBFS, filtre fantôme | oui | non | oui | oui | `StreamingPipeline.cs:493,580` |
| Filtres Whisper (no_speech, hallucinations, répétitions) | oui | oui | oui | oui | backend partagé |

**Établi.** La question de Louis a sa réponse : ce n'est pas « quand on active le streaming, on désactive le VAD », c'est l'inverse. Quand le streaming est **désactivé** (dictée monolithique, le défaut livré), ni le segmenteur ni Silero ne tournent : Whisper reçoit la prise entière avec ses silences, et seuls ses propres filtres le protègent des hallucinations sur le silence. Avec le streaming activé, Silero tourne par défaut et ne s'arrête que si on le coupe, si le modèle manque, ou après une erreur pour le reste de la prise.

**Établi.** La transcription de fichier et la récupération passent par le segmenteur et Silero quelle que soit la stratégie, alors que la page Streaming cache tout leur groupe de réglages quand le streaming est désactivé (`StreamingPage.xaml:36,45`, `WhisperViewModel.Settings.cs:159,201`). Un utilisateur en monolithique transcrit ses fichiers avec des réglages qu'il ne peut pas voir.

**Établi.** Dans le consommateur, l'ordre est : coupe à l'énergie sur le RMS brut, puis DSP optionnel, puis Silero sur l'audio après DSP, puis pad de bruit, puis Whisper, puis filtre fantôme (`StreamingPipeline.cs:426-512,580`). Le segmenteur et Silero ne lisent donc pas le même signal quand le prétraitement est activé. Interprétation : c'est voulu, Silero juge ce que Whisper entendra ; mais ça veut dire que « niveau de voix » et « confiance du détecteur » ne pourront jamais être une seule valeur, et qu'il ne faut pas essayer.

### Ce qui n'est pas un doublon

Trois familles portent le mot « seuil » sans répondre à la question de la voix, et il ne faut pas les fondre dans le signal de voix :

- `NoSpeechThreshold`, `EntropyThreshold`, `LogprobThreshold` : des portes de qualité du décodage Whisper, passées au natif (`WhisperParamsMapper.cs:69-71`). Non vérifié : ce que whisper.cpp en fait exactement, le natif n'est pas dans le dépôt.
- Le filtre fantôme (12 tokens/s, probabilités 0,7 et 0,1, audio < 1 s) : un jugement sur le texte décodé, pas sur l'audio (`PhantomUtteranceFilter.cs:20-35`).
- Le prétraitement (cible −20 dBFS, compresseur −24, gate −55 désactivé, limiteur −1) et le pad de bruit (−60 dBFS) : du gain et du conditionnement, pas de la détection (`PreprocessingSettings.cs`, `TrailingNoisePad.cs:22`). Le contrôle micro de la page Recording compare le RMS de 5 s à la cible −20 (`MicLevelCheck.cs:53`) et ne parle ni au segmenteur ni au HUD.

### Les données qui existent pour mesurer

Deux sources, toutes deux sous consentement dans Diagnostics, donnent de quoi régler les valeurs ci-dessous sans deviner :

- `microphone.jsonl` (« Log microphone level ») : la distribution du niveau par prise, avec p25, p50, p90 en dBFS (`MicrophoneTelemetryCalculator.cs:71-92`). C'est là qu'on lit où la voix de Louis et le silence de sa pièce se placent.
- Le corpus audio (`WavCorpusWriter`, un WAV 16 kHz par transcription sous `<telemetry-root>/audio/`) : de quoi rejouer un détecteur hors ligne et compter les trames qui changent de camp. Pour cette mesure, le contenu du corpus doit être réglé sur « AlwaysRaw » (toujours brut) : le contenu par défaut est ce que le backend a reçu, découpé par Silero en Monolithic depuis cette branche, donc privé des silences qu'on veut compter. Le relecteur a raison que `TakeAudioRecovery` ne sert pas à ça : il ne garde au plus qu'une prise, celle qui a échoué (`TranscriptionEngine.TakeAudio.cs:21-26`).

Non vérifié : si ces deux consentements sont actuellement actifs chez Louis, et donc combien de données existent déjà.

## 3. Les propositions

Sept propositions, numérotées, de la plus sûre à la plus engageante. Chacune se prend ou se laisse seule, sauf dépendance dite. La taille est une estimation de lecture du code, corrigée par le relecteur qui a compté les références.

### P1. Supprimer la fenêtre de niveau et l'auto-calibration

**Quoi.** Retirer `LevelWindowSettings`, `MicrophoneCalibrationCalculator`, `TryAutoCalibrate` et ses deux appels, `_autoCalibBuffer`, `ApplyLevelWindow` sur `ITranscriptionEngineHost` et ses implémentations (l'adaptateur de l'app et quatre hôtes de test), les quatre propriétés `LevelWindow*` du view-model Recording, les appels au boot (`App.Theme.cs`, `App.Startup.CommandLine.cs`), le champ dans l'événement `CaptureSettingsResolved`, les quatre noms dans `DeckleSettingsUxSource`, et les lignes de `docs/inventaire-exposables.md`. Le mapper du HUD garde deux constantes en attendant P2. Un `"levelWindow": {}` reste dans les fichiers des utilisateurs, sans effet.

**Pourquoi.** C'est du code mort de fait (§1, jamais persisté), sans carte, qui réécrit encore le fichier à chaque édition de la page Recording. Le supprimer lève une ambiguïté avant d'en construire quoi que ce soit dessus.

**Taille.** Petite en difficulté, étendue en surface : une vingtaine de fichiers, tous en suppression. Un test de calcul de calibration disparaît avec sa classe. Compile-only, puis `Deckle.Audio.Tests` et `Deckle.Transcription.Tests`. À faire avant P5, les deux touchent `MonolithicPipeline.cs`.

**Alternative.** Corriger la persistance (propriétés au lieu de champs) et garder l'auto-calibration. Je ne le recommande pas : elle calibre l'affichage seul, jamais le seuil de coupe, et P2 lui retire sa raison d'être. La tâche « Seuil adaptatif » reste ouverte pour plus tard, sur le niveau de voix cette fois.

### P2. Un seul signal de voix dans Deckle.Audio, lu par tous

**Quoi.** Un détecteur de voix par énergie dans `Deckle.Audio` : un petit objet à état, nourri d'un RMS par trame de 50 ms, qui répond « voisée » ou « silencieuse », avec un seul réglage, le **niveau de voix** en dBFS (l'actuel `ThresholdDbfs`, −45 par défaut), et une hystérésis dérivée, jamais réglable. Trois consommateurs le lisent au lieu de recalculer :

- le segmenteur, qui consomme la décision au lieu de comparer lui-même (`EnergySegmenter.Push`) et garde sa machine à états, son hangover, sa marge ;
- l'avertissement micro faible, qui remplace sa constante −45 par le niveau de voix et garde ses 200 ms tenus et ses 5 s ; son message change de sens et le dit : non plus « micro faible » mais « aucune voix au-dessus du niveau de voix depuis 5 s », ce qui est vrai et utile aussi quand c'est le réglage qui est trop haut ;
- le contour du HUD, dont la fenêtre s'ancre sur le niveau de voix au lieu de −55/−32 (le détail en P6).

**La tuyauterie, corrigée par le relecteur.** Pas un booléen sur `CaptureFrame` : le HUD et l'aperçu du Playground écoutent exprès l'événement `AudioLevel`, un simple float, et laissent `Frame` désabonné pour s'épargner la conversion (`IMicrophoneCapture.cs:8-9`, `WaveInLoop.cs:449-469`, `MicrophonePreviewFeed.cs`), et des trames sont fabriquées hors capture en quatre endroits (fichier, récupération, `UtteranceProgressMeter`, tests). La forme qui tient : **un détecteur par consommateur**, déterministe, chacun nourri de la même suite de RMS et donnant donc les mêmes décisions. Le segmenteur en construit un dans `SegmentedTranscriptionSession` (le seul endroit où le segmenteur naît, donc dictée, fichier et récupération couverts d'un coup), `WaveInLoop` un pour l'avertissement (le niveau lui arrive par `IAudioRecordingHost`, une méthode de plus sur les hôtes et leurs doublures de test), le HUD et l'aperçu un chacun. `CaptureFrame` et `AudioLevel` ne changent pas. Sens des dépendances établi : Transcription référence Audio, jamais l'inverse.

**Le sens de l'hystérésis, à trancher.** Une hystérésis a deux niveaux ; le réglage de l'utilisateur n'en fixe qu'un. Deux ancrages possibles :

- *Le réglage est le niveau d'entrée* (mon premier brouillon : sortie à seuil − 6 dB). Objection du relecteur, confirmée : la valeur de Louis (−38) a été réglée contre le comportement actuel, où toute trame sous −38 est silence. Avec une sortie à −44, un bruit de pièce entre −44 et −38 garderait l'utterance ouverte une fois la voix partie ; comme le segmenteur n'a aucun plafond de durée, la jauge ne partirait jamais et la prise ne se couperait qu'au Stop.
- *Le réglage est le niveau de sortie*, l'entrée est dérivée au-dessus (seuil + 3 dB, interprétation). La coupe garde exactement son comportement d'aujourd'hui ; ce qui change, c'est qu'une trame qui retombe un peu sous l'entrée sans passer sous la sortie reste voisée (le but), et qu'il faut parler un peu plus fort pour ouvrir. **Je recommande cet ancrage**, avec une bande étroite, parce qu'il ne défait pas le réglage que Louis a trouvé à l'oreille.

La largeur de la bande (3 dB) et son effet sur une voix douce ne se devinent pas : première étape, rejouer le corpus audio à travers le détecteur avec plusieurs bandes et compter les trames qui changent de camp et les utterances qui se coupent autrement. Si le corpus est vide, l'activer et dicter une semaine avant de figer.

**Où vit le réglage.** Il quitte la page Streaming pour la page **Recording**, dans le module Audio, parce que c'est une propriété du signal que Deckle écoute et que le HUD, l'avertissement et toutes les coupes le lisent. Honnêteté due au relecteur : il gouverne aussi la coupe des fichiers transcrits et des prises récupérées, dont la chaîne de gain n'est pas celle du micro. C'est déjà le cas aujourd'hui ; le déplacement ne le change pas, il le rend visible. Si Louis veut un niveau propre aux fichiers un jour, c'est une décision à part.

**Migration, corrigée.** Ce n'est pas la migration du cap d'enregistrement (interne à un fichier) : c'est un déplacement entre `modules/transcription/settings.json` et `modules/audio/settings.json`. Le précédent existe, `SettingsBootstrap.MoveModuleSetting`, qui tourne avant qu'un service n'ouvre son fichier et a déjà déplacé `applicationLogToDisk` de telemetry vers logging (`SettingsBootstrap.cs:44,169-202`). Il ne traite qu'une clé de premier niveau ; la source est `streaming.segmenter.thresholdDbfs`, il faut une variante à chemin. Il n'écrase jamais une cible déjà écrite et retire la source sans retour : une version antérieure relancée repartirait du défaut −45.

**Pourquoi.** C'est la demande de Louis prise au mot : une seule valeur dit « il y a de la voix », et tout ce qui réagit à la voix la lit. Le HUD devient un instrument du segmenteur, ce qui règle le constat de « Parole douce » et donne à l'utilisateur le moyen de régler le niveau à l'œil.

**Dépend de.** P1 (la fenêtre du HUD disparaît) ; P3 en découle.

**Taille.** Moyenne. Un type dans Deckle.Audio et son test (entrée depuis le silence, sortie depuis la parole, bande) ; `EnergySegmenter` et `SegmentedTranscriptionSession` ; `WaveInLoop` et `IAudioRecordingHost` avec ses implémentations ; le HUD et l'aperçu ; la variante de `MoveModuleSetting` et son test ; le réglage déplacé avec sa carte, ses chaînes `.resw` (Transcription → Audio), `SettingsSearch`, le view-model, la ligne de journal `SegmenterSettingsSnapshot`, la doc. Les tests actuels du segmenteur ne nourrissent que 0,1 et 0,0 de RMS, ils passent inchangés : la bande n'a aucune couverture et en veut une.

### P3. L'hystérésis, aussi pour la jauge

**Quoi.** Avec P2, la fraction `IsPausing` ne repart plus à chaque creux, parce qu'une trame qui retombe dans la bande reste voisée. Rien de plus à coder que P2 : c'est la tâche « Hystérésis du segmenteur » fermée par le même geste. Je la cite à part pour que Louis sache qu'elle tombe, et parce que `IsPausing` et `SilenceElapsedMs` changent un peu de sens (la pause ne commence qu'à la sortie de la bande).

**Alternative, si P2 est refusé.** Ajouter l'hystérésis dans `EnergySegmenter` seul, avec le même ancrage sur la sortie. Petite, mais ça laisse le HUD incohérent.

**Mise à jour du 2026-11-06 (soir), après relecture du code livré.** Le relecteur a établi que la bande gouvernait aussi la reprise : une trame sous le niveau fait passer le segmenteur en hangover et le détecteur en silence, et de là il fallait de nouveau l'entrée (+3 dB) pour reprendre. Les trames entre le niveau et l'entrée, qui reprenaient la parole avant, comptaient comme pause et pouvaient couper l'utterance : la coupe ne restait pas « exactement » celle d'avant. Décision : la marge vaut 0 par défaut, ce qui rend le comportement livré identique à celui d'avant la branche ; une marge positive ne gouvernerait que l'ouverture d'une utterance, la reprise après une pause se faisant au niveau (`VoiceDetector.Resume`). P3 n'est donc pas livré par P2 : l'hystérésis de la jauge demande un autre mécanisme, en temps et non en niveau, à mesurer sur le corpus.

### P4. Un seul « minimum de parole »

**Quoi.** Fondre « Minimum utterance (s) » (segmenteur) et « Minimum speech (s) » (Silero) en un réglage, **Minimum de parole**, 0,25 s par défaut, lu par les deux. Vendu pour ce qu'il est : un seul plancher, deux portées (le segmenteur sur l'utterance entière, Silero sur chaque plage de parole dedans). Le segmenteur reste le pré-filtre bon marché, Silero le juge fin.

**Pourquoi.** Deux cartes pour un plancher, même défaut, et aujourd'hui baisser l'une sans l'autre ne sert à rien en silence. C'est le doublon le plus visible de la page.

**Où.** Dans la section Segmentation, pas dans le groupe Silero : le plancher du segmenteur est le seul garde-fou quand Silero est coupé ou que son modèle manque (`StreamingPipeline.cs:303-305`), il doit rester visible quand le groupe est fermé.

**Taille.** Petite. Un réglage supprimé avec migration (si les deux valeurs stockées diffèrent, garder celle de Silero, le juge final), une carte retirée, le segmenteur lit la valeur partagée. Dépendants à ajuster : `SegmentedTranscriptionSessionTests.cs:88`, `LiveTranscriptionPipelineTests.cs:736` (un 250 codé en dur avec six trames voisées), les copies du view-model, `SettingsSearch`. Avant de toucher le **défaut**, relire `PhantomUtteranceBugNotes.md:48-50` : le plancher est une des portes qui nourrissent le filtre fantôme, le monter change ce qu'il voit.

**Ce que je laisse.** La marge du segmenteur (150 ms) et le padding Silero (30 ms) touchent le même bord de fin d'utterance, l'un après l'autre. Ce ne sont pas des doublons : la marge dit ce que Silero reçoit, le padding ce que Whisper reçoit. Je les garde, en réécrivant leurs descriptions pour dire l'étage. L'alignement strict/inclusif des deux comparaisons est sans objet en pratique : 250 ms ne tombent jamais sur une frontière de fenêtre Silero (4000 échantillons n'est pas un multiple de 512).

### P5. Les mêmes détecteurs sur tous les chemins

**Quoi.** Deux lectures de la même incohérence. Après relecture, je recommande la seconde d'abord, la première ensuite, et seulement sur mesure.

**P5b, à faire d'abord. Garder le monolithique, mais rendre les détecteurs cohérents.** Sortir le groupe « Speech filtering » et la section Segmentation de la condition de visibilité du streaming, puisque la transcription de fichier et la récupération les lisent déjà ; faire tourner Silero sur la prise monolithique avant l'appel Whisper (il enlève les silences que Whisper hallucine). Petite, aucun comportement perdu.

**P5a, la décision de produit. Retirer la stratégie monolithique.** Le streaming devient le seul pipeline de dictée, la bascule « Transcribe as you speak » disparaît, la page Streaming devient la page **Segmentation**. Un seul chemin à comprendre, tester et journaliser.

**Pourquoi la retenue.** Mon premier brouillon s'appuyait sur « Louis dicte en streaming, sa valeur à −38 en témoigne » ; le relecteur a raison, c'est circulaire : le seuil gouverne aussi les fichiers, et la note d'octobre dit que la stratégie de Louis « cannot be established ». Et le retrait fait perdre des choses que le monolithique fait et que le streaming ne fait pas :

- un mot seul très court (« oui » sous 250 ms de voix) est jeté par le plancher du segmenteur, puis par Silero ; le monolithique l'aurait transcrit ;
- le gain du DSP se calcule sur toute la prise, au lieu d'utterance par utterance où il varie ;
- Whisper garde son propre contexte sur 30 s au lieu des queues passées à la main ;
- un nouvel utilisateur sans modèle Silero installé aurait un streaming sans VAD, moins protégé que ce que la table du §2 laisse croire.

**Ce que ça touche vraiment (P5a).** `MonolithicPipeline.cs`, `ConsumeMonolithicAudioAsync`, `StateMachine.cs:430-435`, `PipelineStrategyKind` et `Strategy`, le view-model et la bascule, `TakeStartCheckTests.cs:187-195` et sa note de bug, les doublures `StreamingHost`, la phase de télémétrie `at_stop` et le sens de `stop_to_pipeline_ms`, dix-sept mentions dans `docs/transcription-audio-chain.md`, `TREE.md`, l'inventaire des exposables, le commentaire de `TranscriptionPreprocessor.cs:12`, les chaînes de la page. La clé `"strategy"` d'un fichier existant est simplement ignorée au chargement : la migration vers Streaming est automatique, pas un pas à écrire. C'est un chantier à part entière, pas une étape de celui-ci.

**Condition.** P5a se décide quand une mesure dit que le streaming tient une dictée de vingt minutes avec la fidélité du monolithique et que les mots courts ne manquent pas. Non vérifié aujourd'hui ; le corpus audio et `latency.jsonl` sont les deux sources qui le diront.

### P6. La page Recording devient la page du micro, avec son instrument

**Quoi.** Après P2, la page Recording porte : le micro, le niveau de voix avec un **instrument à côté** (un vumètre qui suit le micro quelques secondes et marque le niveau, construit sur `MicrophonePreviewFeed` que le Playground a déjà), la durée maximale, le prétraitement et ses deux contrôles. Le contrôle micro actuel (« −38 dBFS → −20 dBFS ») apprend à dire où la voix se place par rapport au niveau de voix, en plus de la cible du DSP. La page Segmentation du module Transcription ne garde que ce qui dépend de la coupe : la courbe de pause et ses quatre ancres, la marge, le minimum de parole, le groupe Silero (confiance, minimum de silence, padding).

**Le contour du HUD, précisé.** Le mapper n'est pas une porte, c'est une courbe continue lissée (`AudioLevelMapper.cs:59-71`), et il doit le rester : un contour qui s'allume d'un coup clignoterait. Ce qui change, c'est l'ancrage de sa fenêtre : éteint au niveau de sortie du détecteur, déjà nettement allumé au niveau d'entrée (interprétation : la courbe réglée pour qu'une voix juste au-dessus du niveau donne près de la moitié de l'intensité, pas un dixième, sinon une voix douce qui passe donnerait un anneau presque noir, l'inverse de ce que « Parole douce » demande), plein à entrée + 20 dB. Le contour s'allume aussi sur une bribe que le segmenteur jettera ensuite ; c'est acceptable, le son était là. En monolithique, aucune utterance ne s'ouvre, le contour reste un simple niveau.

Vocabulaire fixé : **niveau de voix** pour le dBFS, **confiance du détecteur** pour la probabilité, **pause** et **minimum de parole** pour les durées. Le mot « seuil » disparaît des libellés.

**Pourquoi.** La doctrine `deckle-settings-ux` demande qu'une grandeur qui dépend du micro ait un instrument à côté ; c'est la raison même pour laquelle les curseurs du HUD ont été retirés en octobre, et le champ « Speech level (dBFS) » est resté seul sans instrument. C'est aussi la tâche « Clarifier les réglages d'enregistrement » (seuils nommés, distinguer niveau et confiance).

**Dépend de.** P2. **Taille.** Moyenne, surtout de l'interface : un bloc d'instrument nouveau dans le composer, ou réutilisé du Playground, à discuter avant d'écrire (la doctrine dit qu'un besoin sans bloc se discute).

### P7. Hygiène, sans décision à prendre

- `WhisperViewModel` recopie à la main les défauts de Silero et du segmenteur (`WhisperViewModel.cs:29-53`) alors que la courbe lit déjà les initialiseurs du POCO ; aligner.
- `docs/transcription-audio-chain.md` place la bascule streaming et les cartes de confiance sur la page Dictation ; elles sont sur Streaming et Decoding. Elle ignore le filtre fantôme, le pad de bruit et la récupération audio.
- `src/Deckle.Transcription/GLOSSARY.md` parle encore d'un « safety ceiling » et d'un VAD sur « a whole captured buffer » ; il n'y a pas de plafond, et Silero travaille par utterance.
- `AudioLevelMapper.cs:13-23` cite des curseurs qui n'existent plus ; `RecordingViewModel.Settings.cs:55-56` dit que le contrôle micro « mesure la fenêtre », ce qu'il ne fait pas ; après P1, les commentaires de `CaptureSettings.cs:26-47` et `RecordingViewModel.cs:14-15` tombent aussi.
- `docs/inventaire-exposables.md:112` cite la constante de l'avertissement à `WaveInLoop.cs:102` ; elle est ligne 96.
- Le préréglage de coupe (`CutPreset`, `CutCustomValues`) est stocké et dérivé sans qu'aucune page le sélectionne depuis le 2026-10-14 ; à trancher un jour : le remettre ou le retirer.

## 4. L'ordre que je propose

1. **P1 + P7** : du nettoyage sûr, chacun un commit, aucune décision de produit.
2. **Mesurer** : vérifier que `microphone.jsonl` et le corpus audio sont actifs, et sinon les activer et dicter quelques jours. Tout ce qui suit s'appuie dessus.
3. **P2 (+P3)** : le cœur, dans un worktree dédié, la bande d'hystérésis fixée par la mesure de l'étape 2.
4. **P4 et P5b** : petits, dans le même worktree ou juste après.
5. **P6** : l'interface, une fois le signal unique en place.
6. **P5a** : la décision de produit, à part, quand la mesure dit que le streaming tient seul.

## 5. Ce que je n'ai pas vérifié

- La largeur de la bande d'hystérésis (3 dB) et la forme de la fenêtre du HUD sont des interprétations à mesurer, pas des faits.
- Si −38 ou −45 dBFS sépare bien la voix de Louis du silence de sa pièce, et si les deux consentements de mesure sont actifs chez lui.
- La tenue du streaming sur une longue dictée et sur les mots courts (condition de P5a).
- Comment `Load()` du view-model Recording est ordonné par rapport à la page mise en cache : l'agent UX soupçonne une réécriture de valeurs périmées ; P1 fait disparaître le cas.
