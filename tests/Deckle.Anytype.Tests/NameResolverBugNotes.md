# Name resolver bug notes

## A write named an object by a name that only resembled it, and the resolver took the lone hit

- Trigger: a gesture that mutates (update, complete, link, archive, delete, set properties, rename, log) receives a name that no object carries exactly, and the search returns a single hit.
- Observed symptom: the write landed on the one object the search ranked, whatever its real name. The caller saw a success and nothing said that the name had been guessed. Separately, when a name had many hits, the object named exactly could sit behind the 20 hits the resolver read, so an exact match was missed.
- Cause: `NameResolver` accepted a lone inexact candidate for every caller, reads and writes alike, and read a single page of 20 hits. A wrong write changes the wrong object and no later read points at it.
- Violated invariant: a write resolves a name only on an exact match (case-insensitive, same comparison as the existing exact step); anything else is refused with the candidates by id. An exact match is never missed because of the candidate cap. Reads keep the tolerant acceptance.
- Fix: `NameResolver.ResolveAsync` takes a required `NameMatch` (`Tolerant` for reads, `Exact` for writes). `Exact` refuses with `InexactNameException` listing the nearest candidates, and pages the search up to 1000 hits to look for the exact name. Past that ceiling the write is refused (fail closed), and a second exact-named object beyond it would go unseen (uncertain: not measured on a real space).
- Recurrence cue: a mutation call given a partial name that succeeds, or a new write gesture that resolves with `NameMatch.Tolerant`.
- Guarded by: `NameResolverTests` (the write refusals and the later-page exact match) and the archive and complete cases of `QueryGesturesTests`.

## An idea or a report could not be resolved by its title once resolution moved to the v2 search

- Trigger: a typed read or a write names an idea or a report by its title, in the default space, since name resolution moved to the v2 search (60d9fdac, 2026-10-22).
- Observed symptom: on the live space on 2026-10-27, `idea_text "Socle MCP"` failed with four candidates that all read « (sans titre) ». Before the fix, `search` had just listed that idea under that very title. Only the id copied from `search` reached it.
- Cause: an idea or a report has an empty `name`; its title is the first line of its body. A v2 search row carries no snippet. Asking for one with `fields: ["snippet"]` gets a 400 (measured 2026-10-27). So every candidate's display name was « (sans titre) » and no title could match exactly. The v1 search, which `search` still uses, carries the snippet that gives the title.
- Violated invariant: an object a search shows under a title resolves by that title, whatever its layout.
- Fix: `NameResolver` resolves on the v1 search whenever the requested types include `idee` or `rapport`. A resolution without a type filter stays on v2, so a nameless object still reads « (sans titre) » there.
- Recurrence cue: a typed read of an idea or a report that lists candidates named « (sans titre) », or a resolver change that routes body-titled types through a search answer without a snippet.
- Guarded by: `NameResolverTests.AnIdeaResolvesByTheTitleItsBodyCarries`.