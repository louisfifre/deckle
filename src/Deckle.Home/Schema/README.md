# Home schema snapshot

The manifest and annotations are exact copies of the Home repository at commit
`100c591` (2026-11-09). They contain public structure and policies, never household
objects, room registries or provider IDs. Update them together when accepted Home
decisions change. `../Terms/terms.fr.json` carries the French labels of the same
keys, taken from the Home repository's `labels.fr.json` without its descriptions
and sections.

The space speaks the manifest keys as they are. There is no wire projection: a
type or property key in the manifest is the key Deckle sends and expects back.

`HomeSchemaTargetData` reads from the manifest each type's layout and attached
properties, each property's format and its seed options. From the annotations it
reads two things only: which vocabularies are closed, and the target types of the
relations that name them (`zone`, `installed_in`, `powered_by`, `worksite`…). The
remaining annotations are documentation, not executable constraints, an
extension policy implementation, or a migration algorithm.

Zone is an ordinary basic type with no property of its own: a Room carries the
`zone` relation, which resolves like any other relation through its annotated
target type. An absent Zone type fails the schema check like any other missing
type.

The entire required shape is checked before Home operations. A build does not
make a space ready; this module performs no schema provisioning or data
conversion. The recreation of the old space, whose seven live keys died on
2026-11-09, goes through a replay sheet written on the manifest keys, whose
old-key bridge is recorded in the annotations' `migration` section; it does not
go through this module.

The 22 Point categories are recognized exactly. Recognition does not settle the
open network, LED-assembly and control-panel classification.
