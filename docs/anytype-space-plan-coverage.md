---
description: Everything an Anytype space is made of, one row per element, with what Deckle's space plan reads, writes and removes, the API limit that blocks the rest, and what is still by hand. The checklist for "Deckle can rebuild and reorganise any space".
---

# Anytype space plan coverage

The goal: through Deckle's MCP, build a space from zero, reorganise it, remove what is no longer wanted, and read it back so a second space is the same. This page is the checklist for that goal. A row is done when its three columns read "yes", or when what is left is an API limit named in the row.

The space plan engine is `src/Deckle.Anytype/Plan/` plus the schema stage in `src/Deckle.Anytype/Gestures/SchemaPlanner.cs`. Its tools sit on the schema-admin surface only (`SpacePlanToolCatalog.cs`: `space_plan_preview`, `space_plan_apply`, `space_plan_read`, `create_type_preview`, `create_type`). The everyday tools (`create_task`, `create_project`…) carry no plan gesture.

"Remove" happens only in the `prune` stage, asked for explicitly beside the others (`stages: ["schema", "structure", "prune"]`). It removes, within the stages asked for, what the space holds and a section the plan states leaves out. A section the plan omits is left alone, natives are never removed, and every removal is listed in the preview before the apply.

## The checklist

| # | Element | Read | Write | Remove | What is left |
|---|---|---|---|---|---|
| 1 | The space itself | n/a | no | no | No route deletes a space. The bot cannot invite the owner: a person creates the space and admits the bot, or the bot creates it and owns it alone (`POST /v2/spaces`). |
| 2 | Properties | yes | create, rename | yes (prune) | Changing a property's format is refused as a conflict. |
| 3 | Options of a select: name, colour | yes | create; recolour an existing one (v1) | yes (prune, v1) | — |
| 4 | Options of a select: order | no | no | n/a | Not rendered by the API. |
| 5 | Types: name, plural, icon, description | yes | create, rename; icon and description only when empty | yes (prune) | Layout cannot change after creation. An icon that exists is not replaced. Native types removed from a space cannot be recreated under their key. |
| 6 | Type panel: header, plain list, hidden | yes | yes | yes | — |
| 7 | Type display: header in a list or inline, page width | yes (`display`) | yes | n/a | — |
| 8 | Views of a type: kind, columns, sorts, filters, grouping | yes | yes | yes, views missing from the plan are deleted | Every `insert_view` adds a « last opened » column the API cannot hide. Sorts and filters need an `id` or they collapse after a reload. |
| 9 | Hidden kanban groups (« No value »…) | no | no | n/a | `set.groups` is output-only editor state. By hand in the app. |
| 10 | Views of native types (Page, Note, Chat…) | no | no | no | Owner only (403 to an editor). By hand in the app. |
| 11 | Collections: members and views | yes | create, add members, views | members and collections (prune) | A collection the bot did not create is refused (403). |
| 12 | Queries | yes | create, single-type source only | yes (prune) | A query whose source differs is reported, not rewritten. One the bot did not create is refused. |
| 13 | Templates and a type's default template | yes | create; replace when the body differs; set the default | yes (prune) | Name, icon and property presets are not compared. A template the app made (« Project example ») is refused. A template for a note-layout type loses its name, so the validator refuses it. |
| 14 | Pages with views in their body (« Accueil ») | only pages named in `pages` | create; rewrite in place when the body differs, id kept | no | — |
| 15 | Widgets of the shared sidebar | yes | create, update (stage `widgets`, owner's key) | yes (prune, owner's key) | Editor key refused. Removal is untried, since it needs the owner's key. |
| 16 | The space's home page setting | no | no | no | Not modelled at all. |
| 17 | Objects: properties | never read | create (stage `objects`); skip when one of that type and name exists | no | Existing objects are not updated. |
| 18 | Objects: bodies | never read | from the template only | no | No body field in `object_create`. |
| 19 | Links between objects (object properties) | never read | values copied raw | no | No name-to-id resolution for property values. |
| 20 | An Experience export | n/a | n/a | n/a | The owner's act in the app (ADR-0010). What it carries is unverified. |

## A removal the server refuses

`DELETE objects/{id}` bins only objects the deleting key created. Templates, collections and queries made in the app, or before the API recorded a creator, answer 403. The prune reports each refusal and goes on with the others, and the next preview lists it again: the space still holds it. Archive it in the app to end it.

## The proof

On 2026-11-14, on a throwaway space the bot created (`plan-trial-2026-11-14-prune`):

1. `space_plan_read` of Project management, the page « Accueil » included: 31 properties, 8 types, 6 templates, 1 collection, 7 widgets.
2. That plan built on the empty space (schema and structure), then a second preview: nothing to do.
3. A second plan with extras (a property, an option, a type, a template, a collection, a paragraph on « Accueil ») and changes (an option colour, Tâche's header inline), built, then previewed: nothing to do.
4. The original plan with `prune`: every extra removed, the colour and the header back, « Accueil » rewritten, then previewed: nothing to do except the app-made template « Project example », refused as above.

Not covered: the sidebar (owner's key), objects, and the ten-minute re-check after a write.
