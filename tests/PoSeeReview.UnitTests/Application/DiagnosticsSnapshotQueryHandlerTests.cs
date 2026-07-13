using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PoSeeReview.Application.Abstractions;
using PoSeeReview.Application.Diagnostics;

namespace PoSeeReview.UnitTests.Application;

public class DiagnosticsSnapshotQueryHandlerTests
{
    private readonly Mock<ICurrentRequestIdentityAccessor> _identityAccessorMock = new();
    private readonly Mock<HealthCheckService> _healthCheckServiceMock = new();

    private DiagnosticsSnapshotQueryHandler BuildSut(IConfiguration? config = null)
    {
        config ??= new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Development",
                ["ApplicationInsights:ConnectionString"] = "InstrumentationKey=test-key"
            })
            .Build();

        return new DiagnosticsSnapshotQueryHandler(
            config,
            _healthCheckServiceMock.Object,
            _identityAccessorMock.Object,
            Mock.Of<IHostEnvironment>(e => e.EnvironmentName == Environments.Development),
            NullLogger<DiagnosticsSnapshotQueryHandler>.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsSnapshotWithEnvironment()
    {
        SetupHealthOk();
        _identityAccessorMock.Setup(x => x.GetCurrentUserId()).Returns("ANON123456");

        var sut = BuildSut();
        var result = await sut.ExecuteAsync(default);

        Assert.Equal("Development", result.Environment);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsCurrentUserId()
    {
        SetupHealthOk();
        _identityAccessorMock.Setup(x => x.GetCurrentUserId()).Returns("ANON654321");

        var sut = BuildSut();
        var result = await sut.ExecuteAsync(default);

        Assert.Equal("ANON654321", result.CurrentUserId);
    }

    [Fact]
    public async Task ExecuteAsync_MasksKnownSensitiveKeyValues()
    {
        SetupHealthOk();
        _identityAccessorMock.Setup(x => x.GetCurrentUserId()).Returns(default(string)!);

        var sut = BuildSut();
        var result = await sut.ExecuteAsync(default);

        // ApplicationInsights:ConnectionString is in keyStatus list — should be masked
        var appInsightsKey = result.KeyStatus?.FirstOrDefault(k => k.Key == "ApplicationInsights:ConnectionString");
        Assert.NotNull(appInsightsKey);
        Assert.True(appInsightsKey.Exists);
        Assert.NotEqual("InstrumentationKey=test-key", appInsightsKey.Value);
        Assert.Contains("***", appInsightsKey.Value ?? string.Empty);
    }

    [Fact]
    public async Task ExecuteAsync_WhenHealthCheckThrows_DependencyStatusIsNull()
    {
        _healthCheckServiceMock
            .Setup(x => x.CheckHealthAsync(It.IsAny<Func<HealthCheckRegistration, bool>?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("health check failed"));
        _identityAccessorMock.Setup(x => x.GetCurrentUserId()).Returns(default(string)!);

        var sut = BuildSut();
        var result = await sut.ExecuteAsync(default);

        Assert.Null(result.DependencyStatus);
    }

    [Fact]
    public async Task ExecuteAsync_TimestampIsRecent()
    {
        SetupHealthOk();
        _identityAccessorMock.Setup(x => x.GetCurrentUserId()).Returns(default(string)!);

        var before = DateTime.UtcNow.AddSeconds(-1);
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(default);
        var after = DateTime.UtcNow.AddSeconds(1);

        Assert.InRange(result.Timestamp, before, after);
    }

    [Fact]
    public async Task ExecuteAsync_MachineNameAndProcessId_ArePopulated()
    {
        SetupHealthOk();
        _identityAccessorMock.Setup(x => x.GetCurrentUserId()).Returns(default(string)!);

        var sut = BuildSut();
        var result = await sut.ExecuteAsync(default);

        Assert.NotEmpty(result.MachineName ?? string.Empty);
        Assert.True(result.ProcessId > 0);
    }

    private void SetupHealthOk()
    {
        var report = new HealthReport(
            new Dictionary<string, HealthReportEntry>(),
            TimeSpan.FromMilliseconds(1));
        _healthCheckServiceMock
            .Setup(x => x.CheckHealthAsync(It.IsAny<Func<HealthCheckRegistration, bool>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(report);
    }
}
