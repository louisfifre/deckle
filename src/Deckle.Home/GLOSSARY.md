---
name: glossary-deckle-home
description: "Home inventory vocabulary. It defines the inventory code and the Home surface, the two notions the Home module owns today, and keeps the user's own inventory out of Deckle's source."
---

# Deckle.Home, Glossary

This glossary names what the Home module owns. The inventory itself, the rooms, the points and their relations, is the user's data and lives in their Anytype space, never in Deckle's source. The domain is still being built beside the code, so this file stays small until its words settle.

## Codes and surface

**Inventory code** :
The immutable code carried by the `code` property of a coded inventory object, never by its title. Titles are human names, written for the whole household. A circuit alone may use its code as a provisional title until someone renames it.
_Avoid_ : object id (provider coordinate), label (human wording), object title (the human name, no longer the code).

**Home surface** :
The MCP surface that exposes the guarded Home operations through Deckle's single resident MCP host. It adapts the Home module and does not own its rules.
_Avoid_ : Home space (data store), Home module (domain implementation).

**Object id** :
The identifier Anytype gives an object at runtime, in one live space. It is a local coordinate, not a code that Deckle defines.

**Label** :
In ordinary use, the human-readable text shown for something. It is wording for people, never an identifier like a code or a key.

**Object title** :
The human name an Anytype object shows, written for the household. It no longer carries the inventory code.

**Home space** :
The user's Anytype space that stores their inventory. It holds the data, while the Home surface exposes operations on it.

**Home module** :
The `Deckle.Home` code that implements the Home domain and its rules. The Home surface is only an adapter over it.
