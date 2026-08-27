using System;
using System.IO;

namespace HebrewBooks.UI.Services;

internal static class UninstallCleanup
{
	public static string AppDataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HebrewBooks");

	public static string DeleteDataMarkerPath => Path.Combine(AppDataDir, ".uninstall-delete-data");

	public static bool IsLocalDeletableDataRoot(string? dataRoot)
	{
		if (string.IsNullOrWhiteSpace(dataRoot))
		{
			return false;
		}
		try
		{
			string fullPath = Path.GetFullPath(dataRoot);
			if (fullPath.StartsWith("\\\\", StringComparison.Ordinal))
			{
				return false;
			}
			string pathRoot = Path.GetPathRoot(fullPath);
			if (string.IsNullOrEmpty(pathRoot))
			{
				return false;
			}
			if (new DriveInfo(pathRoot).DriveType != DriveType.Fixed)
			{
				return false;
			}
			return File.Exists(Path.Combine(fullPath, "App", "Katalog.db"));
		}
		catch
		{
			return false;
		}
	}
}
