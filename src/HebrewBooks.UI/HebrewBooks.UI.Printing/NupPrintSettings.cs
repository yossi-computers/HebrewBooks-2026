namespace HebrewBooks.UI.Printing;

public sealed record NupPrintSettings(int PagesPerSheet = 1, NupOrderMode Order = NupOrderMode.RightToLeftThenDown, double Margin = 24.0, double Gutter = 16.0);
