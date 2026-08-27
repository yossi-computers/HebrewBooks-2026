using System;
using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace HebrewBooks.Core.Models;

public static class TocSerializer
{
	private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
	{
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
		WriteIndented = false
	};

	public static IReadOnlyList<TocEntry> Parse(string? json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return Array.Empty<TocEntry>();
		}
		try
		{
			IReadOnlyList<TocEntry> readOnlyList = JsonSerializer.Deserialize<List<TocEntry>>(json, JsonOpts);
			return readOnlyList ?? Array.Empty<TocEntry>();
		}
		catch (JsonException)
		{
			return Array.Empty<TocEntry>();
		}
	}

	public static string? Serialize(IReadOnlyList<TocEntry>? entries)
	{
		if (entries == null || entries.Count == 0)
		{
			return null;
		}
		return JsonSerializer.Serialize(entries, JsonOpts);
	}
}
