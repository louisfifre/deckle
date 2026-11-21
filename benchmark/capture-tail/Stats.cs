namespace Deckle.Benchmark.CaptureTail;

internal static class Stats
{
    public static double Percentile(IReadOnlyCollection<double> values, double p)
    {
        if (values.Count == 0) return double.NaN;
        double[] a = values.Order().ToArray();
        double r = (a.Length - 1) * p / 100;
        int lo = (int)Math.Floor(r), hi = (int)Math.Ceiling(r);
        return a[lo] + (a[hi] - a[lo]) * (r - lo);
    }

    public static string Share(int count, int total) =>
        total == 0 ? "0/0" : $"{count}/{total} ({100.0 * count / total:F1}%)";

    // Every line this tool prints goes through here, prefixed, so a run's
    // stdout can be filtered down to the aggregates it chose to print.
    public static void Result(string line) => Console.WriteLine("RESULT " + line);
}
