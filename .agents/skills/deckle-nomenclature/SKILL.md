---
name: deckle-nomenclature
description: One normalized way to name files, folders, symbols, resources and providers. Invoke before naming or renaming.
type: skill
---

# Deckle — Naming

## Intent

Agree one normalized way to name everything — symbols, files, folders, resources, providers — grounded in world conventions so the name reads to anyone and its responsibility shows. Habits meant to carry across projects, not only this one.

## How

A name says what the thing is responsible for, never how it does it. A name carrying a framework, a call pattern, or an internal mechanism expires at the next change — step up a level. So an internal change invisible to consumers forces no rename; a change in public responsibility does.

Casing, structural prefixes, boolean prefixes, the `Async` suffix and event tense follow the .NET Framework Design Guidelines and the dotnet/runtime field conventions — the world convention, not restated here.

Prefer a suffix that names the precise role over the overflow ones — Manager, Helper, Util(s), a generic Wrapper — which name an unowned dump rather than a responsibility. Two suffixes fitting one type means two responsibilities — split. And two names so alike they get confused signal a missing factorization or a fuzzy role — rename to make the difference explicit.

A namespace names a module's capability, never a file's location: one module exposes one namespace, and sub-folders organize files freely without ever shifting or splitting it. A file and a folder are still named for the responsibility they hold, and no two modules' capability names overlap. Avoid the fuzzy generic namespaces (Common, Shared, Utilities) — name the real capability.

Mark resources consistently: the `x:Name`/`x:Key`/`x:Uid` split kept distinct, a `.resw` key frozen once sent for translation, an EventSource provider as `Deckle-<Component>` (dash, never dot — ETW collision). Theme resources are named by function, not value — that rule lives in `deckle-interface`.

A name that strays from the ordinary deserves questioning — it usually flags a responsibility not yet clearly seen.
