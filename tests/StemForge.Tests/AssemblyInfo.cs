using Xunit;

// AppPaths resolves its data root from a process-wide environment variable, which the
// test fixtures set per test. Running test classes in parallel would let one fixture
// redirect another's paths (and could write into the real application data folder), so
// the suite runs sequentially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
