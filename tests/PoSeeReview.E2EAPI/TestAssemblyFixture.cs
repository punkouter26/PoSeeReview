using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = false)]
[assembly: TestFramework("PoSeeReview.E2EAPI.TestAssemblyFixture", "PoSeeReview.E2EAPI")]

namespace PoSeeReview.E2EAPI;

public class TestAssemblyFixture : Xunit.Sdk.XunitTestFramework
{
    public TestAssemblyFixture(Xunit.Abstractions.IMessageSink messageSink)
        : base(messageSink)
    {
        // Set environment variable BEFORE any tests run to disable Serilog
        Environment.SetEnvironmentVariable("DISABLE_SERILOG", "true");
    }
}
