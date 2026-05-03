namespace Po.SeeReview.Shared.Dtos;

public class ConfigurationKeyStatusDto
{
    public string Key { get; set; } = string.Empty;
    public bool Exists { get; set; }
    public string? Value { get; set; }
}
