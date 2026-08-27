using System;
using System.IO;
using System.Text.Json;
using Serilog;

namespace HebrewBooks.UI.Navigation;

internal static class NavigationSessionStore
{
	private static readonly JsonSerializerOptions _opts = new JsonSerializerOptions
	{
		WriteIndented = false
	};

	public static NavigationHistorySnapshot? Load(string path)
	{
		if (App.IsProtectMode)
		{
			return null;
		}
		try
		{
			if (!File.Exists(path))
			{
				return null;
			}
			using FileStream fileStream = File.OpenRead(path);
			if (fileStream.Length == 0L)
			{
				return null;
			}
			return JsonSerializer.Deserialize<NavigationHistorySnapshot>(fileStream, _opts);
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "Navigation: session load failed at {Path} — starting with empty history", path);
			return null;
		}
	}

	public static void Save(NavigationHistorySnapshot snapshot, string path)
	{
		if (App.IsProtectMode)
		{
			return;
		}
		try
		{
			string directoryName = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			string text = path + ".tmp";
			using (FileStream utf8Json = File.Create(text))
			{
				JsonSerializer.Serialize(utf8Json, snapshot, _opts);
			}
			if (File.Exists(path))
			{
				File.Replace(text, path, null);
			}
			else
			{
				File.Move(text, path);
			}
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "Navigation: session save failed at {Path}", path);
		}
	}
}
