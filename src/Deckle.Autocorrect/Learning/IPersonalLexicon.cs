namespace Deckle.Autocorrect;

// The engine-facing view of the personal dictionary: adopted words
// shield themselves from correction and join the candidate pool;
// suppressed pairs are corrections the user took back — never fired
// again on their own.
public interface IPersonalLexicon
{
    bool IsAdopted(string word);
    bool IsSuppressed(string original, string replacement);
    IReadOnlyCollection<string> AdoptedWords { get; }
}
