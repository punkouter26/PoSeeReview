namespace PoSeeReview.Shared.Dtos;

public class DependencyCheckDto
{
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Description { get; set; }
    public double DurationMilliseconds { get; set; }
    public string? Error { get; set; }
}
