using CommunityToolkit.Mvvm.ComponentModel;
using HebrewBooks.Services.Provisioning;

namespace HebrewBooks.UI.ViewModels;

public sealed partial class TierCardViewModel : ObservableObject
{
	public InstallTier Tier { get; }

	public string Title { get; }

	public string Subtitle { get; }

	public string SizeText { get; }

	public bool Fits { get; }

	public string FitWarning { get; }

	public TierCardViewModel(InstallTier tier, string title, string subtitle, string sizeText, bool fits, string fitWarning)
	{
		Tier = tier;
		Title = title;
		Subtitle = subtitle;
		SizeText = sizeText;
		Fits = fits;
		FitWarning = fitWarning;
	}
}
