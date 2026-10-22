---
name: deckle-modularite
description: Use when code is about to grow a module, add a new one, or split a file or a surface, to decide where the boundary falls and which way the dependencies point.
metadata:
  version: "1.0"
---

## Why

Modules keep the application legible and easy to grow. Each capability is a separable unit, and dependencies run one way.

## Statement

A module carries one responsibility that one sentence can name, and it exposes a narrow public surface. When the sentence needs more, or the surface keeps spreading, the module holds several responsibilities. Separate them.

Know which kind of module you are writing before you grow it, because the kind sets the rules. The dividing question is whether it holds an active runtime state that acts on the system. If it does, it is a domain module. It owns that state and everything attached to it, settings and persistence included. If it does not, it is support, pure code that others call, the bottom layer, referenced widely and depending on almost nothing. A module that owns no domain and assembles others is a shell when it only presents them, and a host when its whole job is to aggregate them by role. A host references widely, because that is its job.

Dependencies point one way, toward the more fundamental module. A cycle means a shared notion should rise into a common module, or the two modules are one.

Ask on every change whether this could live in its own file. The answer is usually yes. Give each cohesive group of functions its own small file named for its responsibility, and keep no function stranded alone. A tree of named files shows what changed at a glance, so it takes the place of section comments inside a file and of thousand-line files. Keep code together only when separating it would cost real load or runtime.

Settings are modular too. Each module carries its own settings page and its own persistence. An engine meant to be reused stays free of interface code, and the module that presents it carries the page.
