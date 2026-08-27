namespace HebrewBooks.Infrastructure.Settings;

public sealed class ViewOptions
{
	public int ManualResize { get; set; } = 3150;

	public int ExplorerBarWidth { get; set; } = 350;

	public double PercentSplitBarInLeft { get; set; } = 42.5;

	public bool PinResultList { get; set; } = true;

	public int CountScroll { get; set; } = 1;

	public MissingBookAction MissingBookAction { get; set; }

	public double LibraryCatalogRatio { get; set; } = 0.5;

	public string Theme { get; set; } = "System";

	public bool NavPaneOpen { get; set; } = true;

	public WindowPlacementOptions MainWindowPlacement { get; set; } = new WindowPlacementOptions();

	public bool ShowRowDetails { get; set; } = true;

	public bool ShowSearchHint { get; set; } = true;

	public bool EnableSynonymChips { get; set; } = true;

	public bool ShowPerformanceHints { get; set; } = true;

	public int SlowStorageEvents { get; set; }

	public bool ChromeAutoHide { get; set; }

	public bool ShowPageRail { get; set; }

	public bool ShowHitPagesStrip { get; set; } = true;

	public bool UnifiedSearchLayout { get; set; } = true;

	public bool PersistSessionState { get; set; } = true;

	public int RegionCopyDpi { get; set; } = 200;

	public int PrintDpi { get; set; }
}
