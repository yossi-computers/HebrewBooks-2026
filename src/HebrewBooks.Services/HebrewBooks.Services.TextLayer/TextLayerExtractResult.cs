using System;

namespace HebrewBooks.Services.TextLayer;

public sealed record TextLayerExtractResult(int FileId, string TextLayerPath, int PagesProcessed, int Words, long TextLayerBytes, TimeSpan Elapsed, string Engine, string Mode);
