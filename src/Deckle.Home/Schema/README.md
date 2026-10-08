# Home schema snapshot

The manifest and annotations are exact copies of the Home repository at commit
`d4f98c6` (2026-09-11). They contain public structure and policies, never household
objects, room registries or provider IDs. Update them together when accepted Home
decisions change. `Terms/terms.fr.json` contains the corresponding French labels
under the current wire coordinates, with legacy terms retained for callers.

`HomeSchemaTargetData` reads the manifest's formats, type membership and seed
options. It reads only closed-vocabulary policy and relation target types from
the annotations. It also preserves the pre-existing Worksite and powered_by
target guards. The remaining annotations are documentation, not executable
constraints, an extension policy implementation, or a migration algorithm.

`HomeSchema.WirePropertyKey` explicitly retains six historical coordinates:
zone/floor, manual/documents, product_category/errand_category,
memory_size/memory_size_gb, memory_frequency/memory_frequency_mhz,
switch_nature/switch_kind. Product still addresses the historical errand type.
Zone objects address the runtime-discovered collection type. New model-facing
fields use canonical names. This projection makes no choice about the final
experience/reconstruction migration and performs no fallback or live renaming.
The old power/power_watts mapping is deliberately not applied to either new
power quantity. Output power stays unattached pending the assembly review.

The entire required shape is checked before Home operations. In particular,
checkbox Earth/DCL and scalar control_link are incompatible. A build does not
make an old live space ready. Preserve and compare data before an explicit
migration; this module performs no schema provisioning or data conversion.

The retained Point category list is recognized exactly. Recognition does not
settle the deferred network, LED-assembly and control-panel classification.
