using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using CommunityToolkit.Mvvm.Input;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.Infrastructure.Settings;
using HebrewBooks.Services.Search;
using HebrewBooks.UI.Resources;
using HebrewBooks.UI.Services;
using Serilog;

namespace HebrewBooks.UI.ViewModels;

public partial class PdfViewerViewModel : ObservableObject, IHitStripNavigator
{
	private readonly SearchOrchestrator _search;

	private readonly JsonSettingsStore _settings;

	private readonly RemoteSearchClient _remote = new RemoteSearchClient();

	private readonly WebApiClient _webApi = new WebApiClient();

	private readonly RasheyTevotMap _rasheyTevotMap;

	private readonly HebAramMap _hebAramMap;

	[ObservableProperty]
	private string _bookTitle = string.Empty;

	[ObservableProperty]
	private string? _currentPdfPath;

	[ObservableProperty]
	private string? _currentFileId;

	[ObservableProperty]
	private int _currentPage = 1;

	[ObservableProperty]
	private bool _isTextMode;

	[ObservableProperty]
	private string? _textRelativePath;

	[ObservableProperty]
	private string _inBookSearchText = string.Empty;

	[ObservableProperty]
	private string _inBookSearchStatus = string.Empty;

	[ObservableProperty]
	private string _matchPositionText = string.Empty;

	[ObservableProperty]
	[NotifyPropertyChangedFor("HasInBookResults")]
	[NotifyPropertyChangedFor("ShowHitPagesStrip")]
	private InBookHitInfo? _currentBookHits;

	[ObservableProperty]
	private int _currentHitIndex = -1;

	[ObservableProperty]
	private int? _selectedHitPage;

	[ObservableProperty]
	private string? _markedPdfPath;

	private string? _searchQueryOverride;

	private bool _syncingSelectionFromViewer;





	public IReadOnlyList<string> TextHighlightTerms { get; private set; } = Array.Empty<string>();

	public bool HasInBookResults
	{
		get
		{
			InBookHitInfo currentBookHits = CurrentBookHits;
			if ((object)currentBookHits != null)
			{
				return currentBookHits.HitCount > 0;
			}
			return false;
		}
	}

	public bool ShowHitPagesStrip
	{
		get
		{
			if (HasInBookResults)
			{
				return _settings.Load().View.ShowHitPagesStrip;
			}
			return false;
		}
	}

	public ObservableCollection<int> InBookHitPages { get; } = new ObservableCollection<int>();


















	public event Action<int>? ScrollToPageRequested;

	public PdfViewerViewModel(SearchOrchestrator search, JsonSettingsStore settings, RasheyTevotMap rasheyTevotMap, HebAramMap hebAramMap)
	{
		_search = search;
		_settings = settings;
		_rasheyTevotMap = rasheyTevotMap;
		_hebAramMap = hebAramMap;
	}

	public void LoadBasic(string fileId, string title, string pdfPath, string? displayQuery = null, string? searchQuery = null)
	{
		IsTextMode = false;
		TextRelativePath = null;
		TextHighlightTerms = Array.Empty<string>();
		CurrentFileId = fileId;
		BookTitle = title;
		CurrentPdfPath = pdfPath;
		CurrentPage = 1;
		MarkedPdfPath = null;
		CurrentBookHits = null;
		InBookHitPages.Clear();
		OnPropertyChanged("InBookHitPages");
		InBookSearchText = displayQuery ?? string.Empty;
		_searchQueryOverride = (string.IsNullOrWhiteSpace(searchQuery) ? null : searchQuery);
	}

	public void LoadTextBook(string fileId, string relativePath, string title, string? displayQuery = null, IReadOnlyList<string>? terms = null)
	{
		IsTextMode = true;
		CurrentFileId = fileId;
		TextRelativePath = relativePath;
		BookTitle = title;
		CurrentPdfPath = null;
		CurrentPage = 1;
		MarkedPdfPath = null;
		CurrentBookHits = null;
		InBookHitPages.Clear();
		OnPropertyChanged("InBookHitPages");
		InBookSearchText = displayQuery ?? string.Empty;
		IReadOnlyList<string> textHighlightTerms;
		if (!string.IsNullOrWhiteSpace(displayQuery))
		{
			textHighlightTerms = QueryBuilder.ExtractHighlightTerms(displayQuery, addPrefixes: false, expandRoots: false, expandNumberGender: false, expandGematria: false, expandSpelling: false, null, expandRashiOcr: false, dropPhraseConstituents: true);
		}
		else
		{
			IReadOnlyList<string> readOnlyList = ((terms != null) ? terms.Where((string t) => !string.IsNullOrWhiteSpace(t)).ToArray() : Array.Empty<string>());
			textHighlightTerms = readOnlyList;
		}
		TextHighlightTerms = textHighlightTerms;
	}

