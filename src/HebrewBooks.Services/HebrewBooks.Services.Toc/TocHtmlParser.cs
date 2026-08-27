using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using HebrewBooks.Core.Models;

namespace HebrewBooks.Services.Toc;

public static class TocHtmlParser
{
	private static readonly Regex SelectPattern = new Regex("<select\\s+name=\"ctl00\\$cpMstr\\$ctl06\"[^>]*>(?<body>[\\s\\S]*?)</select>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static readonly Regex OptionPattern = new Regex("<option\\s+value=\"(?<page>\\d+)\">(?<title>[^<]*)</option>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	public static IReadOnlyList<TocEntry> Parse(string html)
	{
		if (string.IsNullOrEmpty(html))
		{
			return Array.Empty<TocEntry>();
		}
		Match match = SelectPattern.Match(html);
		if (!match.Success)
		{
			return Array.Empty<TocEntry>();
		}
		List<TocEntry> list = new List<TocEntry>();
		foreach (Match item in OptionPattern.Matches(match.Groups["body"].Value))
		{
			if (int.TryParse(item.Groups["page"].Value, out var result) && result > 0)
			{
				string text = CleanTitle(item.Groups["title"].Value);
				if (text.Length != 0)
				{
					list.Add(new TocEntry(text, result));
				}
			}
		}
		return list;
	}

	private static string CleanTitle(string raw)
	{
		if (string.IsNullOrEmpty(raw))
		{
			return string.Empty;
		}
		string text = ((raw.IndexOf('&') >= 0) ? WebUtility.HtmlDecode(raw) : raw);
		return ((text.IndexOf('\'') >= 0) ? text.Replace("''", "\"") : text).Trim();
	}
}
