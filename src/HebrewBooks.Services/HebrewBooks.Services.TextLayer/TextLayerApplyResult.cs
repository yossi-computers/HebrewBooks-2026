using System;

namespace HebrewBooks.Services.TextLayer;

public sealed record TextLayerApplyResult(int FileId, string UsbPath, string BackupPath, bool BackupCreated, long NewSizeBytes, TimeSpan Elapsed);
