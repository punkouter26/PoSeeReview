using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = false)]
[assembly: TestFramework("PoSeeReview.Integration.TestAssemblyFixture", "PoSeeReview.Integration")]

namespace PoSeeReview.Integration;

public class TestAssemblyFixture : Xunit.Sdk.XunitTestFramework
{
    public TestAssemblyFixture(Xunit.Abstractions.IMessageSink messageSink)
        : base(messageSink)
    {
        // Set environment variable BEFORE any tests run to disable Serilog
        Environment.SetEnvironmentVariable("DISABLE_SERILOG", "true");
    }
}
