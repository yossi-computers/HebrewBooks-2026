namespace HebrewBooks.Core.Models;

public sealed record Book
{
	public int ID { get; init; }

	public string? FileID { get; init; }

	public string? BookName { get; init; }

	public string? AuthorName { get; init; }

	public string? PrintPlace { get; init; }

	public string? PrintYear { get; init; }

	public int? CountPage { get; init; }

	public string? Description { get; init; }

	public string? Folder { get; init; }

	public string? Categories { get; init; }

	public bool Searchable { get; init; } = true;

	public string SourceType { get; init; } = "PDF";

	public string? RelativePath { get; init; }
}
