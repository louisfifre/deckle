# Refonte des diagnostics — proposition

Pour Louis. Branche `refs/heads/diagnostics-rework-4dbe26`, rien de fusionné ni de poussé. Captures avant/après sous `docs/proposals/captures/` (ignorées par git, présentes dans le worktree).

## Ce qui ne va pas aujourd'hui

Établi par les captures et par quatre lecteurs (deux « froids », sans les doctrines ; deux « cadrés », avec elles), qui convergent :

- **Le niveau change de sens d'une carte à l'autre.** « Summary » veut dire un battement toutes les 5 s (éclairage), toutes les 1 s (dictée), toutes les 30 s (autocorrect), ou un compte de trames (Input). Windowing n'offre que Off/Details. Chaque carte doit donc s'expliquer, et on ne peut pas prévoir le volume.
- **« Telemetry » mélange deux natures.** Les corpus (dictées, texte tapé) sont des données personnelles gardées volontairement ; le reste (niveau du micro, timings, molette, trames du touchpad) est du diagnostic de développeur. Les deux toggles qui gardent du texte tapé (« Autocorrect decisions », « Keep the text I type ») sont séparés et indiscernables pour un lecteur.
- **Le coût disque est introuvable.** La page parle de lignes et d'archives, jamais d'octets.
- **Trop de niveaux de texte** : titre, description, titre de section, légende, titre de carte, description longue.
- **Fenêtre de log** : trois formes de bouton dans une barre ; Filters, Word wrap et Auto-scroll ~15 px plus bas que Copy/Save/Clear ; une icône puzzle (Playground) sans lien avec le log ; un vide sous la dernière ligne.

Mesuré sur ta machine : une ligne de trace pèse ~720 octets. Le trace garde aujourd'hui **10 archives de 8 000 lignes** (≈ 57 Mo), pas 20 × 5 000 comme tu pensais : chez toi, ça couvre **environ une journée** (la dernière rotation en est à la 252e archive). Le log toujours actif (`log.jsonl`) est un autre fichier, plus petit, qui ne garde que jalons, avertissements et erreurs.

## Avant / après

**Avant** — Log verbosity (5 listes Off/Summary/Details) · Trace (1 toggle) · Telemetry (8 cartes à plat) · Folders (2 dossiers).

**Après**

