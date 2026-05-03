namespace Po.SeeReview.Shared.Dtos;

public class DiagnosticsSnapshotDto
{
    public DateTime Timestamp { get; set; }
    public string Environment { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public string OsVersion { get; set; } = string.Empty;
    public string DotNetVersion { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public string CurrentUserId { get; set; } = string.Empty;
    public DependencyStatusSummaryDto? DependencyStatus { get; set; }
    public List<ConfigurationKeyStatusDto> KeyStatus { get; set; } = new();
    public List<ConfigurationValueDto> Config { get; set; } = new();
}
