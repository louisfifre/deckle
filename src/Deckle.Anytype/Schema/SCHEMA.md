# Espace Anytype « Dev » — fiche de schéma

Instantané **vérifié sur l'API live le 2026-06-16**. Pendant lisible de `DevSpace.cs`
(qui, lui, est gelé au 12-13 juin et a divergé — voir § Résidus & dérives).

Fiche **temporaire** : une fois la surface MCP complétée et auto-descriptive, les
commandes porteront elles-mêmes cette information et cette fiche pourra mourir.

## Les six objets de gestion

| Objet | Rôle | Layout |
|---|---|---|
| **Epic** | Regroupe des projets (collection, kanban de projets). Géré à la main par Louis. | collection |
| **Projet** | Centre de gravité : cadrage, état, charge/budget, dépendances, vues filtrées. | page |
| **Tâche** | Unité d'action cochable. Porte le type d'action et le livrable attendu. | action |
| **Rapport** | Mémoire d'une séance de travail sur une tâche. Léger, daté. Gardé même quand la tâche est close : la query Rapport est la mémoire cherchable du travail. | note |
| **Idée** | Note transversale légère, pas rattachée à un projet (sinon c'est une tâche). | basic |
| **Document** | Contenu de référence stable, titré, versionné. | page |

Les autres types de l'espace (Note, Page, Audio, Contact, Entreprise, Rôle, Favori…)
sont natifs Anytype ou relèvent d'autres domaines de vie (Candidatures, Website) —
hors gestion Deckle.

## Propriétés par objet (live)

● = portée par le type aujourd'hui. ¹ = case built-in du layout (présente sur l'objet
même si le type ne l'« assigne » pas).

| Propriété | Format | Epic | Projet | Tâche | Rapport | Idée | Doc |
|---|---|:--:|:--:|:--:|:--:|:--:|:--:|
| État | select | ● | ● | | | | |
| Phase projet | select | | ● | | | | |
| Priorité | select | ● | ● | ● | | | |
| Type de tâche | select | | | ● | | | |
| Type de document | select | | | | | | ● |
| Définition de fini | text | ● | ● | ● | | | |
| Version | text | | ● | | | | ● |
| Date cible | date | | ● | ● | | | |
| Date du journal | date | | | | ● | | |
| Budget estimé (€) | number | ● | ● | | | | |
| Budget réel (€) | number | ● | ● | | | | |
| Charge estimée (Jours) | number | ● | ● | ● | | | |
| Charge réelle (Jours) | number | ● | ● | ● | | | |
| Livrable(s) | multi_select | | | ● | | | |
| Projet(s) lié(s) | objects | | | ● | | | |
| Tâche(s) liée(s) | objects | | | | ● | | |
| Contact(s) lié(s) | objects | | | ● | ● | | |
| Fichier(s) lié(s) | files | | | ● | ● | | |
| Dépend de | objects | | ● | | | | |
| Document système | checkbox | | | | | | ● |
| Terminé (done) | checkbox | | | ●¹ | | | |
| Archivé | checkbox | ● | ● | ¹ | | ● | ● |

**Terminé vs Archivé.** `done` est automatique et propre aux tâches : pour une tâche on
coche Terminé, pas Archivé. `archive` est le mécanisme transversal (presque partout) pour
sortir un objet des vues. Un rapport ne s'archive pas avec sa tâche (impossible côté
Anytype) — c'est voulu, il reste cherchable.

## Vocabulaires (options des selects)

- **État** : Terminé · Ouvert · En cours · Dormant · En attente · Abandonné
- **Priorité** : 0 → 5 (5 = max)
- **Type de tâche** : Produire · Chercher · Organiser · Échanger · Gérer
- **Type de document** : Astuce · Nomenclature · Référence · Spécification · Instructions · Recherche · Architecture
- **Phase projet** : Cadrage *(seule option — embryonnaire)*
- **Livrable(s)** : Texte · Règle de cadrage *(2 placeholders à curer — palette cible en un mot : Décision, Prototype, Refonte, Convention, Mesure, Référence, Fonctionnalité, Correctif…)*

## Pièges de clés (réels — ne jamais « corriger » dans l'espace, ça casserait les objets)

Anytype fige l'**ID d'une propriété sur son titre de création** : une faute de frappe au
départ reste dans l'ID à vie, même après renommage. D'où :

| Libellé affiché | Clé sur le fil | Piège |
|---|---|---|
| Charge réelle (Jours) | `charge_estimee_(jours)` | clé trompeuse — c'est la **réelle**, pas l'estimée |
| Charge estimée (Jours) | `charge_estimee` | l'estimée |
| Budget réel (€) | `budget_reel_(` | clé tronquée |
| Tâche(s) liée(s) | `tache(s)_liee(s)` | clé sans accents |
| Rapport(s) lié(s) | `rpport(s)_lie(s)` | « a » manquant, gelé ; orpheline (assignée à aucun type) |

## Résidus & dérives à corriger

**Côté espace (à la main / futur outillage de gestion) :**
- `tag` (multi_select) — la seule propriété auto-transversale d'Anytype : se remet seule sur
  tous les objets, supprimée plusieurs fois sans succès. Non utilisée. Les 5 types qui la
  portent encore l'entretiennent peut-être.
- `État` sur Idée — résidu, à retirer (l'idée n'a pas besoin d'état).
- `rpport(s)_lie(s)` orpheline — à nettoyer si possible.

**Côté code (`DevSpace.cs`) — resynchronisé le 2026-06-16 :**
- `tag` retiré des 5 tables de type (mappé sur aucun type ; la clé survit, commentée,
  pour documenter le résidu auto-transversal). Conséquence : `update` refuse désormais
  `tag`, et la résolution d'options *live* (`LiveTagResolver`) n'est plus atteinte par
  aucune propriété mappée — elle reste comme infra dormante (cf. curation des Livrables).
- `Charge estimée` / `Charge réelle` ajoutées à la **Tâche** → `update` peut les écrire.
- `État` retiré d'Idée.

## La surface MCP

**Base — 16 outils**, servis à tout consommateur, sur les classes de gestes lecture,
création, écriture propriétés/corps, liens, journal, document et **cycle de vie**. Le cycle de vie
tient en deux verbes précis : `complete` (case `done` d'une tâche, pose/retire) et
`archive` (case `Archivé` transversale, archive/restaure — refusée sur un rapport, qui
reste cherchable). Garde-fous tenus : **aucune création d'option**, corps protégé.

**Gestion — catalogue séparé**, monté à la demande seulement (drapeau de lancement
`--management` ou env `DECKLE_ANYTYPE_MANAGEMENT`) ; un consommateur non supervisé n'en
reçoit rien. Aujourd'hui : `delete` (→ corbeille **réversible**), aperçu intégré en deux
temps épinglé par id — 1er appel = ce qu'il supprimerait, 2e appel avec l'id + `confirm`.
Le lot (jeton preview→confirm) est différé.
