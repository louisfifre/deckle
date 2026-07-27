# JOURNAL — Deckle.Travel

## 2026-07-27 — Les deux faits API marqués « à vérifier » sont tranchés

- **Templates : lecture et application seulement.** L'API expose
  `GET .../types/:type_id/templates` (+ get unitaire) et le champ optionnel
  `template_id` de `POST .../objects`, qui applique un template existant à la
  création. Aucun endpoint de création de template n'existe dans la référence
  (`2025-11-08`). Provisionner un template = le fabriquer dans l'app, puis
  lire son id via list-templates.
- **Fichiers : l'upload existe, non câblé chez nous.** `POST
  /v1/spaces/:space_id/files` (multipart), plus GET/DELETE unitaires — ajout
  rétrocompatible du 2026-05-13 (anytype-heart v0.50.5) sous l'ombrelle
  `2025-11-08` déjà pinnée par `AnytypeApiClient`. Chemin d'attache : upload →
  id d'objet-fichier → propriété de format `files` sur l'objet cible. Rien de
  ce chemin n'est implémenté dans `Deckle.Anytype` ; le schéma Travel déclare
  déjà ses propriétés `files` (billets, GPX), la clé de payload `files` à la
  création d'objet reste à vérifier en vif.
- **L'espace s'appelle « Travel » côté bot**, pas « Vacances » ; id gravé
  dans le spaces.json runtime sous l'alias `travel`.

## 2026-07-27 — Cadrage clos (grilling, deux sessions)

- **Modèle arrêté à sept types** : Séjour, Étape, Lieu, Activité, Déplacement,
  Hébergement, Dépense. L'Activité est l'objet central de l'espace.
- **Pivot budgétaire en trois temps.** La Réservation est d'abord devenue
  Dépense (centre de gravité budget). Puis le Déplacement et l'Hébergement ont
  été promus types de plein droit — la réservation vit chez eux, avec leurs
  fichiers (billets, confirmations). La Dépense a fini minimale, à hauteur de
  ticket de caisse : montant, date, catégorie, séjour. Les objets riches lient
  leur dépense (objet lié) ; les tickets orphelins restent libres.
- **Les statuts sont morts deux fois.** L'Activité n'a pas d'état : la Date
  fait l'état (absente = vivier, posée = fixée, passée = faite). Le statut
  envisagée/réservée/payée de la Dépense a sauté pour la même raison : une
  dépense saisie est un fait, le montant ne s'écrit que certain.
- **Choisi Date + RDV** (propriété jour, la maîtresse ; propriété datée avec
  heure, optionnelle) parce qu'Anytype n'offre pas de plage début/fin riche.
  Le trek multi-jours ne porte que son jour de départ — assumé.
- **Carnet dissous.** Pas de type carnet : le quotidien au corps de l'Étape,
  le souvenir au corps de l'objet vécu, le savoir durable au corps du Lieu.
- **Règles de saisie** : catégorie de Dépense obligatoire (vocabulaire fermé,
  options ajoutées par Louis dans Anytype, jamais par la surface) ; séjour
  résolu par la date du ticket, sinon exigé explicitement.
- **Pas de grammaire de codes** à la Home : destination + dates identifient
  un séjour, le nom et les liens font le reste.
- **Code anglais, espace français** — choix esthétique assumé (contraste
  app/personnel). Tout libellé visible dans Anytype sort dans un fichier de
  termes par module ; le multilingue est une porte (tâche Anytype « Revue
  multilingue » ; les IDs français gravés par Home y sont signalés).
- **Nommage** : module `Deckle.Travel`, client `travel`, secret
  `mcp-token-travel`, env `DECKLE_MCP_TOKEN_TRAVEL`, alias spaces.json
  `travel` → espace « Vacances », descriptor `deckle-travel`.
- **Tickets de caisse : local exclusivement.** Le futur pipeline (photos du
  soir, OCR) ne passera jamais par un cloud ; le geste dépense du MCP doit
  rester appelable par un client local.
- **À vérifier au chantier** (non testé) : ce que l'API Anytype permet sur
  les templates (création vs application) et sur l'attachement de fichiers.
