namespace HebrewBooks.Infrastructure.Settings;

public sealed class SearchOptions
{
	public const string DefaultHighlightColor = "#FFD500";

	public bool TrimQuery { get; set; } = true;

	public int SortMode { get; set; } = 1;

	public bool RunOnStartup { get; set; } = true;

	public int MaxProximity { get; set; } = 30;

	public bool Hybur { get; set; }

	public bool RasheyTevot { get; set; }

	public bool RootSearch { get; set; }

	public bool ExpandNumberGender { get; set; }

	public bool ExpandGematria { get; set; }

	public bool ExpandSpelling { get; set; }

	public bool ExpandAramaic { get; set; }

	public bool ResizeFont { get; set; } = true;

	public bool IncludeNumbers { get; set; } = true;

	public bool SortBySeder { get; set; }

	public int MaxFilesToRetrieve { get; set; } = 10000;

	public bool QuickSave { get; set; } = true;

	public bool OpenRichTextOnSearch { get; set; } = true;

	public bool ShowResultsList { get; set; }

	public int Fuzziness { get; set; }

	public bool RequireWordOrder { get; set; }

	public bool ExpandRashiOcr { get; set; }

	public bool ExpandWeakLetters { get; set; }

	public string HighlightColor { get; set; } = "#FFD500";
}
