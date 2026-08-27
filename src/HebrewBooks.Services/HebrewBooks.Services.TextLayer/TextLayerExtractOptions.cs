namespace HebrewBooks.Services.TextLayer;

public sealed record TextLayerExtractOptions(string Engine = "windows", string Mode = "fix-text-only", bool RashiCorrect = true);
