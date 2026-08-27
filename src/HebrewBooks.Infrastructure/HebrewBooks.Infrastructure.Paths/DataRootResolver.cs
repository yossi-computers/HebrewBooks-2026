using System;
using System.Collections.Generic;
using System.IO;
using HebrewBooks.Infrastructure.Settings;
using Microsoft.Extensions.Logging;

namespace HebrewBooks.Infrastructure.Paths;

public sealed class DataRootResolver(JsonSettingsStore settingsStore, ILogger<DataRootResolver>? logger = null)
{
	public const string EnvVar = "HEBREWBOOKS_DATA";

	public const string MarkerRelative = "App\\Katalog.db";

	private const int PerDriveTimeoutMs = 5000;

	private DriveInfo[] SafeGetDrives()
	{
		try
		{
			using (DriveProbe.EnterFailFast())
			{
				return DriveInfo.GetDrives();
			}
		}
		catch (Exception exception)
		{
			logger?.LogWarning(exception, "DataRoot: DriveInfo.GetDrives() failed");
			return Array.Empty<DriveInfo>();
		}
	}

	public string Resolve(string[]? args = null, Func<string?>? firstRunPrompter = null)
	{
		BookshelfOptions bookshelfOptions = settingsStore.Load();
		bool forceRescan = bookshelfOptions.Paths.ForceRescan;
		if (TryValidate(ParseArgRoot(args), out string root))
		{
			return PersistSerial(root);
		}
		if (TryValidate(Environment.GetEnvironmentVariable("HEBREWBOOKS_DATA"), out string root2))
		{
			return PersistSerial(root2);
		}
		if (!forceRescan && bookshelfOptions.Paths.DataVolumeSerial != 0 && TryFindByVolumeSerial(bookshelfOptions.Paths.DataVolumeSerial, bookshelfOptions.Paths.DataSubdir, out string root3))
		{
			return root3;
		}
		if (!forceRescan && DataRootIsValid(bookshelfOptions.Paths.LastDataRootPath, out string full))
		{
			return PersistSerial(full);
		}
		if (!forceRescan && bookshelfOptions.NetworkInstall && TryAcceptLocalNetworkRoot(bookshelfOptions.Paths.LastDataRootPath, out string full2))
		{
			return PersistSerial(full2);
		}
		if (TryMarkerScan(bookshelfOptions.Paths.DataSubdir, forceRescan, out string root4))
		{
			return PersistSerial(root4);
		}
		if (firstRunPrompter != null && TryValidate(firstRunPrompter(), out string root5))
		{
			return PersistSerial(root5);
		}
		throw new DataRootNotFoundException("Could not locate the HebrewBooks data root. Connect the HebrewBooks USB drive, set the HEBREWBOOKS_DATA environment variable, or pass --data-root <path>.");
	}

	public void PersistRoot(string root)
	{
		PersistSerial(root);
	}

