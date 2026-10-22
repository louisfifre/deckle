using System.Text.Json.Nodes;

namespace Deckle.Anytype;

// Which PATCH endpoints accept an op. Each modeled op belongs to one side (the
// four view ops, which both sides take, are not modeled), and the batch refuses
// a stray op before it spends the shared 1 rps budget on a certain 400.
[Flags]
public enum AnytypeV2OpScope
{
    Object = 1,
    Type = 2,
}

// One entry of a {"ops":[…]} batch, as the served op schema (GET /v2/schemas)
// describes it. Json is the entry as it goes on the wire, "op" discriminator
// included. The factories in the AnytypeV2*Ops classes are the only place that
// builds one, so the rules a schema states in prose (id or match, never both;
// at most one placement field) hold by construction instead of by a 400.
public sealed record AnytypeV2Op(string Kind, AnytypeV2OpScope Scope, JsonObject Json);
