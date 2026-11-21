using Xunit;

// The observability tests listen to one process-wide provider; run in parallel, the
// events of a unit test land in another test's listener and the assertions flake.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
