using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.Infrastructure.Settings;

namespace HebrewBooks.Infrastructure.Paths;

public sealed class PathResolver : IPathResolver
{
	public enum FastIndexStatus
	{
		NotSet,
		Usable,
		FolderMissing,
		NoIndexFiles
	}

	public string DataDriveRoot { get; }

	public char DriveLetter { get; }

	public string AppPath { get; }

	public string LanguagesDir { get; }

	public string CatalogDbPath { get; }

	public string RasheyTevotPath { get; }

	public string HebAramPath { get; }

	public string CiteDbPath { get; }

	public string PdfsRoot { get; }

	public string IndexesRoot { get; }

	public string OtzrayaRoot { get; }

	public string OtzrayaIndexPath { get; }

	public string PersonalRoot { get; }

	public string PersonalIndexPath { get; }

	public string UserDataRoot { get; }

	public string WorkAreaDir { get; }

	public string BookBackupsRoot { get; }

	private static string? UsableIndexDir(string? dir)
	{
		dir = PathInput.Normalize(dir);
		if (dir == null)
		{
			return null;
		}
		try
		{
			return (Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*.ix").Any()) ? dir : null;
		}
		catch
		{
			return null;
		}
	}

	public static (FastIndexStatus Status, string? Path) InspectFastIndexDir(string? configured)
	{
		string text = PathInput.Normalize(configured);
		if (text == null)
		{
			return (Status: FastIndexStatus.NotSet, Path: null);
		}
		string text2 = UsableIndexDir(text);
		if (text2 != null)
		{
			return (Status: FastIndexStatus.Usable, Path: text2);
		}
		bool flag;
		try
		{
			flag = Directory.Exists(text);
		}
		catch
		{
			flag = false;
		}
		if (!flag)
		{
			return (Status: FastIndexStatus.FolderMissing, Path: text);
		}
		string text3 = UsableIndexDir(Path.Combine(text, "Bookshelf_IDX"));
		if (text3 == null)
		{
			return (Status: FastIndexStatus.NoIndexFiles, Path: text);
		}
		return (Status: FastIndexStatus.Usable, Path: text3);
	}

	public static bool IsUsableIndexDir(string? dir)
	{
		return UsableIndexDir(dir) != null;
	}

	public static int CountIndexSegments(string? dir)
	{
		string text = PathInput.Normalize(dir);
		if (text == null)
		{
			return 0;
		}
		try
		{
			return Directory.EnumerateFiles(text, "*.ix").Count();
		}
		catch
		{
			return 0;
		}
	}

	public static IReadOnlyList<string> FindIndexFoldersInside(string? dir, int max = 5)
	{
		string text = PathInput.Normalize(dir);
		if (text == null)
		{
			return Array.Empty<string>();
		}
		try
		{
			return Directory.EnumerateDirectories(text).Where(IsUsableIndexDir).Take(max)
				.ToList();
		}
		catch
		{
			return Array.Empty<string>();
		}
	}

	private static string? TryGetParent(string dir)
	{
		try
		{
			return Path.GetDirectoryName(Path.GetFullPath(dir));
		}
		catch
		{
			return null;
		}
	}

	private static string SiblingIndexOrDefault(string? overrideBase, string folderName, string fallbackBase)
	{
		if (!string.IsNullOrEmpty(overrideBase))
		{
			string text = UsableIndexDir(Path.Combine(overrideBase, folderName));
			if (text != null)
			{
				return text;
			}
		}
		return Path.Combine(fallbackBase, folderName);
	}

	public PathResolver(string dataRoot, BookshelfOptions options)
	{
		DataDriveRoot = Path.GetFullPath(dataRoot);
		DriveLetter = ((DataDriveRoot.Length >= 1) ? DataDriveRoot[0] : '?');
		AppPath = AppContext.BaseDirectory;
		LanguagesDir = Path.Combine(DataDriveRoot, "App", "Languages");
		CatalogDbPath = Path.Combine(DataDriveRoot, "App", "Katalog.db");
		RasheyTevotPath = Path.Combine(DataDriveRoot, "App", "RasheyTevot.DB");
		HebAramPath = Path.Combine(DataDriveRoot, "App", "HebAram.DB");
		CiteDbPath = Path.Combine(DataDriveRoot, "App", "cite.db");
		string text = options.EffectiveBooksDir();
		string text2 = options.EffectiveNetworkBase();
		string text3 = (options.NetworkInstall ? options.Paths.IndexesDirOverride : null);
		(FastIndexStatus, string) tuple = InspectFastIndexDir(options.Paths.FastIndexesDir);
		string text4 = ((tuple.Item1 == FastIndexStatus.Usable) ? tuple.Item2 : null);
		if (text4 != null)
		{
			text3 = text4;
		}
		PdfsRoot = ((!string.IsNullOrWhiteSpace(text)) ? text : Path.Combine(DataDriveRoot, "Books"));
		IndexesRoot = ((!string.IsNullOrWhiteSpace(text3)) ? text3 : ((!string.IsNullOrEmpty(text2)) ? Path.Combine(text2, "Bookshelf_IDX") : Path.Combine(DataDriveRoot, "Bookshelf_IDX")));
		string text5 = ((!string.IsNullOrEmpty(text2)) ? text2 : DataDriveRoot);
		OtzrayaRoot = Path.Combine(text5, "Otzraya");
		PersonalRoot = Path.Combine(text5, "Personal");
		string overrideBase = ((text4 == null) ? null : TryGetParent(text4));
		OtzrayaIndexPath = SiblingIndexOrDefault(overrideBase, "Otzraya_IDX", text5);
		PersonalIndexPath = SiblingIndexOrDefault(overrideBase, "Personal_IDX", text5);
		UserDataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HebrewBooks");
		WorkAreaDir = Path.Combine(UserDataRoot, "WorkArea");
		BookBackupsRoot = Path.Combine(UserDataRoot, "BookBackups");
	}

	public string PdfPath(int fileId, string? folder)
	{
		string path = (string.IsNullOrEmpty(folder) ? "" : folder);
		return Path.Combine(PdfsRoot, path, $"{fileId}.pdf");
	}

	public string OtzrayaTextPath(string relativePath)
	{
		string path = relativePath?.TrimStart('\\', '/') ?? string.Empty;
		return Path.Combine(OtzrayaRoot, path);
	}

	public string PersonalFilePath(string relativePath)
	{
		string path = relativePath?.TrimStart('\\', '/') ?? string.Empty;
		return Path.Combine(PersonalRoot, path);
	}

	public string BookBackupPath(int fileId)
	{
		return Path.Combine(BookBackupsRoot, $"{fileId}.pdf");
	}

	public string BookBackupPath(string key)
	{
		string text = string.Concat((key ?? "").Select((char c) => (Array.IndexOf(Path.GetInvalidFileNameChars(), c) < 0) ? c : '_')).Trim('_', ' ');
		if (text.Length == 0 || text.Length > 120)
		{
			text = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key ?? "")));
		}
		return Path.Combine(BookBackupsRoot, text + ".pdf");
	}
}
