using System.Text;
using Deckle.Input.Autocorrect.Cli.Commands;

// CLI host of the autocorrect module: the offline data pipeline (build-data,
// train-pairs, eval) and the live prototype (watch, inject, run, enroll, dict).
// Output is UTF-8 so accented forms and the report's box-drawing render
// correctly in any console.
Console.OutputEncoding = Encoding.UTF8;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

string command = args[0].ToLowerInvariant();
var rest = new CliArgs(args, 1);

return command switch
{
    "build-data" => BuildDataCommand.Run(rest),
    "train-pairs" => TrainPairsCommand.Run(rest),
    "eval" => EvalCommand.Run(rest),
    "mlm-probe" => MlmProbeCommand.Run(rest),
    "dry-run" => DryRunCommand.Run(rest),
    "watch" => WatchCommand.Run(rest),
    "harvest" => HarvestCommand.Run(rest),
    "inject" => InjectCommand.Run(rest),
    "run" => RunCommand.Run(rest),
    "enroll" => EnrollCommand.Run(rest),
    "dict" => DictCommand.Run(rest),
    "help" or "--help" or "-h" => Help(),
    _ => Unknown(command),
};

static int Help()
{
    PrintUsage();
    return 0;
}

static int Unknown(string command)
{
    Console.Error.WriteLine($"Unknown command: {command}");
    Console.Error.WriteLine();
    PrintUsage();
    return 1;
}

static void PrintUsage()
{
    Console.WriteLine("Deckle autocorrect CLI");
    Console.WriteLine();
    Console.WriteLine("Offline pipeline:");
    Console.WriteLine("  build-data  [--raw <dir>] [--out <dir>]");
    Console.WriteLine("              Build lexicon-fr / lexicon-en from the raw sources.");
    Console.WriteLine("  train-pairs [--corpus <file>] [--data <dir>]");
    Console.WriteLine("              Train the left-context pair model from the FR corpus.");
    Console.WriteLine("  eval        [--corpus <file>] [--data <dir>] [--no-context] [--no-en]");
    Console.WriteLine("              [--valid-forms] [--en-guard <ppm>] [--dominance <ratio>]");
    Console.WriteLine("              [--max-tokens <n>] [--reranker] [--rerank-margin <m>]");
    Console.WriteLine("              [--rerank-freq-prior <λ>] [--model <dir>]");
    Console.WriteLine("              Score diacritics restoration against the reference.");
    Console.WriteLine("              --reranker swaps the bigram for the post-sentence CamemBERT MLM.");
    Console.WriteLine("  mlm-probe   [--model <dir>] [--corpus <file>] [--n <per-class>] [--out <tsv>]");
    Console.WriteLine("              Probe a CamemBERT MLM on a balanced a/à set (reranker POC).");
    Console.WriteLine();
    Console.WriteLine("Dry-run (no injection):");
    Console.WriteLine("  dry-run     [text…] [--strip] [--show-ambiguous] [--model <dir>] [--data <dir>]");
    Console.WriteLine("              [--no-en] [--rerank-margin <m>] [--rerank-freq-prior <λ>]");
    Console.WriteLine("              [--en-guard <ppm>] [--dominance <ratio>]");
    Console.WriteLine("              Show what gate + reranker WOULD correct. Text runs once;");
    Console.WriteLine("              no text opens a REPL. --strip simulates a typist from accented");
    Console.WriteLine("              text; --show-ambiguous reveals slots left untouched on purpose.");
    Console.WriteLine();
    Console.WriteLine("Live prototype:");
    Console.WriteLine("  watch       Observe keyboard / focus — no correction, no injection.");
    Console.WriteLine("  harvest     [--data <dir>] | list | purge | path");
    Console.WriteLine("              Observe and persist the signal streams (edit pairs +");
    Console.WriteLine("              unknown words), DPAPI-encrypted. No correction, no injection.");
    Console.WriteLine("  inject <text> [--delay-ms 3000]");
    Console.WriteLine("              Type a literal string after a countdown.");
    Console.WriteLine("  run [--toy] [--data <dir>]");
    Console.WriteLine("              Run the live correction engine.");
    Console.WriteLine();
    Console.WriteLine("Maintenance:");
    Console.WriteLine("  enroll list | add <process> | remove <process>");
    Console.WriteLine("  dict   list | remove <word> | remove-suppression <orig> <repl> | purge | path");
}
