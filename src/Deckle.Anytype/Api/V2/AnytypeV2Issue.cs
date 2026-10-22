namespace Deckle.Anytype;

// One entry of the v2 error envelope's `issues` array: where in the request the
// problem sits (a JSON pointer like /filter, or a path like ops[1].match) and
// what the server says about it. The hint, when present, tells how to fix it.
public sealed record AnytypeV2Issue(string Path, string Message, string? Hint);
