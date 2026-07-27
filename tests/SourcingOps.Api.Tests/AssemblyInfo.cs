using Xunit;

// Each test class here spins up its own Testcontainers Postgres instance
// (ApiFactory). Running test classes in parallel was causing intermittent
// "password authentication failed" errors against the wrong/still-starting
// container under concurrent Docker container startup on this machine — force
// sequential execution across classes for reliability. Tests within a single
// class still share one container/fixture as designed.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
