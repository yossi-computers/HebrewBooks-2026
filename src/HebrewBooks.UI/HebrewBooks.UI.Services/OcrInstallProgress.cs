namespace HebrewBooks.UI.Services;

public sealed record OcrInstallProgress(OcrInstallPhase Phase, int Percent, string Message);
