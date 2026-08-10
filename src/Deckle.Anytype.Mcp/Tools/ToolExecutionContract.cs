namespace Deckle.Anytype.Mcp;

// Model-facing execution semantics owned by the tool catalog. The MCP
// transport may project the generic flags into protocol annotations, but never
// branches on a domain tool name.
public enum ToolEffect
{
    ReadOnly,
    Mutating,
    Destructive,
}

// The standard MCP destructive hint distinguishes additive writes from every
// write that may replace or remove existing state. That distinction is
// intentionally separate from Deckle's broader effect classification above.
public enum ToolChangeKind
{
    None,
    Additive,
    Overwriting,
    Destructive,
}

public enum AmbiguousOutcomePolicy
{
    SafeToRetry,
    VerifyBeforeRetry,
    RequiresDeduplication,
    Uncertain,
}

public sealed record ToolExecutionContract(
    ToolEffect Effect,
    ToolChangeKind Change,
    AmbiguousOutcomePolicy AmbiguousOutcome,
    bool RequiresStableTarget = false)
{
    public static ToolExecutionContract ReadOnly { get; } =
        new(ToolEffect.ReadOnly, ToolChangeKind.None, AmbiguousOutcomePolicy.SafeToRetry);

    public static ToolExecutionContract AdditiveRequiresDeduplication { get; } =
        new(ToolEffect.Mutating, ToolChangeKind.Additive, AmbiguousOutcomePolicy.RequiresDeduplication);

    public static ToolExecutionContract AdditiveRequiresDeduplicationWithStableTarget { get; } =
        new(
            ToolEffect.Mutating,
            ToolChangeKind.Additive,
            AmbiguousOutcomePolicy.RequiresDeduplication,
            RequiresStableTarget: true);

    public static ToolExecutionContract AdditiveUncertain { get; } =
        new(ToolEffect.Mutating, ToolChangeKind.Additive, AmbiguousOutcomePolicy.Uncertain);

    public static ToolExecutionContract AdditiveVerifiable { get; } =
        new(ToolEffect.Mutating, ToolChangeKind.Additive, AmbiguousOutcomePolicy.VerifyBeforeRetry);

    public static ToolExecutionContract AdditiveVerifiableWithStableTarget { get; } =
        new(
            ToolEffect.Mutating,
            ToolChangeKind.Additive,
            AmbiguousOutcomePolicy.VerifyBeforeRetry,
            RequiresStableTarget: true);

    public static ToolExecutionContract OverwritingIdempotent { get; } =
        new(ToolEffect.Mutating, ToolChangeKind.Overwriting, AmbiguousOutcomePolicy.SafeToRetry);

    public static ToolExecutionContract OverwritingIdempotentWithStableTarget { get; } =
        new(
            ToolEffect.Mutating,
            ToolChangeKind.Overwriting,
            AmbiguousOutcomePolicy.SafeToRetry,
            RequiresStableTarget: true);

    public static ToolExecutionContract OverwritingUncertain { get; } =
        new(ToolEffect.Mutating, ToolChangeKind.Overwriting, AmbiguousOutcomePolicy.Uncertain);

    public static ToolExecutionContract DestructiveVerifiable { get; } =
        new(
            ToolEffect.Destructive,
            ToolChangeKind.Destructive,
            AmbiguousOutcomePolicy.VerifyBeforeRetry,
            RequiresStableTarget: true);
}
