namespace HebrewBooks.Services.Updates;

public sealed record TextLayerManifestEntry(int FileId, string Sha256, int Version, string DownloadUrl, long SizeBytes);
