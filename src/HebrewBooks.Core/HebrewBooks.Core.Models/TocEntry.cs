using System.Text.Json.Serialization;

namespace HebrewBooks.Core.Models;

public sealed record TocEntry([property: JsonPropertyName("Title")] string Title, [property: JsonPropertyName("Page")] int Page, [property: JsonPropertyName("Level"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int Level = 0);
