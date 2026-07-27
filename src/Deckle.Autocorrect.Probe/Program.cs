using Deckle.Autocorrect.Probe;

ProbeArguments? parsed = ProbeArguments.Parse(args);
if (parsed is null)
{
    ProbeUsage.Print();
    return 2;
}

return parsed.Mode switch
{
    ProbeMode.Single => SingleProbeCommand.Run(parsed),
    ProbeMode.Benchmark => CorrectionBenchmarkCommand.Run(parsed),
    ProbeMode.AutocorrectBenchmark => AutocorrectBenchmarkCommand.Run(parsed),
    _ => 2,
};