1. **Logs**
   - *Detailed logs* — un interrupteur, éteint par défaut. Ouvert, il montre une ligne par source, chacune son interrupteur, dans cet ordre (ce qu'on suit, puis ce qui se mesure, puis le très gros volume) : Ambient lighting, Dictation, Dictation timings, Microphone level, Language model calls, Autocorrect, Touchpad gestures, Windows, Touchpad frames, Wheel events. Les deux dernières disent leur volume (une ligne par trame) et sont éteintes par défaut ; *Microphone level* garde son dialogue de consentement ; les autres sont allumées, donc allumer *Detailed logs* donne « tout, sauf le très gros volume et le micro ».
   - *Keep logs on disk* — le trace, avec dedans *Keep up to* : About 60 MB · About 300 MB · About 1 GB · No limit.
2. **Corpus**
   - *Keep my dictations* → dedans *Also save the audio* (et son choix brut/traité).
   - *Keep the text I type* → dedans *Also keep each correction decision*.
3. **Folders** — *Dictation audio folder*, *Diagnostics folder*.

## Décisions et leurs raisons

1. **Un seul modèle de niveau : « normal » ou « détaillé ».** Le résumé (battement, cumul, chiffres d'une prise) passe **toujours**, comme les jalons et les erreurs : il coûte une ligne par période et c'est lui qui dit qu'une activité tourne. Le détail s'ajoute par-dessus, source par source. Plus de « Off » ni de « Summary » à expliquer. Pourquoi ce n'est pas une perte : masquer les battements reste possible dans la fenêtre de log par le filtre Category › Heartbeat, sans les détruire. *Interprétation* : c'est ce que tu décrivais pour l'éclairage (« le détail s'ajoute, et éteint il y a quand même un battement »).
2. **Un interrupteur général avec des sous-interrupteurs**, plutôt qu'un réglage par module à plat : le cas courant (« donne-moi tout pour ce bug ») est un geste ; l'affinage reste là, une profondeur plus bas, hors de la vue d'un utilisateur ordinaire.
3. **Les diagnostics de développeur rejoignent les logs.** Niveau du micro, timings de dictée, molette et trames du touchpad deviennent des sous-interrupteurs de *Detailed logs* : l'interrupteur général les coupe tous. Les deux « enregistrements de trames » (molette, touchpad) sont côte à côte, avec le même avertissement de volume.
4. **Les fichiers de ces jeux restent où ils sont** (`telemetry\latency.jsonl`, `microphone.jsonl`, `mouse-wheel`, `trackpad`). Une série trame par trame dans une liste à l'écran serait illisible et ferait tomber la fenêtre ; les scripts d'analyse lisent ces fichiers. Seul l'endroit du réglage change.
5. **Corpus = ce que tu dis ou tapes, gardé volontairement.** Deux plis de même forme : la donnée principale, puis l'option « en plus ». *Also keep each correction decision* devient l'option de *Keep the text I type* : les décisions contiennent les mots tapés, donc elles ne peuvent exister sans ce consentement-là. Elles ne peuvent pas aller dans les logs : la doctrine de log interdit d'y mettre ce que la personne tape.
6. **Tokens des appels LLM.** Les trois moteurs (Ollama, OpenAI-compatible, Claude) lisent déjà les compteurs que la réponse porte (`prompt_eval_count`/`eval_count`, `usage.prompt_tokens`/`completion_tokens`, `usage.input_tokens`/`output_tokens` + cache) et les écrivaient déjà, toujours, dans une ligne Verbose sans le nom du fournisseur. Cette ligne passe sous *Language model calls* (éteinte par défaut, comme tu le demandes), commence par profil et modèle, et porte les tokens avant les durées. Aucun prix n'est inventé.
7. **Rétention exprimée en taille, pas en lignes.** Ce qui compte pour toi, c'est le disque. Les tailles sont « About » : la rotation reste en lignes, la taille d'une ligne varie. Coût : 60 Mo ≈ une journée chez toi, 300 Mo ≈ une semaine, 1 Go ≈ trois semaines, « No limit » ≈ 60 Mo par jour d'usage comme le tien, sans fin.
8. **Moins de texte.** Plus de légende sous les titres de section ; une description de carte seulement quand l'effet n'est pas évident ; jamais de nom de fichier dans une description (le dossier est en bas).
9. **Fenêtre de log.**
   - Une seule barre, une seule forme : Copy all ▾, Save all ▾, Clear à gauche ; Word wrap, Auto-scroll à droite, mêmes boutons, même hauteur, même ligne.
   - Les filtres (Severity, Module, Category) sont toujours visibles, à gauche, juste au-dessus des lignes qu'ils filtrent : plus de bouton Filters à ouvrir.
   - Le bouton Settings quitte la barre de titre et devient « Log settings » dans la barre d'outils, avec son libellé ; le bouton Playground quitte la fenêtre de log (il reste dans Settings et le mode développeur).
   - Le vide en bas venait d'un pied de liste de cinq lignes blanches ajouté exprès ; il est réduit à une marge.
10. **Contenu du log.**
    - « A dictation batch came back rewritten » doublait « Transcript rewrite complete » au même instant : seule la seconde reste (elle porte le fournisseur, la durée et l'identifiant qui l'apparie à son début).
    - Les lignes « Hot loop » (une par boucle, six à la même milliseconde chaque minute) deviennent **une** ligne par minute, nommée « Loop counts », `window_s=60 | whisp_streaming_heartbeat=30 | …` : la fenêtre d'abord (ce qui se répète), puis les comptes.

11. **Détails de construction à connaître.**
    - Le pli *Detailed logs* appartient à la page Diagnostics (le composeur lit les libellés dans l'assemblage du modèle qui les possède) : d'où une nouvelle référence `Deckle.Diagnostics.Logging.Views` → `Deckle.Input.Trackpad`. Les pages Recording et Trackpad perdent leurs cartes « telemetry ».
    - *Dictation timings* est maintenant allumé par défaut, mais n'écrit rien tant que *Detailed logs* est éteint.
    - Les décisions d'autocorrect exigent les deux consentements (texte tapé **et** décisions) ; le code le vérifie aussi, pas seulement l'interface.

## Trois demandes ajoutées en cours de route

### 1. Un geste pour un utilisateur ordinaire, et le partage

**Construit** (`069b4934`) : *Detailed logs* est ce geste unique. Sous l'interrupteur, une personne qui n'est pas développeur voit **six** sources, toutes allumées : Ambient lighting, Dictation, Language model calls, Autocorrect, Touchpad gestures, Windows. Ce sont des lignes de log, celles qu'on partage. Les quatre captures de développeur (Dictation timings, Microphone level, Touchpad frames, Wheel events) sont des jeux de données pour les scripts d'analyse : elles ne s'affichent **et n'enregistrent** qu'en mode développeur (Settings > General). Ainsi, la liste que voit la personne est tout ce que l'interrupteur allume ; rien de caché n'enregistre dessous. Le self-check photographie la page des deux façons : `docs/proposals/captures/after-dev/captures/settings-diagnostics-open.png` (développeur, 10 sources) et `settings-diagnostics-open-user.png` (ordinaire, 6 sources).

**Proposé, pas construit : le partage, sans aucun envoi automatique.**
- Un bouton **« Save a diagnostics report… »** dans la section Logs. Il ouvre le sélecteur de fichier de Windows et écrit **un zip** là où la personne le choisit. Elle l'envoie elle-même (courriel, issue GitHub). Deckle n'envoie rien, et aucune télémétrie réseau n'est ajoutée.
- Le zip contient : `log.jsonl` et ses archives, le trace s'il est gardé, les rapports de plantage récents, la version de Deckle et de Windows, et les réglages **sans leurs valeurs personnelles** (les chemins et les noms d'appareil sont déjà caviardés par `SettingValueRedaction`).
- Il **ne contient jamais** le corpus (dictées, audio, texte tapé), ni les jeux de développeur. Avant d'écrire, une ligne dit sa taille et ce qu'il contient.
- Le parcours : *allumer Detailed logs → reproduire le problème → Save a diagnostics report*. La description de *Detailed logs* le dit déjà en partie (« Turn it on before the problem comes back »).
- Une tâche Anytype existe déjà pour ça : « Diagnostics : exporter un zip et vider les dossiers ». Je ne l'ai pas construite ici pour garder la branche relisible.

### 2. Savoir quels réglages sont utilisés

**Établi : c'est journalisé, mais en partie seulement.**
- L'événement existe : `Deckle-SettingsUx` › `SettingChanged`, message `setting=… | value=…` (`src/Deckle.Diagnostics/DeckleSettingsUxSource.cs`). La valeur passe par une liste blanche ; un réglage inconnu s'écrit `(changed)`.
- **Où le lire** : dans la fenêtre de log, filtre Module › « Settings UX ». Sur disque, seulement dans le trace (`trace.jsonl` et `archive/`), donc seulement si *Keep logs on disk* est allumé : il est en Verbose, et `log.jsonl` ne garde que jalons, avertissements et erreurs.
- **Ce qui manque** :
  1. Seuls les réglages écrits à la main l'émettent (General, Hotkeys, Recording, Diagnostics, Animations), une soixantaine d'appels en tout. Les réglages composés par `SettingsComposer` (la plupart des pages de module) ne l'émettent pas.
  2. L'ancienne valeur n'y est pas.
  3. Par défaut, rien n'atteint le disque.

**Proposé, le plus petit point de log** : un seul endroit, dans `SettingsComposer`. Chaque descripteur reçoit un setter enveloppé qui lit l'ancienne valeur, écrit, puis émet `SettingChanged(setting, old_value, new_value)` si la valeur a changé.
- `setting` = module + `LabelKey`, déjà unique et stable.
- La valeur n'est écrite que pour les natures sûres : interrupteur, choix d'une énumération, nombre, raccourci clavier. Un chemin, un texte libre ou un éditeur s'écrit `(changed)`, jamais la valeur.
- Il couvre d'un coup toutes les pages composées. Il demande une référence `Deckle.Catalog` → `Deckle.Diagnostics`, et un nouvel identifiant d'événement (l'ancien garde sa forme).
- **Reste ta décision** : pour qu'un changement atteigne le disque sans le trace, il faut l'admettre dans `log.jsonl`. C'est un geste humain rare, donc ça ne coûte presque rien en volume, mais ça change ce que garde le log toujours actif.
- *Autre lecture, sans rien construire* : comparer chaque fichier de réglages à ses valeurs par défaut dit **ce qui est réglé aujourd'hui**. Le log dit **ce qui a été touché, et quand**.

### 3. La même forme pour les deux corpus

**Établi : c'est déjà la forme livrée.** *Keep my dictations* contient *Also save the audio* (et le choix brut/traité) ; *Keep the text I type* (le corpus de l'autocorrect) contient *Also keep each correction decision*. Même pli, même verbe « Also ». *Interprétation* : si tu voulais plutôt le texte tapé comme sous-option d'un pli « Autocorrect », dis-le ; la forme actuelle met la donnée principale en titre, comme pour la dictée.

**Établi : les deux corpus sont stockés en clair.**
- Dictées : JSONL texte sous `telemetry\corpus\…\corpus.jsonl` ; l'audio en fichiers WAV.
- Texte tapé : `telemetry\autocorrect.text.jsonl`, `autocorrect.stream.jsonl` et, si l'option est allumée, `autocorrect.decisions.jsonl`.
- Aucun chiffrement. À noter pour plus tard : les magasins de *récupération* de dictée (`DictationRecoveryStore`, `TakeAudioRecoveryStore`) protègent déjà leurs fichiers, et `Deckle.Security` a un coffre (`SecretVault`). Le chiffrement du corpus reste hors périmètre (tâche Anytype Sécurité).

## Après la seconde revue

Captures prises à nouveau, relues par les mêmes types de lecteurs sans leur donner mes constats. Établi : la fenêtre de log a une barre d'une seule forme et ses filtres au-dessus des lignes ; la page a trois sections ; le coût disque est lisible. Ils ont relevé que *Dictation* et *Dictation timings* se confondaient, que « About 60 MB » se lisait « par fichier », et que la carte du dossier audio était la seule sans texte : corrigé (`d091fcea`), chaque source dit ce qu'elle **ajoute**, la rétention dit qu'elle couvre tout l'historique, *Detailed logs* dit de l'allumer avant que le problème revienne.

## Ce qui reste faible

- **Dix sous-interrupteurs dans un pli en mode développeur** (six sinon) : long, et les plis ne s'emboîtent pas.
- **Le mot « Corpus »** reste opaque pour un lecteur froid ; la légende l'explique, le titre non.
- **La taille actuelle sur disque et une commande pour vider** manquent ; c'est la tâche Anytype existante « Diagnostics : exporter un zip et vider les dossiers ».
- **Fenêtre de log** : aucun signe qu'un filtre est actif ni combien de lignes il cache ; dans la capture « fin », la dernière ligne semble passer sous la barre de défilement horizontale (*incertain* : des lignes arrivaient peut-être après le défilement).
- **« Log settings »** ouvre la page nommée « Diagnostics » : deux noms pour un lieu.
- **Fréquence d'image de la fenêtre de log** : pas mesurée. Je ne peux pas faire défiler au trackpad depuis le self-check, et je ne l'affirme donc pas (tâche Anytype).
- **Réordonner les champs de tous les événements** du général au particulier : fait sur ceux que j'ai touchés seulement (tâche Anytype).
- **`GLOSSARY.md`** de Diagnostics décrit encore Off/Summary/Details : c'est un élément de contexte, je ne le réécris qu'avec ton accord.

## Captures

- Avant : `docs/proposals/captures/before/captures/`, fenêtre de log défilée : `docs/proposals/captures/before-log/captures/log-end.png`.
- Après : `docs/proposals/captures/after/captures/`.
- Après correction : `docs/proposals/captures/after-fix/captures/` (dont `settings-diagnostics-open.png`, tous les plis ouverts).

## Questions pour toi

1. **Le résumé toujours admis** te convient-il ? Ça retire la possibilité d'éteindre les battements à la source (ils restent masquables dans la fenêtre). Sinon, je rends un « Off » mais il faudra le nommer.
2. **Décisions d'autocorrect sous « Keep the text I type »** : aujourd'hui elles sont indépendantes. Les lier interdit de garder les décisions sans le texte. D'accord ?
3. **Niveau du micro** : son ancien dialogue de consentement reste (il mesure ton micro, même sans voix), il est donc éteint par défaut même quand *Detailed logs* est allumé. Le garder, ou le traiter comme les autres diagnostics ?
4. **Playground** retiré de la fenêtre de log : il te manquera ?
5. **Copy all** : deux lecteurs voudraient que le bouton principal copie ce qui est filtré quand un filtre est actif. Je ne l'ai pas changé ; le veux-tu ?
6. **Recherche dans la barre de titre** : un lecteur la voudrait à côté des filtres. Je l'ai laissée (modèle Explorer/Settings). Ton avis ?
7. **Anciens réglages de verbosité** : tes choix Summary/Details actuels ne sont pas repris ; tout repart sur « normal ». Acceptable pour l'alpha ?
8. **Rétention en paliers** (60 Mo / 300 Mo / 1 Go / sans limite) plutôt qu'un nombre libre : ça te suffit ?
9. **`GLOSSARY.md`** : je le mets à jour au nouveau modèle (normal / détaillé) ?
10. **Rapport de diagnostic en zip** : le contenu proposé (logs, trace, plantages, versions, réglages caviardés ; jamais le corpus) te va-t-il ? Je le construis ici ou dans sa tâche Anytype ?
11. **Changements de réglages** : je pose le point de log unique dans `SettingsComposer` ? Et l'admets-tu dans `log.jsonl`, toujours actif, pour que l'usage se lise sans le trace ?
12. **Dictation timings** était allumé par défaut sous *Detailed logs* ; il est maintenant réservé au mode développeur. D'accord ?