	public string? FindConnectedLibraryDrive(string? excludeRoot)
	{
		string text = null;
		try
		{
			text = Path.GetPathRoot(excludeRoot);
		}
		catch
		{
		}
		using (DriveProbe.EnterFailFast())
		{
			DriveInfo[] array = SafeGetDrives();
			foreach (DriveInfo driveInfo in array)
			{
				if (!driveInfo.IsReady || driveInfo.DriveType != DriveType.Removable)
				{
					continue;
				}
				string fullName = driveInfo.RootDirectory.FullName;
				if (text != null && string.Equals(fullName, text, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				if (HasMarker(fullName))
				{
					return Path.GetFullPath(fullName);
				}
				foreach (string item in SafeSubdirs(fullName))
				{
					if (HasMarker(item))
					{
						return Path.GetFullPath(item);
					}
					foreach (string item2 in SafeSubdirs(item))
					{
						if (HasMarker(item2))
						{
							return Path.GetFullPath(item2);
						}
					}
				}
			}
			return null;
		}
	}

	private static bool HasMarker(string dir)
	{
		try
		{
			return File.Exists(Path.Combine(dir, "App\\Katalog.db"));
		}
		catch
		{
			return false;
		}
	}

	private static IEnumerable<string> SafeSubdirs(string dir)
	{
		try
		{
			return Directory.EnumerateDirectories(dir);
		}
		catch
		{
			return Array.Empty<string>();
		}
	}

	private string PersistSerial(string root)
	{
		uint serial = 0u;
		string volumeRoot;
		bool hasSerial = TryGetVolumeRoot(root, out volumeRoot) && VolumeProbe.TryGetSerial(volumeRoot, out serial);
		settingsStore.Update(delegate(BookshelfOptions o)
		{
			if (hasSerial)
			{
				o.Paths.DataVolumeSerial = serial;
			}
			o.Paths.LastDataRootPath = root;
			o.Paths.ForceRescan = false;
		});
		return root;
	}

	private bool TryFindByVolumeSerial(uint targetSerial, string subdir, out string root)
	{
		root = "";
		DriveInfo[] array = SafeGetDrives();
		foreach (DriveInfo drive in array)
		{
			string name = drive.Name;
			string text = DriveProbe.RunWithTimeout(delegate
			{
				if (!IsCandidateDrive(drive))
				{
					return (string?)null;
				}
				if (!VolumeProbe.TryGetSerial(drive.RootDirectory.FullName, out var serial))
				{
					return (string?)null;
				}
				if (serial != targetSerial)
				{
					return (string?)null;
				}
				string text2 = (string.IsNullOrEmpty(subdir) ? drive.RootDirectory.FullName : Path.Combine(drive.RootDirectory.FullName, subdir));
				if (!Directory.Exists(text2))
				{
					return (string?)null;
				}
				return (!File.Exists(Path.Combine(text2, "App\\Katalog.db"))) ? null : Path.GetFullPath(text2);
			}, null, 5000, "serial-match " + name, logger);
			if (text != null)
			{
				logger?.LogInformation("DataRoot: matched saved volume serial on {Drive} → {Root}", name, text);
				root = text;
				return true;
			}
		}
		return false;
	}

	private bool TryMarkerScan(string subdir, bool preferRemovable, out string root)
	{
		root = "";
		DriveInfo[] array = SafeGetDrives();
		if (preferRemovable)
		{
			Array.Sort(array, (DriveInfo a, DriveInfo b) => ((b.DriveType == DriveType.Removable) ? 1 : 0) - ((a.DriveType == DriveType.Removable) ? 1 : 0));
		}
		DriveInfo[] array2 = array;
		foreach (DriveInfo drive in array2)
		{
			string name = drive.Name;
			string text = DriveProbe.RunWithTimeout(delegate
			{
				if (!IsCandidateDrive(drive))
				{
					return (string?)null;
				}
				string text2 = (string.IsNullOrEmpty(subdir) ? drive.RootDirectory.FullName : Path.Combine(drive.RootDirectory.FullName, subdir));
				if (!Directory.Exists(text2))
				{
					return (string?)null;
				}
				return (!File.Exists(Path.Combine(text2, "App\\Katalog.db"))) ? null : Path.GetFullPath(text2);
			}, null, 5000, "marker-scan " + name, logger);
			if (text != null)
			{
				logger?.LogInformation("DataRoot: marker scan matched on {Drive} → {Root}", name, text);
				root = text;
				return true;
			}
		}
		return false;
	}

	private static bool IsCandidateDrive(DriveInfo drive)
	{
		if (!drive.IsReady)
		{
			return false;
		}
		DriveType driveType = drive.DriveType;
		if ((uint)(driveType - 2) <= 2u)
		{
			return true;
		}
		return false;
	}

	private static bool TryValidate(string? candidate, out string root)
	{
		root = "";
		if (string.IsNullOrWhiteSpace(candidate))
		{
			return false;
		}
		if (!Directory.Exists(candidate))
		{
			return false;
		}
		root = Path.GetFullPath(candidate);
		return true;
	}

	private static bool DataRootIsValid(string? root, out string full)
	{
		full = "";
		if (string.IsNullOrWhiteSpace(root))
		{
			return false;
		}
		try
		{
			if (!Directory.Exists(root))
			{
				return false;
			}
			if (!File.Exists(Path.Combine(root, "App\\Katalog.db")))
			{
				return false;
			}
			full = Path.GetFullPath(root);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static bool TryAcceptLocalNetworkRoot(string? root, out string full)
	{
		full = "";
		if (string.IsNullOrWhiteSpace(root))
		{
			return false;
		}
		if (root.StartsWith("\\\\", StringComparison.Ordinal) || root.StartsWith("//", StringComparison.Ordinal))
		{
			return false;
		}
		try
		{
			Directory.CreateDirectory(root);
			full = Path.GetFullPath(root);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static bool TryGetVolumeRoot(string anyPath, out string volumeRoot)
	{
		try
		{
			volumeRoot = Path.GetPathRoot(anyPath) ?? "";
			return !string.IsNullOrEmpty(volumeRoot);
		}
		catch
		{
			volumeRoot = "";
			return false;
		}
	}

	private static string? ParseArgRoot(string[]? args)
	{
		if (args == null)
		{
			return null;
		}
		for (int i = 0; i < args.Length; i++)
		{
			if (string.Equals(args[i], "--data-root", StringComparison.OrdinalIgnoreCase))
			{
				if (i + 1 >= args.Length)
				{
					return null;
				}
				return args[i + 1];
			}
		}
		return null;
	}
}
