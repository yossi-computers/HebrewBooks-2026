namespace HebrewBooks.Services.TextLayer;

public sealed record RepairProgress(string Stage, int Current, int Total, string? LastLogLine);
