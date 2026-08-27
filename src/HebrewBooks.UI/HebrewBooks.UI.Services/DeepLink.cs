using System;
using System.Collections.Generic;
using System.Linq;

namespace HebrewBooks.UI.Services;

public static class DeepLink
{
	public const string Scheme = "hebrewbooks";

	private const string Prefix = "hebrewbooks://";

	public static string? FindUri(IEnumerable<string> args)
	{
		return args.FirstOrDefault((string a) => !string.IsNullOrEmpty(a) && a.StartsWith("hebrewbooks://", StringComparison.OrdinalIgnoreCase));
	}

	public static bool TryParse(string? uri, out int fileId, out int page)
	{
		fileId = 0;
		page = 0;
		if (string.IsNullOrWhiteSpace(uri))
		{
			return false;
		}
		if (!uri.StartsWith("hebrewbooks://", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		string[] array = uri.Substring("hebrewbooks://".Length).Trim().Trim('/')
			.Split('/', StringSplitOptions.RemoveEmptyEntries);
		if (array.Length < 2 || !array[0].Equals("book", StringComparison.OrdinalIgnoreCase) || !int.TryParse(array[1], out fileId) || fileId <= 0)
		{
			fileId = 0;
			return false;
		}
		if (array.Length >= 4 && array[2].Equals("page", StringComparison.OrdinalIgnoreCase))
		{
			int.TryParse(array[3], out page);
		}
		if (page < 0)
		{
			page = 0;
		}
		return true;
	}

	public static string Build(int fileId, int page)
	{
		if (page > 0)
		{
			return $"{"hebrewbooks://"}book/{fileId}/page/{page}";
		}
		return $"{"hebrewbooks://"}book/{fileId}";
	}

	public static string? TryParseSearch(string? uri)
	{
		if (string.IsNullOrWhiteSpace(uri))
		{
			return null;
		}
		if (!uri.StartsWith("hebrewbooks://", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		string text = uri.Substring("hebrewbooks://".Length).TrimStart('/');
		int num = text.IndexOf('/');
		if (num < 0 || !text.Substring(0, num).Equals("search", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		string text2 = text.Substring(num + 1).Trim();
		if (text2.Length == 0)
		{
			return null;
		}
		string text3;
		try
		{
			text3 = Uri.UnescapeDataString(text2).Trim();
		}
		catch
		{
			text3 = text2;
		}
		if (!string.IsNullOrWhiteSpace(text3))
		{
			return text3;
		}
		return null;
	}

	public static string BuildSearch(string query)
	{
		return "hebrewbooks://search/" + Uri.EscapeDataString(query.Trim());
	}
}
