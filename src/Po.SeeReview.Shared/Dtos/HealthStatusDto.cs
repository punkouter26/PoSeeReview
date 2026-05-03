namespace Po.SeeReview.Shared.Dtos;

public class HealthStatusDto
{
    public string Status { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public double DurationMilliseconds { get; set; }
    public List<HealthCheckStatusDto> Checks { get; set; } = new();
}
