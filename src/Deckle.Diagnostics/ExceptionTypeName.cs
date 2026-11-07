namespace Deckle.Diagnostics;

// The value of the `ex_type` payload field, spelled one way by every provider.
//
// Why the short name and not the namespace-qualified one: a person filters the
// log on `ex_type`, and `IOException` matches the same line whichever assembly
// raised it. The namespace adds length without telling a reader anything the
// event's own provider does not already say. Before this helper, most providers
// wrote the short type name and a dozen wrote the namespace-qualified one, so
// one exception type read two ways in one file.
//
// The only call every provider makes is `ExceptionTypeName.Of(ex)`; a provider
// that computes the name by hand brings the spelling back
// (ExceptionTypeNameDoctrineTests holds the rule).
public static class ExceptionTypeName
{
    // The type name of the exception, without its namespace; empty when there
    // is no exception, since an event payload is never null.
    public static string Of(Exception? exception) => exception?.GetType().Name ?? "";
}