	public Task StartInitialSearchAsync()
	{
		if (string.IsNullOrWhiteSpace(InBookSearchText))
		{
			return Task.CompletedTask;
		}
		return SearchInBookAsync();
	}

	private string? RemoteSearchUrl()
	{
		return _settings.Load().EffectiveSearchServiceUrl();
	}

	private string? OnlineServiceUrl()
	{
		return _settings.Load().EffectiveOnlineServiceUrl();
	}

	[RelayCommand]
	private async Task SearchInBookAsync()
	{
		if (string.IsNullOrWhiteSpace(InBookSearchText))
		{
			ResetSearch();
			return;
		}
		if (string.IsNullOrEmpty(CurrentFileId))
		{
			Log.Warning("SearchInBook: no book loaded in the viewer window — query '{Q}' skipped", InBookSearchText);
			ResetSearch();
			return;
		}
		try
		{
			await _search.EnsureIndexOpenAsync();
			string fileName = (CurrentFileId.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? CurrentFileId : (CurrentFileId + ".pdf"));
			SearchOptions search = _settings.Load().Search;
			string text = RemoteSearchUrl();
			string actualQuery = null;
			string rawQuery = ((!string.IsNullOrEmpty(_searchQueryOverride)) ? _searchQueryOverride : InBookSearchText.Trim());
			InBookHitInfo inBookHitInfo;
			string siteBase;
			int result;
			if (text != null)
			{
				inBookHitInfo = await _remote.GetInBookHitsAsync(text, fileName, rawQuery, new RemoteSearchClient.InBookOptions(search.Hybur, search.RootSearch, search.ExpandGematria, search.ExpandSpelling, search.ExpandNumberGender, search.ExpandAramaic, search.RasheyTevot, search.RequireWordOrder, search.ExpandRashiOcr, Math.Clamp(search.Fuzziness, 0, 10)), InBookSearchText);
			}
			else if ((siteBase = OnlineServiceUrl()) != null && int.TryParse(CurrentFileId, out result))
			{
				inBookHitInfo = await _webApi.GetInBookHitsAsync(siteBase, result.ToString(CultureInfo.InvariantCulture), rawQuery, new WebApiClient.InBookOptions(search.Hybur, search.RootSearch, search.ExpandGematria, search.ExpandSpelling, search.ExpandNumberGender, search.ExpandAramaic, search.RasheyTevot, search.RequireWordOrder, search.ExpandRashiOcr, Math.Clamp(search.Fuzziness, 0, 10)));
			}
			else
			{
				actualQuery = ((!string.IsNullOrEmpty(_searchQueryOverride)) ? _searchQueryOverride : QueryBuilder.Build(InBookSearchText.Trim(), new QueryBuildOptions(30, search.Hybur, FirstWordOnly: false, LastWordOnly: false, search.RasheyTevot ? _rasheyTevotMap : null, search.RootSearch, search.ExpandNumberGender, search.ExpandGematria, search.ExpandSpelling, search.ExpandAramaic ? _hebAramMap : null, _search.Engine.FilterIndexedWords, 200, RequireWordOrder: search.RequireWordOrder, ExpandRashiOcr: search.ExpandRashiOcr, ExpandWeakLetters: search.ExpandWeakLetters)));
				inBookHitInfo = await _search.GetInBookHitsCachedAsync(fileName, actualQuery, InBookSearchText, search.Hybur, search.RootSearch, search.ExpandNumberGender, search.ExpandGematria, search.ExpandSpelling, search.ExpandAramaic ? _hebAramMap : null, Math.Clamp(search.Fuzziness, 0, 10), search.ExpandWeakLetters);
			}
			InBookHitPages.Clear();
			if (inBookHitInfo.HitCount == 0)
			{
				CurrentBookHits = null;
				InBookSearchStatus = SharedStrings.S785;
				CurrentHitIndex = -1;
				SelectedHitPage = null;
				MatchPositionText = string.Empty;
				OnPropertyChanged("InBookHitPages");
				return;
			}
			CurrentBookHits = inBookHitInfo;
			if (inBookHitInfo.Pages.Count > 0)
			{
				foreach (int page in inBookHitInfo.Pages)
				{
					InBookHitPages.Add(page);
				}
			}
			else
			{
				InBookHitPages.Add(1);
			}
			OnPropertyChanged("InBookHitPages");
			CurrentHitIndex = 0;
			CurrentPage = InBookHitPages[0];
			SelectedHitPage = CurrentPage;
			UpdateMatchPosition();
			InBookSearchStatus = ((inBookHitInfo.Pages.Count > 0) ? $"{SharedStrings.S2223}{inBookHitInfo.HitCount}{SharedStrings.S2224}{inBookHitInfo.Pages.Count}{SharedStrings.S2225}" : $"{SharedStrings.S2226}{inBookHitInfo.HitCount}{SharedStrings.S2227}");
			if (actualQuery == null)
			{
				MarkedPdfPath = null;
				return;
			}
			try
			{
				MarkedPdfPath = await _search.GetMarkedPdfPathCachedAsync(fileName, actualQuery);
			}
			catch (Exception ex)
			{
				MarkedPdfPath = null;
				InBookSearchStatus = SharedStrings.S2228 + ex.Message;
			}
		}
		catch (Exception ex2)
		{
			InBookSearchStatus = SharedStrings.S2229 + ex2.Message;
		}
	}

