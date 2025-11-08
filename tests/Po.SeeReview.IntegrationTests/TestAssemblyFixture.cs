using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = false)]
[assembly: TestFramework("Po.SeeReview.IntegrationTests.TestAssemblyFixture", "Po.SeeReview.IntegrationTests")]

namespace Po.SeeReview.IntegrationTests;

public class TestAssemblyFixture : Xunit.Sdk.XunitTestFramework
{
    public TestAssemblyFixture(Xunit.Abstractions.IMessageSink messageSink)
        : base(messageSink)
    {
        // Set environment variable BEFORE any tests run to disable Serilog
        Environment.SetEnvironmentVariable("DISABLE_SERILOG", "true");
    }
}
