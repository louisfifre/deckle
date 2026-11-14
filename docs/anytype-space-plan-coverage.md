---
description: Everything an Anytype space is made of, one row per element, with what Deckle's space plan reads, writes and removes, the API limit that blocks it, and the script outside the repository that already does the missing part. The checklist for "Deckle can rebuild and reorganise any space".
---

# Anytype space plan coverage

The goal: through Deckle's MCP, build a space from zero, reorganise it, remove what is no longer wanted, and read it back so a second space is the same. This page is the checklist for that goal. A row is done when its three columns read "yes", or when its limit is an API limit named below.

State read from the code on 2026-11-14 (main at `c7fbcc51`). The space plan engine is `src/Deckle.Anytype/Plan/` plus the schema stage in `src/Deckle.Anytype/Gestures/SchemaPlanner.cs`. Its tools sit on the schema-admin surface only (`SpacePlanToolCatalog.cs`: `space_plan_preview`, `space_plan_apply`, `space_plan_read`, `create_type_preview`, `create_type`). The everyday tools (`create_task`, `create_project`…) carry no plan gesture.

"Script" names a file in `D:\anytype\tools` or `D:\anytype\migrations\pm-2026-11\tools`, outside any repository, that wrote the missing part on the live Project management space. Their how is in homelab's runbook `docs/runbooks/deb-serv-anytype-sync.md`, section « The project-management space's views, templates and home page ».

## The checklist

| # | Element | Read | Write | Remove | Blocking limit, or where it is done today |
|---|---|---|---|---|---|
| 1 | The space itself | n/a | no | no | No route deletes a space. The bot cannot invite the owner, so a person creates the space and admits the bot (`scripts/maintenance/apply-anytype-schema.ps1` header). |
| 2 | Properties | yes | create, rename | no | Changing a property's format is refused as a conflict (`SchemaPlanner.cs`). |
| 3 | Options of a select: name, colour | yes | create only | no | v2 writes a colour only at creation; v1 `PATCH …/properties/{id}/tags/{tag_id}` recolours and renames. Script: `color-options.ps1`. Removing an option is untried (v1, probably). |
| 4 | Options of a select: order | no | no | n/a | Not rendered by the API (`schema_inspect_space` says so). |
| 5 | Types: name, plural, icon, description | yes | create, rename; icon and description only when empty | no | v1 `PATCH /v1/…/types/{id}` writes the icon. Layout cannot change after creation. Native types removed from a space cannot be recreated under their key. |
| 6 | Type panel: header, plain list, hidden | yes | yes (`type_panel_add`, `type_panel_remove`, `type_panel_order`) | yes | Script: `order-type-panels.ps1`, same ground. |
| 7 | Type display: header in a list or inline (`header_relations_layout`), page width (`layout_width`) | no | no | n/a | `PATCH types/{key}` refuses them; `PATCH objects/{type id}` with `set_properties` takes them. Script: `order-type-panels.ps1` (`TypeDisplay`). Their absence lost every header on 2026-11-14. |
| 8 | Views of a type: kind, columns, sorts, filters, grouping | yes | yes | yes, views missing from the plan are deleted | Every `insert_view` adds a « last opened » column the API cannot hide. Sorts and filters need an `id` or they collapse after a reload. Scripts: `restore-views-from-export.ps1`, `compare-with-export.ps1`. |
| 9 | Hidden kanban groups (« No value »…) | no | no | n/a | `set.groups` is output-only editor state. By hand in the app. |
| 10 | Views of native types (Page, Note, Chat…) | no | no | no | Owner only (403 to an editor). By hand in the app. |
| 11 | Collections: members and views | yes | create, add missing members, views | views only | Extra members and extra collections are left in place. |
| 12 | Queries | yes | create, single-type source only | no | A query whose source differs is reported, not rewritten. |
| 13 | Templates and a type's default template | yes | create; replace when the body differs; set the default | only the replaced template | Name, icon and property presets of a template are not compared. |
| 14 | Pages with views in their body (« Accueil ») | only pages named in `pages` | create only | no | A page that exists and differs is reported `page_differs`, never rewritten: delete it by hand to rebuild. Script: `apply-home.ps1`. Page views copied from the Tâche type: `copy-type-views-to-pages.ps1`. |
| 15 | Widgets of the shared sidebar | yes | create, update (stage `widgets`, owner's key) | no | Editor key refused. Script: `apply-sidebar.ps1`, run by the owner. ADR-0010 still names the sidebar as manual. |
| 16 | The space's home page setting | no | no | no | Not modelled at all. |
| 17 | Objects: properties | never read | create (stage `objects`), skip when one of that type and name exists | no | Existing objects are not updated. |
| 18 | Objects: bodies | never read | from the template only | no | No body field in `object_create`. |
| 19 | Links between objects (object properties) | never read | values copied raw | no | No name-to-id resolution for property values. |
| 20 | An Experience export | n/a | n/a | n/a | The owner's act in the app (ADR-0010). What it carries (views, widgets, templates) is unverified. |

## What "remove" means today

`space_plan_apply` adds and corrects; it prunes only where the plan is exact by construction:

- a type's property list: extras are detached from the type, never deleted from the space;
- a type's panel sections;
- the views over a type, a collection or a page;
- a template whose body differs, replaced by a new one.

Every other element present in the space and absent from the plan stays. `create_type` is the add-only variant and refuses any removal.

## The proof

The checklist is met when this round trip shows no difference, checked again ten minutes after the write:

1. `space_plan_read` on Project management, pages and objects included.
2. `space_plan_apply` of that plan on a throwaway space (`throwaway-bot.sh` in the homelab runbook).
3. `space_plan_read` of the throwaway, compared with step 1, plus `compare-with-export.ps1` against the original.

It has not run on Project management yet. Its plan, `D:\anytype\space-builder\plans\pm.generated.plan.json`, holds no pages and no objects.
