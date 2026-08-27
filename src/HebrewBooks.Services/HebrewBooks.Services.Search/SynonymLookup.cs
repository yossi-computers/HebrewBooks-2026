using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace HebrewBooks.Services.Search;

public sealed class SynonymLookup
{
	private const char Sep = '\u001f';

	private readonly string _dbPath;

	private readonly ILogger<SynonymLookup>? _log;

	private readonly object _gate = new object();

	private Dictionary<string, string[]>? _map;

	public int Count => Map.Count;

	private Dictionary<string, string[]> Map
	{
		get
		{
			if (_map != null)
			{
				return _map;
			}
			lock (_gate)
			{
				if (_map == null)
				{
					_map = Load();
				}
			}
			return _map;
		}
	}

	public SynonymLookup(string dbPath, ILogger<SynonymLookup>? log = null)
	{
		_dbPath = dbPath;
		_log = log;
	}

	public IReadOnlyList<string> Lookup(string query)
	{
		if (string.IsNullOrWhiteSpace(query))
		{
			return Array.Empty<string>();
		}
		Dictionary<string, string[]> map = Map;
		if (map.Count == 0)
		{
			return Array.Empty<string>();
		}
		Dictionary<string, string[]> dictionary = map;
		foreach (string item in HebrewSynonymNormalize.Candidates(query))
		{
			if (dictionary.TryGetValue(item, out var value))
			{
				return value;
			}
		}
		string[] array = HebrewSynonymNormalize.BaseKey(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (array.Length < 2)
		{
			return Array.Empty<string>();
		}
		for (int num = Math.Min(array.Length - 1, 12); num >= 2; num--)
		{
			for (int i = 0; i + num <= array.Length; i++)
			{
				foreach (string item2 in HebrewSynonymNormalize.Candidates(string.Join(' ', array, i, num)))
				{
					if (dictionary.TryGetValue(item2, out var value2))
					{
						return value2;
					}
				}
			}
		}
		string text = HebrewSynonymNormalize.BaseKey(query) + " ";
		string text2 = null;
		foreach (string key in dictionary.Keys)
		{
			if (key.Length > text.Length && key.StartsWith(text, StringComparison.Ordinal) && (text2 == null || key.Length < text2.Length))
			{
				text2 = key;
			}
		}
		if (text2 != null)
		{
			return dictionary[text2];
		}
		for (int j = 0; j < array.Length; j++)
		{
			foreach (string item3 in HebrewSynonymNormalize.Candidates(array[j]))
			{
				if (dictionary.TryGetValue(item3, out var value3))
				{
					return value3;
				}
			}
		}
		return Array.Empty<string>();
	}

	public IReadOnlyList<SynonymGroup> LookupGrouped(string query)
	{
		if (string.IsNullOrWhiteSpace(query))
		{
			return Array.Empty<SynonymGroup>();
		}
		Dictionary<string, string[]> map = Map;
		if (map.Count == 0)
		{
			return Array.Empty<SynonymGroup>();
		}
		List<SynonymGroup> list = new List<SynonymGroup>();
		string[] array = query.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
		int num = 0;
		while (num < array.Length)
		{
			bool flag = false;
			for (int num2 = Math.Min(array.Length - num, 5); num2 >= 1; num2--)
			{
				string text = string.Join(' ', array, num, num2);
				string item = MatchWindow(text, map).key;
				if (item != null)
				{
					list.Add(new SynonymGroup(text, map[item]));
					num += num2;
					flag = true;
					break;
				}
			}
			if (!flag)
			{
				num++;
			}
		}
		if (list.Count > 0)
		{
			return list;
		}
		string text2 = HebrewSynonymNormalize.BaseKey(query);
		if (text2.Length >= 2)
		{
			string text3 = text2 + " ";
			string text4 = null;
			foreach (string key in map.Keys)
			{
				if (key.Length > text3.Length && key.StartsWith(text3, StringComparison.Ordinal) && (text4 == null || key.Length < text4.Length))
				{
					text4 = key;
				}
			}
			if (text4 != null)
			{
				list.Add(new SynonymGroup(query.Trim(), map[text4]));
			}
		}
		return list;
	}

	private static (string? key, char prefix) MatchWindow(string rawWindow, Dictionary<string, string[]> map)
	{
		string text = HebrewSynonymNormalize.BaseKey(rawWindow);
		if (text.Length == 0)
		{
			return (key: null, prefix: '\0');
		}
		if (map.ContainsKey(text))
		{
			return (key: text, prefix: '\0');
		}
		char item = '\0';
		string text2 = null;
		(string, char)? tuple = HebrewSynonymNormalize.TryStripLeadingPrefix(text);
		if (tuple.HasValue)
		{
			(string, char) valueOrDefault = tuple.GetValueOrDefault();
			string item2 = valueOrDefault.Item1;
			char item3 = valueOrDefault.Item2;
			text2 = item2;
			item = item3;
			if (map.ContainsKey(item2))
			{
				return (key: item2, prefix: item3);
			}
		}
		if (text.IndexOf(' ') >= 0)
		{
			return (key: null, prefix: '\0');
		}
		(string, char)[] array = ((text2 != null) ? new(string, char)[2]
		{
			(text, '\0'),
			(text2, item)
		} : new(string, char)[1] { (text, '\0') });
		(string, char)[] array2 = array;
		for (int i = 0; i < array2.Length; i++)
		{
			var (text3, item4) = array2[i];
			foreach (string item6 in HebrewSpelling.Expand(text3))
			{
				if (item6 != text3 && map.ContainsKey(item6))
				{
					return (key: item6, prefix: item4);
				}
			}
		}
		array2 = array;
		for (int i = 0; i < array2.Length; i++)
		{
			var (text4, item5) = array2[i];
			foreach (string item7 in HebrewMorph.ExpandForQuery(text4))
			{
				if (item7 != text4 && map.ContainsKey(item7))
				{
					return (key: item7, prefix: item5);
				}
			}
		}
		return (key: null, prefix: '\0');
	}

	private Dictionary<string, string[]> Load()
	{
		Dictionary<string, string[]> dictionary = new Dictionary<string, string[]>(StringComparer.Ordinal);
		try
		{
			if (!File.Exists(_dbPath))
			{
				_log?.LogInformation("Synonym thesaurus not found at {Path}; chips disabled.", _dbPath);
				return dictionary;
			}
			using SqliteConnection sqliteConnection = new SqliteConnection(new SqliteConnectionStringBuilder
			{
				DataSource = _dbPath,
				Mode = SqliteOpenMode.ReadOnly
			}.ToString());
			sqliteConnection.Open();
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = "SELECT Key, Synonyms FROM Synonyms;";
			using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
			while (sqliteDataReader.Read())
			{
				string text = sqliteDataReader.GetString(0);
				string[] array = sqliteDataReader.GetString(1).Split('\u001f', StringSplitOptions.RemoveEmptyEntries);
				if (text.Length > 0 && array.Length != 0)
				{
					dictionary[text] = array;
				}
			}
			_log?.LogInformation("Loaded {Count} synonym entries from {Path}.", dictionary.Count, _dbPath);
		}
		catch (Exception exception)
		{
			_log?.LogWarning(exception, "Failed to load synonym thesaurus from {Path}; chips disabled.", _dbPath);
			dictionary.Clear();
		}
		return dictionary;
	}
}