	private void RequestScrollToPage(int page)
	{
		if (page > 0)
		{
			this.ScrollToPageRequested?.Invoke(page);
		}
	}

	[RelayCommand]
	private void NextHit()
	{
		if (InBookHitPages.Count != 0)
		{
			CurrentHitIndex = (CurrentHitIndex + 1) % InBookHitPages.Count;
			CurrentPage = InBookHitPages[CurrentHitIndex];
			SelectedHitPage = CurrentPage;
			UpdateMatchPosition();
			RequestScrollToPage(CurrentPage);
		}
	}

	[RelayCommand]
	private void PrevHit()
	{
		if (InBookHitPages.Count != 0)
		{
			CurrentHitIndex = (CurrentHitIndex - 1 + InBookHitPages.Count) % InBookHitPages.Count;
			CurrentPage = InBookHitPages[CurrentHitIndex];
			SelectedHitPage = CurrentPage;
			UpdateMatchPosition();
			RequestScrollToPage(CurrentPage);
		}
	}

	[RelayCommand]
	private void GoToHitPage(int page)
	{
		if (page > 0)
		{
			int num = InBookHitPages.IndexOf(page);
			if (num >= 0)
			{
				CurrentHitIndex = num;
			}
			CurrentPage = page;
			SelectedHitPage = page;
			UpdateMatchPosition();
			RequestScrollToPage(page);
		}
	}

	public void ReportViewerPage(int page)
	{
		if (page <= 0)
		{
			return;
		}
		int? num = (InBookHitPages.Contains(page) ? new int?(page) : ((int?)null));
		if (SelectedHitPage == num)
		{
			return;
		}
		_syncingSelectionFromViewer = true;
		try
		{
			SelectedHitPage = num;
		}
		finally
		{
			_syncingSelectionFromViewer = false;
		}
	}

	public void ApplyHighlightProgress(int drawnSoFar)
	{
		InBookHitInfo currentBookHits = CurrentBookHits;
		int num = currentBookHits?.HitCount ?? 0;
		if (num != 0)
		{
			int count = currentBookHits.Pages.Count;
			if (drawnSoFar == 0 || drawnSoFar >= num)
			{
				InBookSearchStatus = ((count > 0) ? $"{SharedStrings.S2230}{num}{SharedStrings.S2231}{count}{SharedStrings.S2232}" : $"{SharedStrings.S2233}{num}{SharedStrings.S2234}");
			}
			else
			{
				InBookSearchStatus = ((count > 0) ? $"{SharedStrings.S2235}{drawnSoFar}{SharedStrings.S2236}{num}{SharedStrings.S2237}{count}{SharedStrings.S2238}" : $"{SharedStrings.S2239}{drawnSoFar}{SharedStrings.S2240}{num}{SharedStrings.S2241}");
			}
		}
	}

	public void ApplyFuzzyFinalPages(IReadOnlyList<int> pages)
	{
		if (InBookHitPages.SequenceEqual(pages))
		{
			return;
		}
		InBookHitPages.Clear();
		foreach (int page in pages)
		{
			InBookHitPages.Add(page);
		}
		OnPropertyChanged("InBookHitPages");
	}

	public void ApplyVerifiedHitPages(IReadOnlyList<int> pages)
	{
		if (pages.Count == 0 || InBookHitPages.SequenceEqual(pages))
		{
			return;
		}
		bool flag = CurrentHitIndex == 0 && InBookHitPages.Count > 0;
		InBookHitPages.Clear();
		foreach (int page in pages)
		{
			InBookHitPages.Add(page);
		}
		OnPropertyChanged("InBookHitPages");
		if (flag)
		{
			CurrentHitIndex = 0;
			CurrentPage = pages[0];
			SelectedHitPage = pages[0];
		}
		UpdateMatchPosition();
	}

	private void ResetSearch()
	{
		InBookHitPages.Clear();
		OnPropertyChanged("InBookHitPages");
		CurrentHitIndex = -1;
		CurrentBookHits = null;
		SelectedHitPage = null;
		InBookSearchStatus = string.Empty;
		MatchPositionText = string.Empty;
		MarkedPdfPath = null;
	}

	private void UpdateMatchPosition()
	{
		if (InBookHitPages.Count == 0 || CurrentHitIndex < 0)
		{
			MatchPositionText = string.Empty;
			return;
		}
		MatchPositionText = $"{CurrentHitIndex + 1} / {InBookHitPages.Count}";
	}


}
