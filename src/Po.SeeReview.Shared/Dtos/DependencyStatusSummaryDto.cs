namespace Po.SeeReview.Shared.Dtos;

public class DependencyStatusSummaryDto
{
    public string Overall { get; set; } = string.Empty;
    public List<DependencyCheckDto> Checks { get; set; } = new();
}
