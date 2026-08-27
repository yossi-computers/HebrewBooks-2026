using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Serilog;

namespace HebrewBooks.UI.Services;

public static class OpenTabsPersistence
{
	public sealed record SavedTab(string FileId, bool Pinned, int Page = 0, string? Query = null);

	public sealed record SavedTabs(IReadOnlyList<SavedTab> Tabs, string? ActiveFileId);

	public sealed record SavedSession(IReadOnlyList<SavedTabs> Windows);

	private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
	{
		WriteIndented = true
	};

	private static string Dir => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HebrewBooks");

	private static string Path => System.IO.Path.Combine(Dir, "open-tabs.json");

	private static string SessionPath => System.IO.Path.Combine(Dir, "open-windows.json");

	public static void Save(SavedTabs data)
	{
		try
		{
			string path = Path;
			Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
			string text = path + ".tmp";
			File.WriteAllText(text, JsonSerializer.Serialize(data, JsonOpts));
			File.Move(text, path, overwrite: true);
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "OpenTabsPersistence.Save failed");
		}
	}

	public static SavedTabs? Load()
	{
		try
		{
			string path = Path;
			if (!File.Exists(path))
			{
				return null;
			}
			SavedTabs savedTabs = JsonSerializer.Deserialize<SavedTabs>(File.ReadAllText(path));
			IReadOnlyList<SavedTab> readOnlyList = savedTabs?.Tabs;
			return (readOnlyList != null && readOnlyList.Count > 0) ? savedTabs : null;
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "OpenTabsPersistence.Load failed");
			return null;
		}
	}

	public static void Clear()
	{
		try
		{
			if (File.Exists(Path))
			{
				File.Delete(Path);
			}
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "OpenTabsPersistence.Clear failed");
		}
	}

	public static void SaveSession(SavedSession data)
	{
		try
		{
			Directory.CreateDirectory(Dir);
			string text = SessionPath + ".tmp";
			File.WriteAllText(text, JsonSerializer.Serialize(data, JsonOpts));
			File.Move(text, SessionPath, overwrite: true);
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "OpenTabsPersistence.SaveSession failed");
		}
	}

	public static SavedSession? LoadSession()
	{
		try
		{
			if (File.Exists(SessionPath))
			{
				List<SavedTabs> list = JsonSerializer.Deserialize<SavedSession>(File.ReadAllText(SessionPath))?.Windows?.Where(delegate(SavedTabs w)
				{
					IReadOnlyList<SavedTab> tabs = w.Tabs;
					return tabs != null && tabs.Count > 0;
				}).ToList();
				if (list != null && list.Count > 0)
				{
					return new SavedSession(list);
				}
				return null;
			}
			SavedTabs savedTabs = Load();
			return ((object)savedTabs == null) ? null : new SavedSession(new SavedTabs[1] { savedTabs });
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "OpenTabsPersistence.LoadSession failed");
			return null;
		}
	}
}
