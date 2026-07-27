---
description: "Trip-preparation vocabulary — the seven types, their French labels, and the terms file boundary."
type: agent-instructions
---

# Deckle.Travel — Context

This context fixes the trip-preparation vocabulary. Code speaks English; the
space speaks French — the mapping below is normative.

## Types

**Stay** (Séjour):
The trip being prepared, already validated elsewhere. Bounded by dates,
identified by destination and dates, carrying no budget.
_Avoid_: project (an Anytype Dev-space concept), estimation holder

**Stage** (Étape):
The mandatory answer to « where I am from the 12th to the 15th » inside a
stay. The only object carrying a dated location, since an Anytype relation
carries no properties.
_Avoid_: optional refinement (it always exists, even degenerate)

**Place** (Lieu):
The durable « where » registry — museum, restaurant, viewpoint, lodging
address worth keeping. Enters the space only when it has value.
_Avoid_: activity (the doing, not the where), stopover café (not worth keeping)

**Activity** (Activité):
The central planned « doing » of a stay — walk, visit, evening, sport, meal,
other. Its Date is its state; it has deliberately no status property.
_Avoid_: transfer (a booked movement), status-bearing task

**Transfer** (Déplacement):
A booked movement between places or stages — flight, train, bus, ferry, car.
Bounds the stages; carries the hour that does not forgive.
_Avoid_: activity of category « trajet » (removed — the type absorbed it)

**Lodging** (Hébergement):
The booked roof of a stage: arrival/departure, address, confirmation. Answers
« where do we sleep, again? » from its stage.
_Avoid_: expense of category hébergement (an on-site receipt, e.g. campsite)

**Expense** (Dépense):
The receipt-level cost register: amount, date, category, stay — nothing else.
Rich objects link to theirs; orphan receipts stay free.
_Avoid_: reservation (lives in Transfer/Lodging), estimation (never stored)

## Vocabularies

**Activity categories**: marche, visite, soirée, sport, repas, autre.
**Expense categories**: transport, hébergement, restauration, activité,
achat, frais, autre.
**Transfer modes**: avion, train, bus, ferry, voiture.

## Terms file

The module file holding every Anytype-visible label. The schema names the
structure; the terms file names the words. One language per file; French is
the only one shipped today.
_Avoid_: resw app strings (UI wording), schema keys (stable, English)
