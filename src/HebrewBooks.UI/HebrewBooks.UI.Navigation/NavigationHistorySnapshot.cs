namespace HebrewBooks.UI.Navigation;

public sealed record NavigationHistorySnapshot(NavigationEntry? Current, NavigationEntry[]? Back, NavigationEntry[]? Forward, string[]? SelectedCorpora = null);
