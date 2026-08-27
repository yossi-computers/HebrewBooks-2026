using System.Collections.Generic;

namespace HebrewBooks.Services.Downloader;

public sealed record CompletionScan(int MirrorCount, int DiskCount, int MaxMirror, IReadOnlyList<int> Missing);
