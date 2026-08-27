using System.Text.Json.Serialization;

namespace HebrewBooks.Search.Incremental;

public sealed class ManifestEntry
{
	[JsonPropertyName("size")]
	public long Size { get; set; }

	[JsonPropertyName("mtime")]
	public long Mtime { get; set; }

	[JsonPropertyName("indexedPath")]
	public string IndexedPath { get; set; } = "";
}
