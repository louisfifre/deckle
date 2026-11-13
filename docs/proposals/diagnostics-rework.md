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
   - *Detailed logs* — un interrupteur, éteint par défaut. Ouvert, il montre une ligne par source, chacune son interrupteur : Ambient lighting, Dictation, Autocorrect, Touchpad, Windows, LLM calls, Dictation timings, Microphone level, Wheel events, Touchpad frames. Les deux dernières disent « One line per frame: very high volume » et sont éteintes par défaut ; les autres sont allumées, donc allumer *Detailed logs* donne « tout, sauf le très gros volume ».
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
6. **Tokens des appels LLM.** Les trois moteurs (Ollama, OpenAI-compatible, Claude) lisent déjà les compteurs que la réponse porte (`prompt_eval_count`/`eval_count`, `usage.prompt_tokens`/`completion_tokens`, `usage.input_tokens`/`output_tokens` + cache) et les écrivaient déjà, toujours, dans une ligne Verbose sans le nom du fournisseur. Cette ligne passe sous *LLM calls* (éteinte par défaut, comme tu le demandes), commence par profil et modèle, et porte les tokens avant les durées. Aucun prix n'est inventé.
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

## Ce qui n'est pas fait, et pourquoi

- **Fréquence d'image de la fenêtre de log** : pas mesurée. Je ne peux pas faire défiler au trackpad depuis le self-check, et je ne l'affirme donc pas. À mesurer avec toi.
- **Réordonner les champs de tous les événements** du général au particulier : fait sur ceux que j'ai touchés seulement ; un balayage complet des providers est une autre tâche (notée dans Anytype).

## Questions pour toi

1. **Le résumé toujours admis** te convient-il ? Ça retire la possibilité d'éteindre les battements à la source (ils restent masquables dans la fenêtre). Sinon, je rends un « Off » mais il faudra le nommer.
2. **Décisions d'autocorrect sous « Keep the text I type »** : aujourd'hui elles sont indépendantes. Les lier interdit de garder les décisions sans le texte. D'accord ?
3. **Niveau du micro** : son ancien dialogue de consentement reste (il mesure ton micro, même sans voix), il est donc éteint par défaut même quand *Detailed logs* est allumé. Le garder, ou le traiter comme les autres diagnostics ?
4. **Playground** retiré de la fenêtre de log : il te manquera ?
5. **Copy all** : deux lecteurs voudraient que le bouton principal copie ce qui est filtré quand un filtre est actif. Je ne l'ai pas changé ; le veux-tu ?
6. **Recherche dans la barre de titre** : un lecteur la voudrait à côté des filtres. Je l'ai laissée (modèle Explorer/Settings). Ton avis ?
7. **Anciens réglages de verbosité** : tes choix Summary/Details actuels ne sont pas repris ; tout repart sur « normal ». Acceptable pour l'alpha ?
