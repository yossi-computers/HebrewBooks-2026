using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using HebrewBooks.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace HebrewBooks.Services.TextLayer;

public sealed class TextlayerStatusStore
{
	private readonly string _path;

	private readonly object _lock = new object();

	private readonly ILogger<TextlayerStatusStore>? _log;

	private Dictionary<int, TextlayerStatus>? _byFileId;

	private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
	{
		WriteIndented = true,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	public TextlayerStatusStore(IPathResolver paths, ILogger<TextlayerStatusStore>? log = null)
	{
		_path = Path.Combine(paths.UserDataRoot, "textlayer-status.json");
		_log = log;
	}

	public TextlayerStatus? Get(int fileId)
	{
		EnsureLoaded();
		lock (_lock)
		{
			TextlayerStatus value;
			return _byFileId.TryGetValue(fileId, out value) ? value : null;
		}
	}

	public IReadOnlyDictionary<int, TextlayerStatus> Snapshot()
	{
		EnsureLoaded();
		lock (_lock)
		{
			return new Dictionary<int, TextlayerStatus>(_byFileId);
		}
	}

	public void Set(TextlayerStatus status)
	{
		EnsureLoaded();
		lock (_lock)
		{
			_byFileId[status.FileId] = status;
			Save();
		}
	}

	public bool Remove(int fileId)
	{
		EnsureLoaded();
		lock (_lock)
		{
			if (!_byFileId.Remove(fileId))
			{
				return false;
			}
			Save();
			return true;
		}
	}

	private void EnsureLoaded()
	{
		if (_byFileId != null)
		{
			return;
		}
		lock (_lock)
		{
			if (_byFileId == null)
			{
				_byFileId = Load();
			}
		}
	}

	private Dictionary<int, TextlayerStatus> Load()
	{
		if (!File.Exists(_path))
		{
			return new Dictionary<int, TextlayerStatus>();
		}
		try
		{
			return (from s in JsonSerializer.Deserialize<List<TextlayerStatus>>(File.ReadAllText(_path), Json) ?? new List<TextlayerStatus>()
				group s by s.FileId).ToDictionary((IGrouping<int, TextlayerStatus> g) => g.Key, (IGrouping<int, TextlayerStatus> g) => g.First());
		}
		catch (Exception exception)
		{
			_log?.LogWarning(exception, "TextlayerStatusStore: failed to load {Path}; starting empty", _path);
			return new Dictionary<int, TextlayerStatus>();
		}
	}

	private void Save()
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(_path));
			string contents = JsonSerializer.Serialize(_byFileId.Values.OrderBy((TextlayerStatus s) => s.FileId).ToList(), Json);
			string text = _path + ".tmp";
			File.WriteAllText(text, contents);
			if (File.Exists(_path))
			{
				File.Delete(_path);
			}
			File.Move(text, _path);
		}
		catch (Exception exception)
		{
			_log?.LogError(exception, "TextlayerStatusStore: failed to save {Path}", _path);
		}
	}

	public static string ComputeSha256(string path)
	{
		using FileStream inputStream = File.OpenRead(path);
		using SHA256 sHA = SHA256.Create();
		return Convert.ToHexString(sHA.ComputeHash(inputStream)).ToLowerInvariant();
	}
}
