namespace HebrewBooks.Diagnostics;

public sealed class DiagnosticResult
{
	public required string Id { get; init; }

	public required string Category { get; init; }

	public required string Title { get; init; }

	public DiagnosticSeverity Severity { get; init; }

	public string Detail { get; set; } = "";

	public string? Evidence { get; set; }

	public DiagnosticFix? Fix { get; init; }
}
