---
description: "Home inventory vocabulary. It separates the public norm from the personal room registry, and defines point codes and the managed Home schema."
---

# Deckle.Home, Glossary

This context names the boundary between Deckle's shareable home-inventory mechanism and the personal data held in each user's Anytype space.

## Norm and data

**Home norm** :
The shareable vocabulary and constraints of the Home domain, carried by `Deckle.Home`, such as the point-code grammar, the frozen category vocabulary, the closed lifecycle vocabularies and the invariants. It is the part of Home that may be published.
_Avoid_ : room registry (personal data), inventory (the user's recorded objects).

**Room registry** :
The live set of room codes and room objects in the configured Home space. It is personal data. It is the authority that decides whether a point-code prefix is known, and it never becomes a constant compiled into code.
_Avoid_ : nomenclature (the registry instantiates the norm but is not the norm), configuration list (a second truth).

**Home inventory** :
The user's rooms, points, circuits, panels and their relations, stored in the Home space. It belongs to the user and is never part of Deckle's source.
_Avoid_ : Home norm (shareable mechanism), schema (storage contract).

**Nomenclature** :
In ordinary use, a system of names and codes. Here it would blur the Home norm, which defines the code system, with the room registry, which fills it with one household's rooms.

**Configuration list** :
A list of values written into Deckle's configuration. A room list kept there would become a second source of truth beside the room registry.

## Codes and schema

**Point code** :
The immutable identifier of one physical point, made of a room prefix, a category code and a two-digit sequence. Its room prefix is valid only when the room registry contains it. The point's category and its room both derive from the code, so neither can be written on its own.
_Avoid_ : object id (Anytype runtime coordinate), room code (only the location prefix).

**Home schema** :
The public, managed Anytype contract that the Home domain requires. Its type keys and property keys are stable. Ids and tag keys are local, resolved from the live space.
_Avoid_ : Home inventory (values stored under the schema), space id (local coordinate).

**Inventory code** :
The immutable code carried by the `code` property of a coded inventory object, never by its title. Titles are human names, written for the whole household. A circuit alone may use its code as a provisional title until someone renames it.
_Avoid_ : object id (provider coordinate), label (human wording), object title (the human name, no longer the code).

**Home surface** :
The MCP capability that exposes the guarded Home operations through Deckle's single resident MCP host. It adapts `Deckle.Home` and does not own its rules.
_Avoid_ : Home space (data store), Home module (domain implementation).

**Object id** :
The identifier Anytype gives an object at runtime, in one live space. It is a local coordinate, not a code that Deckle defines.

**Room code** :
The code of one room, which forms the prefix of every point code in that room. It is only that prefix, not the identifier of a point.

**Space id** :
The identifier Anytype gives a space in one installation. It is a local coordinate, not part of the Home schema.

**Label** :
In ordinary use, the human-readable text shown for something. It is wording for people, never an identifier like a code or a key.

**Object title** :
The human name an Anytype object shows, written for the household. It no longer carries the inventory code.

**Home space** :
The user's Anytype space that stores the Home inventory. It holds the data, while the Home surface exposes operations on it.

**Home module** :
The `Deckle.Home` code that implements the Home domain and its rules. The Home surface is only an adapter over it.
