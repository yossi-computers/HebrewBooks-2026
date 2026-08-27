namespace HebrewBooks.Search.Incremental;

public readonly record struct ScannedFile(string Key, long Size, long Mtime, string AbsPath);
