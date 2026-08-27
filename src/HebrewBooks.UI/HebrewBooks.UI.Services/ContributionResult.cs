namespace HebrewBooks.UI.Services;

public sealed record ContributionResult(bool Success, string Message, string? PullRequestUrl);
