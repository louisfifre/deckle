---
name: context-deckle-security
description: "Security vocabulary — the trusted-session boundary, captured content, and where Deckle's custody ends. Read before storing, exporting, or transmitting user-originated content or credentials."
type: agent-instructions
---

# Deckle.Security — Context

Vocabulary for the protection boundary shared across Deckle. This context classifies content and custody; cryptographic mechanisms and transport policies are specifications, not glossary terms.

## Trust boundary

**Trusted Windows session** :
The security perimeter Deckle assumes while it runs: the signed-in user's Windows session is not already controlled by a hostile process under that same identity. Protection covers data read outside that session and unintended disclosure across Deckle's output boundaries; resistance to same-user process inspection, clipboard access, or use of the user's own decryption authority is outside the claim.
_Avoid_ : secure session (absolute and therefore misleading), same-user malware protection (explicitly outside the boundary).

## Content and custody

Captured content is classified by where it came from; custody is classified separately by who controls the persisted copy.

**Captured content** :
User-originated content Deckle retains because it observed, recorded, transcribed, transformed, or reconstructed it — voice audio, verbatim or rewritten text, typing corpora, and future screen-derived content. Membership comes from provenance, never from a detector's sensitivity verdict: a missed credential remains captured content, while content-free settings and measurements do not become captured content merely because Deckle stores them.
_Avoid_ : telemetry (some telemetry is content-free), sensitive content (sensitivity need not be detected), user data (too broad — includes ordinary preferences).

**Managed copy** :
A persisted representation whose location and lifecycle remain under Deckle's control. A managed copy of captured content stays inside Deckle's data-at-rest protection boundary independently of what its content appears to contain.
_Avoid_ : internal file (location alone does not establish custody), sensitive file (the boundary does not depend on detection).

**User-directed output** :
A representation Deckle creates only because the user explicitly requested delivery to a destination they control — including a transcript file or an export for another tool. Custody passes at creation: ordinary interoperability is intentional, and the output is not a managed copy even when Deckle wrote the bytes.
_Avoid_ : managed copy (Deckle no longer owns its lifecycle), leak (the disclosure was explicitly directed by the user), export (too narrow — file transcription is also delivery).

**Protection failure** :
The state of a managed copy that is present but cannot be authenticated or decrypted under its expected protection authority. It is an unavailable copy, never an absent one; conflating the two would authorize silent replacement of data that may still be recoverable.
_Avoid_ : missing data (the protected bytes still exist), empty dataset (would invite overwriting it), corruption (only one possible cause).
