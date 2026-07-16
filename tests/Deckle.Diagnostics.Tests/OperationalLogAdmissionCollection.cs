using Xunit;

namespace Deckle.Diagnostics.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class OperationalLogAdmissionCollection
{
    public const string Name = "OperationalLogAdmission";
}
