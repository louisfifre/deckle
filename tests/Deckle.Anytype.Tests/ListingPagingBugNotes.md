# Listing paging bug notes

## Exhaustive reads stepped the offset by the limit asked, not by the rows received

- **Trigger:** an exhaustive read (object listing, live tag resolution, schema snapshot) against a server that serves fewer rows per page than the `limit` the client asked, on a list longer than one such page.
- **Observed symptom:** reproduced by tests against a loopback server capped at 40 rows per page over 130 rows, not seen on a live space. The read ended with `has_more` false and returned only the rows at the start of each page window (40 and 70 of 130 in the first failing runs), with no error. A missing row looked like an absent object, an unknown tag option or a missing type or property. Uncertain: the live server's actual ceiling was not measured here; the sibling fixes for `AnytypeV2Searcher` (a10c2908) and for Home paging were made for the same reason.
- **Cause:** five loops (`ObjectListing`, two in `LiveTagResolver`, three in `SchemaSnapshotReader`) advanced `offset` by the constant they had asked as `limit`. The server chooses the page size and silently lowers a limit above its ceiling, so every row between the rows received and the next requested offset was skipped.
- **Violated invariant:** the next offset of a paged read is the number of rows the page carried, never the limit asked. An empty page ends the read even if `has_more` still claims more, so a server that never stops claiming it cannot hold the loop open.
- **Recurrence cue:** an `offset += limit` (or `offset += PageLimit`) in a paged read; a second hand-written `has_more` loop beside `PagedRead`; a list that comes back with exactly one page-window of rows per request, or whose last rows are never found.

Regression: `ListingPagingTests` (object listing, schema types, properties and tags, each capped, plus the empty-page guard for the listing and the snapshot) and `LiveTagResolverTests.FindsThePropertyWhenTheServerCapsThePage`, `FindsTheOptionWhenTheServerCapsThePage` and `StopsReadingOptionsOnAnEmptyPageEvenIfTheServerStillClaimsMore`.
