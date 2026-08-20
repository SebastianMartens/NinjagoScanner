namespace NinjagoScanner.Web.Shared.Models;

public sealed class UpdateReviewStatusRequestDto
{
    public required string ReviewStatus { get; init; }
}

public sealed class UpdateSetNameRequestDto
{
    public string? SetName { get; init; }
}

public sealed class UpdateCardNumberRequestDto
{
    public string? CardNumber { get; init; }
}

public sealed class UpdateCardLanguageRequestDto
{
    public string? Language { get; init; }
}
