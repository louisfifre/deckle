---
name: deckle-nomenclature
description: Use when you give a name to anything in the Deckle repository or change one, a file, a folder, a symbol, a resource or a provider, to choose a name that says its responsibility and follows the conventions the .NET world already uses.
---

## Why

A name grounded in the world's conventions reads to anyone, and a name that says a responsibility shows what the thing is for.

## Statement

A name says what the thing is responsible for, never how it does it. A name that carries a framework, a call pattern or an internal mechanism expires at the next change, so name one level above the mechanism. An internal change invisible to consumers renames nothing. A change in public responsibility does.

Casing, structural prefixes, boolean prefixes, the `Async` suffix and event tense follow the .NET Framework Design Guidelines and the dotnet/runtime field conventions. This skill does not restate them, and the repository's `.editorconfig` turns them into IDE warnings.

Give a type the suffix that names its precise role. A suffix that would fit any type, such as Manager, Helper or Util, names no responsibility, so look for the role it hides. Two suffixes fitting one type mean two responsibilities, so split. Two names so alike they get confused signal a missing factorization or a fuzzy role, so rename to make the difference explicit.

A namespace names a module's capability, never a file's location. One module exposes one namespace, and sub-folders organize files freely without shifting or splitting it. A file and a folder are still named for the responsibility they hold, and no two modules' capability names overlap. A name that would fit any module, such as Common or Shared, names no capability.

Resources keep one marking scheme. `x:Name` names an element the code reaches, `x:Key` an entry in a resource dictionary and `x:Uid` the localized strings of an element, and each name serves its own role. An EventSource provider is named `Deckle-<Component>`, with the dash Microsoft's own providers use as separator. A theme resource is named for the function it serves, not for its value.

A name that strays from the ordinary deserves a question. It usually shows a responsibility not yet clearly seen.
