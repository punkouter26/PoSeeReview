namespace PoSeeReview.Shared.Dtos;

public class DevSessionDto
{
    public string UserId { get; set; } = string.Empty;
    public string? Email { get; set; }
    public bool IsAnonymous { get; set; }
    public bool IsDevelopmentBypass { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
