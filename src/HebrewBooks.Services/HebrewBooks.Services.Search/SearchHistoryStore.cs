using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HebrewBooks.Core.Abstractions;

namespace HebrewBooks.Services.Search;

public sealed class SearchHistoryStore
{
	private const int MaxEntries = 50;

	private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
	{
		WriteIndented = false
	};

	private readonly string _path;

	private readonly object _lock = new object();

	private List<string> _entries = new List<string>();

	private readonly IProtectMode? _protect;

	public IReadOnlyList<string> Recent
	{
		get
		{
			lock (_lock)
			{
				return _entries.ToList();
			}
		}
	}

	public SearchHistoryStore(IProtectMode? protectMode = null)
	{
		_protect = protectMode;
		string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HebrewBooks");
		_path = Path.Combine(path, "search-history.json");
		Load();
	}

	public void Add(string query)
	{
		if (string.IsNullOrWhiteSpace(query))
		{
			return;
		}
		string q = query.Trim();
		lock (_lock)
		{
			_entries.RemoveAll((string s) => string.Equals(s, q, StringComparison.Ordinal));
			_entries.Insert(0, q);
			if (_entries.Count > 50)
			{
				_entries.RemoveRange(50, _entries.Count - 50);
			}
		}
		Save();
	}

	public void Clear()
	{
		lock (_lock)
		{
			_entries.Clear();
		}
		Save();
	}

	private void Load()
	{
		IProtectMode? protect = _protect;
		if (protect != null && protect.IsActive)
		{
			return;
		}
		try
		{
			if (File.Exists(_path))
			{
				List<string> list = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_path));
				if (list != null)
				{
					_entries = list;
				}
			}
		}
		catch
		{
		}
	}

	private void Save()
	{
		IProtectMode? protect = _protect;
		if (protect != null && protect.IsActive)
		{
			return;
		}
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(_path));
			string contents;
			lock (_lock)
			{
				contents = JsonSerializer.Serialize(_entries, JsonOpts);
			}
			string text = _path + ".tmp";
			File.WriteAllText(text, contents);
			File.Move(text, _path, overwrite: true);
		}
		catch
		{
		}
	}
}
