using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace HebrewBooks.Infrastructure.Settings;

public sealed class IniMigration(JsonSettingsStore store)
{
	private sealed class IniDocument
	{
		private readonly Dictionary<string, Dictionary<string, string>> _data = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

		public void Set(string section, string key, string value)
		{
			if (!_data.TryGetValue(section, out Dictionary<string, string> value2))
			{
				value2 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				_data[section] = value2;
			}
			value2[key] = value;
		}

		public bool TryGet(string section, string key, out string value)
		{
			value = "";
			if (!_data.TryGetValue(section, out Dictionary<string, string> value2))
			{
				return false;
			}
			return value2.TryGetValue(key, out value);
		}
	}

	public bool MigrateIfNeeded(string? legacyIniPath = null)
	{
		if (store.Exists)
		{
			return false;
		}
		string path = legacyIniPath ?? Path.Combine(AppContext.BaseDirectory, "Bookshelf.INI");
		if (!File.Exists(path))
		{
			return false;
		}
		IniDocument iniDocument = ParseIni(path);
		BookshelfOptions bookshelfOptions = new BookshelfOptions();
		if (iniDocument.TryGet("Search", "TrimSQL", out string value))
		{
			bookshelfOptions.Search.TrimQuery = ToBool(value);
		}
		if (iniDocument.TryGet("Search", "SQlOption", out string value2))
		{
			bookshelfOptions.Search.SortMode = ToInt(value2, bookshelfOptions.Search.SortMode);
		}
		if (iniDocument.TryGet("Search", "RunSearch", out string value3))
		{
			bookshelfOptions.Search.RunOnStartup = ToBool(value3);
		}
		if (iniDocument.TryGet("Search", "MaxMerchak", out string value4))
		{
			bookshelfOptions.Search.MaxProximity = ToInt(value4, bookshelfOptions.Search.MaxProximity);
		}
		if (iniDocument.TryGet("Search", "Hybur", out string value5))
		{
			bookshelfOptions.Search.Hybur = ToBool(value5);
		}
		if (iniDocument.TryGet("Search", "ResizeFont", out string value6))
		{
			bookshelfOptions.Search.ResizeFont = ToBool(value6);
		}
		if (iniDocument.TryGet("Search", "Number", out string value7))
		{
			bookshelfOptions.Search.IncludeNumbers = ToBool(value7);
		}
		if (iniDocument.TryGet("Search", "Seder", out string value8))
		{
			bookshelfOptions.Search.SortBySeder = ToBool(value8);
		}
		if (iniDocument.TryGet("Search", "MaxFilesToRetrieve", out string value9))
		{
			bookshelfOptions.Search.MaxFilesToRetrieve = ToInt(value9, bookshelfOptions.Search.MaxFilesToRetrieve);
		}
		if (iniDocument.TryGet("Search", "OpenRT", out string value10))
		{
			bookshelfOptions.Search.OpenRichTextOnSearch = ToBool(value10);
		}
		if (iniDocument.TryGet("General", "QwerSave", out string value11))
		{
			bookshelfOptions.Search.QuickSave = ToBool(value11);
		}
		if (iniDocument.TryGet("General", "ShowListResults", out string value12))
		{
			bookshelfOptions.Search.ShowResultsList = ToBool(value12);
		}
		if (iniDocument.TryGet("General", "Pin", out string value13))
		{
			bookshelfOptions.View.PinResultList = ToBool(value13);
		}
		if (iniDocument.TryGet("General", "CountScrool", out string value14))
		{
			bookshelfOptions.View.CountScroll = ToInt(value14, bookshelfOptions.View.CountScroll);
		}
		if (iniDocument.TryGet("General", "IndexAutoCommitIntervalMB", out string value15))
		{
			bookshelfOptions.Indexing.AutoCommitIntervalMb = ToInt(value15, bookshelfOptions.Indexing.AutoCommitIntervalMb);
		}
		if (iniDocument.TryGet("View", "ManualResize", out string value16))
		{
			bookshelfOptions.View.ManualResize = ToInt(value16, bookshelfOptions.View.ManualResize);
		}
		if (iniDocument.TryGet("View", "ExplorerBarWidth", out string value17))
		{
			bookshelfOptions.View.ExplorerBarWidth = ToInt(value17, bookshelfOptions.View.ExplorerBarWidth);
		}
		if (iniDocument.TryGet("View", "PercentSplitBarInLeft", out string value18))
		{
			bookshelfOptions.View.PercentSplitBarInLeft = ToDouble(value18, bookshelfOptions.View.PercentSplitBarInLeft);
		}
		store.Save(bookshelfOptions);
		return true;
	}

	private static IniDocument ParseIni(string path)
	{
		IniDocument iniDocument = new IniDocument();
		string section = "";
		string[] array = File.ReadAllLines(path);
		for (int i = 0; i < array.Length; i++)
		{
			string text = array[i].Trim();
			if (text.Length == 0 || text.StartsWith(';') || text.StartsWith('#'))
			{
				continue;
			}
			if (text.StartsWith('[') && text.EndsWith(']'))
			{
				string text2 = text;
				section = text2.Substring(1, text2.Length - 1 - 1).Trim();
				continue;
			}
			int num = text.IndexOf('=');
			if (num > 0)
			{
				string key = text.Substring(0, num).Trim();
				string text2 = text;
				int num2 = num + 1;
				string value = text2.Substring(num2, text2.Length - num2).Trim();
				iniDocument.Set(section, key, value);
			}
		}
		return iniDocument;
	}

	private static bool ToBool(string s)
	{
		if (!s.Equals("True", StringComparison.OrdinalIgnoreCase) && !(s == "1"))
		{
			return s.Equals("yes", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static int ToInt(string s, int fallback)
	{
		if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
		{
			return fallback;
		}
		return result;
	}

	private static double ToDouble(string s, double fallback)
	{
		if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
		{
			return fallback;
		}
		return result;
	}
}
