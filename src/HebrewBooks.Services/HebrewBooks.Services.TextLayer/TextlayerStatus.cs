using System;

namespace HebrewBooks.Services.TextLayer;

public sealed record TextlayerStatus(int FileId, string SidecarSha256, int Version, string Source, DateTime AppliedAtUtc);
