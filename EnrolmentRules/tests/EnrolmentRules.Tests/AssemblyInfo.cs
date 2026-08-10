// Test classes run in parallel. Safe because the shipped engine is built once and is read-only
// after build (see Harness.BuildFromShippedWorkflows), and every test isolates its filesystem
// state under a Guid-stamped temp directory and routes CLI output through its own StringWriter
// rather than the global Console. The advisor-search tests dominate wall-clock, so overlapping them
// with the rest is the win.

[assembly: CollectionBehavior(DisableTestParallelization = false)]
