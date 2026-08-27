namespace HebrewBooks.Core.Abstractions;

public interface IPathResolver
{
	string DataDriveRoot { get; }

	char DriveLetter { get; }

	string AppPath { get; }

	string LanguagesDir { get; }

	string CatalogDbPath { get; }

	string PdfsRoot { get; }

	string IndexesRoot { get; }

	string UserDataRoot { get; }

	string WorkAreaDir { get; }

	string RasheyTevotPath { get; }

	string HebAramPath { get; }

	string CiteDbPath { get; }

	string OtzrayaRoot { get; }

	string OtzrayaIndexPath { get; }

	string PersonalRoot { get; }

	string PersonalIndexPath { get; }

	string BookBackupsRoot { get; }

	string PdfPath(int fileId, string? folder);

	string OtzrayaTextPath(string relativePath);

	string PersonalFilePath(string relativePath);

	string BookBackupPath(int fileId);

	string BookBackupPath(string key);
}
