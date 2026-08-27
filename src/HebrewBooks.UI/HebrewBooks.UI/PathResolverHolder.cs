using System;
using HebrewBooks.Core.Abstractions;

namespace HebrewBooks.UI;

internal sealed class PathResolverHolder : IPathResolver
{
	private IPathResolver? _inner;

	private IPathResolver Inner => _inner ?? throw new InvalidOperationException("DataRoot not yet resolved.");

	public string DataDriveRoot => Inner.DataDriveRoot;

	public char DriveLetter => Inner.DriveLetter;

	public string AppPath => Inner.AppPath;

	public string LanguagesDir => Inner.LanguagesDir;

	public string CatalogDbPath => Inner.CatalogDbPath;

	public string RasheyTevotPath => Inner.RasheyTevotPath;

	public string HebAramPath => Inner.HebAramPath;

	public string CiteDbPath => Inner.CiteDbPath;

	public string PdfsRoot => Inner.PdfsRoot;

	public string IndexesRoot => Inner.IndexesRoot;

	public string OtzrayaRoot => Inner.OtzrayaRoot;

	public string OtzrayaIndexPath => Inner.OtzrayaIndexPath;

	public string PersonalRoot => Inner.PersonalRoot;

	public string PersonalIndexPath => Inner.PersonalIndexPath;

	public string UserDataRoot => Inner.UserDataRoot;

	public string WorkAreaDir => Inner.WorkAreaDir;

	public string BookBackupsRoot => Inner.BookBackupsRoot;

	public void Set(IPathResolver inner)
	{
		_inner = inner;
	}

	public string PdfPath(int fileId, string? folder)
	{
		return Inner.PdfPath(fileId, folder);
	}

	public string OtzrayaTextPath(string relativePath)
	{
		return Inner.OtzrayaTextPath(relativePath);
	}

	public string PersonalFilePath(string relativePath)
	{
		return Inner.PersonalFilePath(relativePath);
	}

	public string BookBackupPath(int fileId)
	{
		return Inner.BookBackupPath(fileId);
	}

	public string BookBackupPath(string key)
	{
		return Inner.BookBackupPath(key);
	}
}
