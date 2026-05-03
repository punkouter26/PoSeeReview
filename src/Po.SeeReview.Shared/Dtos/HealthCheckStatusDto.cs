namespace Po.SeeReview.Shared.Dtos;

public class HealthCheckStatusDto
{
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Description { get; set; }
    public double DurationMilliseconds { get; set; }
    public string? Exception { get; set; }
}
