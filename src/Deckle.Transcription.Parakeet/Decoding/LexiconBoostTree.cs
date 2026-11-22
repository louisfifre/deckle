namespace Deckle.Transcription.Parakeet.Decoding;

// Context biasing for the greedy TDT decoder, after the phrase-boosting tree of
// NeMo's GPU-PB (TurboBias, arXiv 2508.07014), carried to a single greedy pass on
// the CPU.
//
// The terms are stored in a trie of characters, each term starting with a space
// because a Parakeet word starts with SentencePiece's "▁". Matching works on
// characters, not on one fixed split into tokens: any token whose text walks the
// trie from the current node is a valid next step, so the model may spell a term
// with whatever tokens it prefers. Matching ignores case: the vocabulary is cased
// ("▁De" and "▁de" both exist), and the casing pass afterwards writes the term as
// the person wrote it.
//
// The decoder keeps one state, the node reached by the tokens it emitted. At each
// step the tokens that move the match forward get a bonus, and so do the tokens
// that start a term afresh (the root's), since a match in progress may be the
// wrong one. A token's bonus is the strength times the share of the term the
// match covers once the token is taken, measured on the shortest term that the
// node can still complete. A common first piece (" de" for "Deckle") thus gets a
// small push, and the push grows as the match goes on. Every transition is
// computed at build time: the tree is immutable and costs no allocation while
// decoding.

/// <summary>Immutable trie of lexicon terms over a token vocabulary, giving a logit bonus per decoder state.</summary>
internal sealed class LexiconBoostTree
{
    /// <summary>The state with no match in progress.</summary>
    public const int Root = 0;

    private readonly Transition[][] _transitions;
    private readonly Dictionary<int, Transition>[] _byToken;

    private LexiconBoostTree(Transition[][] transitions, Dictionary<int, Transition>[] byToken, int termCount)
    {
        _transitions = transitions;
        _byToken = byToken;
        TermCount = termCount;
    }

    /// <summary>How many distinct terms the tree holds.</summary>
    public int TermCount { get; }

    /// <summary>How many trie nodes the tree holds, the root included.</summary>
    public int NodeCount => _transitions.Length;

    /// <summary>A step from a node: emitting <see cref="Token"/> moves to <see cref="Next"/> and earns <see cref="Bonus"/>.</summary>
    public readonly record struct Transition(int Token, int Next, float Bonus);

    /// <summary>The tokens that continue a match from <paramref name="state"/>, with their bonus.</summary>
    public ReadOnlySpan<Transition> From(int state) => _transitions[state];

    /// <summary>The bonus <paramref name="token"/> earns from <paramref name="state"/>; zero when it does not continue the match.</summary>
    public float BonusOf(int state, int token) =>
        _byToken[state].TryGetValue(token, out Transition step) ? step.Bonus : 0f;

    /// <summary>
    /// The state after <paramref name="token"/> is emitted in <paramref name="state"/>: the match goes on
    /// when the token continues it, a new match starts when the token opens a term, and otherwise
    /// the match is dropped.
    /// </summary>
    public int Advance(int state, int token)
    {
        if (_byToken[state].TryGetValue(token, out Transition step)) return step.Next;
        if (state != Root && _byToken[Root].TryGetValue(token, out step)) return step.Next;
        return Root;
    }

    /// <summary>
    /// Builds the tree. <paramref name="tokenTexts"/> holds the text of each token id, a leading space
    /// for a word start and empty for silent ids. Returns null when no term is usable.
    /// </summary>
    public static LexiconBoostTree? Build(IEnumerable<string> terms, IReadOnlyList<string> tokenTexts, float strength)
    {
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(tokenTexts);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(strength);

        var trie = new Trie();
        int termCount = 0;
        foreach (string term in terms)
        {
            string key = Key(term);
            if (key.Length > 1 && trie.Insert(key)) termCount++;
        }

        if (termCount == 0) return null;

        // Tokens grouped by their first character, lowercased: a node only tries the tokens
        // that begin with one of its children's characters.
        var byFirstChar = new Dictionary<char, List<(int Id, string Text)>>();
        for (int id = 0; id < tokenTexts.Count; id++)
        {
            string text = tokenTexts[id].ToLowerInvariant();
            if (text.Length == 0) continue;
            if (!byFirstChar.TryGetValue(text[0], out var bucket))
            {
                byFirstChar[text[0]] = bucket = [];
            }
            bucket.Add((id, text));
        }

        int nodeCount = trie.Count;
        var transitions = new Transition[nodeCount][];
        var byToken = new Dictionary<int, Transition>[nodeCount];
        for (int node = 0; node < nodeCount; node++)
        {
            var steps = new List<Transition>();
            foreach (char first in trie.Children(node).Keys)
            {
                if (!byFirstChar.TryGetValue(first, out var bucket)) continue;
                foreach ((int id, string text) in bucket)
                {
                    int landing = trie.Walk(node, text);
                    if (landing < 0) continue;

                    // The bonus is measured where the token lands; a term completed with nothing
                    // after it sends the match back to the root, ready for the next one.
                    float bonus = strength * trie.Depth(landing) / trie.ShortestTermThrough(landing);
                    int next = trie.IsTerminal(landing) && trie.Children(landing).Count == 0 ? Root : landing;
                    steps.Add(new Transition(id, next, bonus));
                }
            }

            transitions[node] = [.. steps];
            byToken[node] = steps.ToDictionary(s => s.Token);
        }

        return new LexiconBoostTree(transitions, byToken, termCount);
    }

    // The trie key: a leading space for the word start, lowercase, one space between words.
    private static string Key(string term) =>
        " " + string.Join(' ', term.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    /// <summary>Mutable character trie used only while building.</summary>
    private sealed class Trie
    {
        private readonly List<Dictionary<char, int>> _children = [new()];
        private readonly List<int> _depth = [0];
        private readonly List<bool> _terminal = [false];
        private readonly List<int> _shortest = [int.MaxValue];

        public int Count => _children.Count;

        public Dictionary<char, int> Children(int node) => _children[node];
        public int Depth(int node) => _depth[node];
        public bool IsTerminal(int node) => _terminal[node];
        public int ShortestTermThrough(int node) => _shortest[node];

        /// <summary>Adds a key; false when it was already there.</summary>
        public bool Insert(string key)
        {
            int node = 0;
            var path = new List<int>(key.Length + 1) { 0 };
            foreach (char c in key)
            {
                if (!_children[node].TryGetValue(c, out int child))
                {
                    child = _children.Count;
                    _children.Add([]);
                    _depth.Add(_depth[node] + 1);
                    _terminal.Add(false);
                    _shortest.Add(int.MaxValue);
                    _children[node][c] = child;
                }
                node = child;
                path.Add(node);
            }

            if (_terminal[node]) return false;
            _terminal[node] = true;
            foreach (int n in path)
            {
                _shortest[n] = Math.Min(_shortest[n], key.Length);
            }
            return true;
        }

        /// <summary>The node reached by reading <paramref name="text"/> from <paramref name="node"/>, or -1.</summary>
        public int Walk(int node, string text)
        {
            foreach (char c in text)
            {
                if (!_children[node].TryGetValue(c, out node)) return -1;
            }
            return node;
        }
    }
}
