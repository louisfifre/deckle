---
name: glossary-deckle-security
description: "Security vocabulary. It defines the trusted-session boundary, captured content, and where Deckle's custody ends. Read before storing, exporting, or transmitting user-originated content or credentials."
---

# Deckle.Security, Glossary

Vocabulary for the protection boundary shared across Deckle. It classifies content and custody. Cryptographic mechanisms and transport policies are specifications, not glossary terms.

## Trust boundary

**Trusted Windows session** :
The security perimeter Deckle assumes while it runs, a signed-in Windows session that no hostile process already controls under the same identity. Deckle's protection covers someone reading its data from outside that session, and unintended disclosure through Deckle's outputs. It makes no claim against a process of the same user that inspects Deckle, reads the clipboard or uses the user's own decryption authority.
_Avoid_ : secure session (absolute and therefore misleading), same-user malware protection (explicitly outside the boundary).

**Secure session** :
A session promised to be safe against every threat. Deckle makes no absolute claim, so it speaks of a trusted Windows session.

**Same-user malware protection** :
Protection against a hostile program that runs under the user's own identity. Deckle makes no such claim, because that threat lies outside the trusted Windows session.

## Content and custody

Captured content is classified by where it came from. Custody is classified separately, by who controls the persisted copy.

**Captured content** :
Content that comes from the user and that Deckle retains because it observed, recorded, transcribed, transformed or reconstructed it, such as voice audio, verbatim or rewritten text, typing corpora, and future screen-derived content. What makes it captured content is its origin, never a detector's verdict on sensitivity. A credential a detector missed is still captured content, and a setting or a measurement that holds no content does not become captured content because Deckle stores it.
_Avoid_ : telemetry (some telemetry is content-free, see `src/Deckle.Diagnostics/GLOSSARY.md`), sensitive content (sensitivity need not be detected), user data (too broad, it includes ordinary preferences).

**Managed copy** :
A persisted representation whose location and lifecycle Deckle controls. A managed copy of captured content stays inside Deckle's protection for data at rest, whatever its content appears to contain.
_Avoid_ : internal file (location alone does not establish custody), sensitive file (the boundary does not depend on detection).

**User-directed output** :
A persisted representation that Deckle creates only because the user explicitly asked for delivery to a destination they control, such as a transcript file or an export for another tool. Custody passes to the user when it is created. This interoperability is intended, so the output is not a managed copy, even though Deckle wrote the bytes.
_Avoid_ : managed copy (Deckle no longer owns its lifecycle), leak (the user explicitly directed the disclosure), export (too narrow, file transcription is also delivery).

**Protection failure** :
The state of a managed copy that is present but cannot be authenticated or decrypted under its expected protection authority. It is an unavailable copy, never an absent one. Treating it as absent would allow the silent replacement of data that may still be recoverable.
_Avoid_ : missing data (the protected bytes still exist), empty dataset (would invite overwriting it), corruption (only one possible cause).

**Sensitive content** :
Content judged private or dangerous to disclose, usually by a detector. Deckle classifies content by its origin rather than by such a verdict, so it speaks of captured content.

**User data** :
Anything that belongs to the user, ordinary preferences included. The word is too broad for the content Deckle retains because it captured it, which is captured content.

**Internal file** :
A file stored in a place that looks internal to Deckle. Location alone does not decide custody, so the Deckle term is managed copy.

**Sensitive file** :
A file judged sensitive from what it contains. Deckle's protection does not depend on such a judgment, so it speaks of a managed copy.

**Leak** :
A disclosure that nobody intended. An output the user asked for is a user-directed output.

**Export** :
In ordinary use, writing data out in a format another tool can read. It is one kind of user-directed output, too narrow to name them all, since the text file written by a file transcription is another.

**Missing data** :
Data that does not exist where it is expected. A copy in protection failure still exists.

**Empty dataset** :
A dataset that holds no records. Calling a protection failure empty would invite overwriting data that still exists.

**Corruption** :
Damage to stored bytes that makes them unreadable. It is one possible cause of a protection failure, not its name.
