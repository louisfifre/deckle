namespace Deckle.Llm.Rewrite;

public sealed record ParagraphRewriteOffer(
    long Revision,
    string Original,
    string Rewritten,
    DiffGateVerdict Verdict);
