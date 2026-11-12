using System.Linq;
using System.Text;

namespace Deckle.Llm.Rewrite;

// The document of one take: its blocks in speech order, each with its raw
// text and the reading it shows, then the text still open after them. The
// transcript panel and the clipboard read it; DictationBatchRewriter writes
// it as rewrites come back.
//
// A block is either a passage the batcher closed, or a group of earlier
// blocks a later step rewrites as one (Regroup): when a step takes over, it
// cuts the text already said into its own blocks, and each of those covers
// whole blocks of the step before. Grouping whole blocks, never cutting one,
// is what lets a block keep showing its earlier reading until the new one of
// its group arrives: the group shows its children until it has a reading of
// its own (specs/deckle-dictation-rewriting.md, Panel 3).
//
// Four guarantees, the reason it exists apart from the rewriter:
//   - the raw text of a block never changes, and every rewrite starts from
//     it, never from an earlier rewrite (two voices would mix otherwise);
//   - a reading older than the one a block shows is ignored: each rewrite is
//     tagged with the generation it was sent in (a generation per step that
//     took over, DictationBatchRewriter.Use), and a block only moves forward;
//   - a failed rewrite leaves the reading the block had, its children's
//     when it is a group that never had one;
//   - reading the document never blocks: one lock, held only to copy.
//
// Generation 0 is the raw text: a block nobody rewrote yet shows it.
public sealed class DictationDocument
{
    readonly object _gate = new();
    readonly List<Block> _blocks = new();
    string _open = "";
    int _nextIndex;

    // The blocks of the current cut, the one the last step that took over
    // rewrites.
    public int BlockCount
    {
        get { lock (_gate) return _blocks.Count; }
    }

    // Appends a passage the batcher closed and returns it as a block.
    internal Block Add(string text, bool endsOnPause)
    {
        lock (_gate)
        {
            var block = new Block(_nextIndex++, text, endsOnPause, []);
            _blocks.Add(block);
            return block;
        }
    }

    // The text after the last closed block, as said.
    internal void SetOpen(string text)
    {
        lock (_gate) _open = text;
    }

    // Cuts the closed text into blocks of at least blockChars: consecutive
    // blocks join until their raw text reaches the size, the way the batcher
    // closes a passage once it reaches it, and the seam stays where a block
    // already ended, at a pause or on the batcher's hard cap. What is left at
    // the end, shorter than the size, is a block too: it was closed, and the
    // step that takes over rewrites everything already closed at once. A
    // group of one is the block itself. Returns the new cut, every block of
    // which the caller sends.
    internal Block[] Regroup(int blockChars)
    {
        lock (_gate)
        {
            var cut = new List<Block>();
            var group = new List<Block>();
            int length = 0;
            foreach (Block block in _blocks)
            {
                if (group.Count > 0) length += Separator(group[^1]).Length;
                group.Add(block);
                length += block.Raw.Length;
                if (length < blockChars) continue;
                cut.Add(Group(group));
                group = new List<Block>();
                length = 0;
            }
            if (group.Count > 0) cut.Add(Group(group));

            _blocks.Clear();
            _blocks.AddRange(cut);
            return cut.ToArray();
        }

        Block Group(List<Block> group) =>
            group.Count == 1
                ? group[0]
                : new Block(_nextIndex++, Join(group, block => block.Raw), group[^1].EndsOnPause, group.ToArray());
    }

    // The blocks of the current cut, in order.
    internal Block[] Blocks()
    {
        lock (_gate) return _blocks.ToArray();
    }

    // Records a rewrite of block sent in generation. Returns false when it
    // changes nothing: a failure (null text) or a reading older than or as
    // old as the one the block has.
    internal bool Apply(Block block, int generation, string? text)
    {
        if (text is null) return false;
        lock (_gate)
        {
            if (generation <= block.Generation) return false;
            block.Reading = text;
            block.Generation = generation;
            return true;
        }
    }

    // The generation each block of the current cut has its own reading in, 0
    // for none (it shows its raw text, or its children's readings).
    internal int[] Generations()
    {
        lock (_gate) return _blocks.Select(block => block.Generation).ToArray();
    }

    // The take as it reads right now: each block's reading in order (its
    // children's, or its raw text, until a rewrite came back), then the open
    // text. A paragraph break after a block that ended on a pause, a space
    // otherwise, the way the take's raw text is joined.
    public string Snapshot()
    {
        lock (_gate)
        {
            string closed = Join(_blocks, Shown);
            if (_open.Length == 0) return closed;
            if (_blocks.Count == 0) return _open;
            return closed + Separator(_blocks[^1]) + _open;
        }

        static string Shown(Block block) =>
            block.Reading ?? (block.Children.Length > 0 ? Join(block.Children, Shown) : block.Raw);
    }

    static string Join(IReadOnlyList<Block> blocks, Func<Block, string> text)
    {
        var joined = new StringBuilder();
        for (int i = 0; i < blocks.Count; i++)
        {
            if (i > 0) joined.Append(Separator(blocks[i - 1]));
            joined.Append(text(blocks[i]));
        }
        return joined.ToString();
    }

    static string Separator(Block before) => before.EndsOnPause ? "\n\n" : " ";

    // One block. Index numbers the blocks of a take in the order they were
    // made, for the log; Raw, EndsOnPause and Children never change. Reading
    // and Generation are guarded by the document's lock.
    internal sealed class Block(int index, string raw, bool endsOnPause, Block[] children)
    {
        public int Index { get; } = index;
        public string Raw { get; } = raw;
        public bool EndsOnPause { get; } = endsOnPause;
        public Block[] Children { get; } = children;
        public string? Reading;
        public int Generation;
    }
}
