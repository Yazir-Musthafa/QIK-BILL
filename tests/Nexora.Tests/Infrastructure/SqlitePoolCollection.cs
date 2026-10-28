namespace Nexora.Tests.Infrastructure;

// Restore and pool-stress tests intentionally clear process-wide SQLite pools. They must not overlap
// with unrelated tests' transactions. Their own explicit concurrency/stress checks still run.
[CollectionDefinition("SQLite pool lifecycle", DisableParallelization = true)]
public sealed class SqlitePoolCollection;
