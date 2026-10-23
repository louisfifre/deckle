using Xunit;

// The observability tests listen to one process-wide provider; run in parallel, one
// class's events land in another class's listener and the assertions flake.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
