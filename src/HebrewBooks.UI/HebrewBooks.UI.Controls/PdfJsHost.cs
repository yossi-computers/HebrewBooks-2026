using System;
using System.CodeDom.Compiler;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media.Imaging;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.Core.Models;
using HebrewBooks.UI.Dialogs;
using HebrewBooks.UI.Resources;
using HebrewBooks.UI.Services;
using HebrewBooks.UI.Views;
using Microsoft.Data.Sqlite;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Serilog;

namespace HebrewBooks.UI.Controls;

public partial class PdfJsHost : UserControl
{
	private enum ViewerMode
	{
		None,
		Pdf,
		Text
	}

	private bool _viewerLoaded;

	private TaskCompletionSource<bool>? _viewerReady;

	private TaskCompletionSource<bool>? _bridgeReady;

	private string? _currentPdfPath;

	private IPathResolver? _lastPaths;

	private readonly Stack<(string Path, int Page)> _navHistory = new Stack<(string, int)>();

	private (IPathResolver paths, string relativePath, string title, IReadOnlyList<string>? terms, int? anchor)? _textRestoreState;

	private ViewerMode _currentMode;

	private bool _pdfPageLoaded;

	private bool _textViewInited;

	private bool _textPageLoaded;

	private TaskCompletionSource<bool>? _textViewReady;

	private TaskCompletionSource<bool>? _textBridgeReady;

	private CancellationTokenSource? _openGenerationCts;

	private TaskCompletionSource<string?>? _pendingPrintRender;

	private int _pendingPrintPage;

	private readonly SemaphoreSlim _printRenderLock = new SemaphoreSlim(1, 1);

	private static readonly ConcurrentDictionary<string, string> _citationsJsonCache = new ConcurrentDictionary<string, string>();

	private static bool? _citeHasKindCol;





	private WebView2 ActiveView
	{
		get
		{
			if (_currentMode != ViewerMode.Text)
			{
				return PdfView;
			}
			return TextView;
		}
	}

	public static string CurrentHighlightColor { get; private set; } = "#FFD500";

	public static string CurrentViewerThemeJson { get; private set; } = "{}";

	public static bool CurrentPageRailEnabled { get; private set; }

	public static int CurrentRegionCopyDpi { get; private set; } = 200;

	public static int CurrentFuzziness { get; private set; }

	private static string MarkedDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HebrewBooks", "marked");

	private static string PrintCacheDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HebrewBooks", "print-cache");

	public event EventHandler? TocQuickAddRequested;

	public event EventHandler? TocEditRequested;

	public event EventHandler<IReadOnlyList<int>>? VerifiedHitPagesReceived;

	public event EventHandler<IReadOnlyList<int>>? FuzzyFinalPagesReceived;

	public event EventHandler<int>? HighlightProgressChanged;

	public event EventHandler<int>? CurrentPageChanged;

	public event EventHandler? ImmersiveToggleRequested;

	public event EventHandler? ImmersiveExitRequested;

	public event EventHandler<string>? ShortcutRequested;

	public event EventHandler<bool>? ChromeRevealRequested;

	public static event Action<string>? HighlightColorChanged;

	public static event Action<string>? ViewerThemeChanged;

	public static event Action<bool>? PageRailEnabledChanged;

	public static event Action<int>? RegionCopyDpiChanged;

	public static event Action<int>? FuzzinessChanged;

	private void SetActiveView(ViewerMode mode)
	{
		_currentMode = mode;
		if (mode == ViewerMode.Text)
		{
			TextView.Visibility = Visibility.Visible;
			PdfView.Visibility = Visibility.Collapsed;
		}
		else
		{
			PdfView.Visibility = Visibility.Visible;
			TextView.Visibility = Visibility.Collapsed;
		}
	}

	private CancellationToken BeginOpenGeneration()
	{
		_openGenerationCts?.Cancel();
		_openGenerationCts = new CancellationTokenSource();
		return _openGenerationCts.Token;
	}

	public PdfJsHost()
	{
		InitializeComponent();
		base.Loaded += OnHostLoaded;
		base.Unloaded += OnHostUnloaded;
	}

	private void OnHostLoaded(object sender, RoutedEventArgs e)
	{
		HighlightColorChanged -= OnHighlightColorChanged;
		HighlightColorChanged += OnHighlightColorChanged;
		PageRailEnabledChanged -= OnPageRailEnabledChanged;
		PageRailEnabledChanged += OnPageRailEnabledChanged;
		RegionCopyDpiChanged -= OnRegionCopyDpiChanged;
		RegionCopyDpiChanged += OnRegionCopyDpiChanged;
		FuzzinessChanged -= OnFuzzinessChanged;
		FuzzinessChanged += OnFuzzinessChanged;
		ViewerThemeChanged -= OnViewerThemeChanged;
		ViewerThemeChanged += OnViewerThemeChanged;
		OnHighlightColorChanged(CurrentHighlightColor);
		OnPageRailEnabledChanged(CurrentPageRailEnabled);
		OnRegionCopyDpiChanged(CurrentRegionCopyDpi);
		OnFuzzinessChanged(CurrentFuzziness);
		OnViewerThemeChanged(CurrentViewerThemeJson);
	}

	private void OnHostUnloaded(object sender, RoutedEventArgs e)
	{
		HighlightColorChanged -= OnHighlightColorChanged;
		PageRailEnabledChanged -= OnPageRailEnabledChanged;
		RegionCopyDpiChanged -= OnRegionCopyDpiChanged;
		FuzzinessChanged -= OnFuzzinessChanged;
		ViewerThemeChanged -= OnViewerThemeChanged;
	}

	public static void BroadcastHighlightColor(string hex)
	{
		CurrentHighlightColor = hex;
		Action<string>? highlightColorChanged = PdfJsHost.HighlightColorChanged;
		int propertyValue = ((highlightColorChanged != null) ? highlightColorChanged.GetInvocationList().Length : 0);
		Log.Information("HighlightColor: broadcast {Hex} to {Count} live host(s)", hex, propertyValue);
		PdfJsHost.HighlightColorChanged?.Invoke(hex);
	}

	private async void OnHighlightColorChanged(string hex)
	{
		Log.Information("HighlightColor: host received {Hex}, webview2={HasWv}", hex, PdfView.CoreWebView2 != null);
		if (PdfView.CoreWebView2 == null)
		{
			return;
		}
		string text = JsonSerializer.Serialize(hex);
		try
		{
			await PdfView.CoreWebView2.ExecuteScriptAsync("window.HB_setHighlightColor && window.HB_setHighlightColor(" + text + ");");
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "HighlightColor: ExecuteScriptAsync failed");
		}
	}

	public static void BroadcastViewerTheme(string json)
	{
		CurrentViewerThemeJson = json;
		PdfJsHost.ViewerThemeChanged?.Invoke(json);
	}

	private async void OnViewerThemeChanged(string json)
	{
		await PushViewerThemeAsync(PdfView, json);
		await PushViewerThemeAsync(TextView, json);
	}

	private static async Task PushViewerThemeAsync(WebView2? view, string json)
	{
		if (view?.CoreWebView2 == null)
		{
			return;
		}
		try
		{
			await view.CoreWebView2.ExecuteScriptAsync("window.HB_setTheme && window.HB_setTheme(" + json + ");");
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "ViewerTheme: ExecuteScriptAsync failed");
		}
	}

	public static void BroadcastPageRailEnabled(bool enabled)
	{
		CurrentPageRailEnabled = enabled;
		Action<bool>? pageRailEnabledChanged = PdfJsHost.PageRailEnabledChanged;
		int propertyValue = ((pageRailEnabledChanged != null) ? pageRailEnabledChanged.GetInvocationList().Length : 0);
		Log.Information("PageRail: broadcast {Enabled} to {Count} live host(s)", enabled, propertyValue);
		PdfJsHost.PageRailEnabledChanged?.Invoke(enabled);
	}

	private async void OnPageRailEnabledChanged(bool enabled)
	{
		if (PdfView.CoreWebView2 == null)
		{
			return;
		}
		string text = (enabled ? "true" : "false");
		try
		{
			await PdfView.CoreWebView2.ExecuteScriptAsync("window.HB_setPageRailEnabled && window.HB_setPageRailEnabled(" + text + ");");
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "PageRail: ExecuteScriptAsync failed");
		}
	}

	public static void BroadcastRegionCopyDpi(int dpi)
	{
		int num = (CurrentRegionCopyDpi = Math.Max(72, Math.Min(600, dpi)));
		Action<int>? regionCopyDpiChanged = PdfJsHost.RegionCopyDpiChanged;
		int propertyValue = ((regionCopyDpiChanged != null) ? regionCopyDpiChanged.GetInvocationList().Length : 0);
		Log.Information("RegionCopyDpi: broadcast {Dpi} to {Count} live host(s)", num, propertyValue);
		PdfJsHost.RegionCopyDpiChanged?.Invoke(num);
	}

	private async void OnRegionCopyDpiChanged(int dpi)
	{
		if (PdfView.CoreWebView2 == null)
		{
			return;
		}
		try
		{
			await PdfView.CoreWebView2.ExecuteScriptAsync($"window.HB_setRegionCopyDpi && window.HB_setRegionCopyDpi({dpi});");
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "RegionCopyDpi: ExecuteScriptAsync failed");
		}
	}

	public static void BroadcastFuzziness(int fuzziness)
	{
		int num = (CurrentFuzziness = Math.Max(0, Math.Min(10, fuzziness)));
		Action<int>? fuzzinessChanged = PdfJsHost.FuzzinessChanged;
		int propertyValue = ((fuzzinessChanged != null) ? fuzzinessChanged.GetInvocationList().Length : 0);
		Log.Information("Fuzziness: broadcast {Fuzziness} to {Count} live host(s)", num, propertyValue);
		PdfJsHost.FuzzinessChanged?.Invoke(num);
	}

	private async void OnFuzzinessChanged(int fuzziness)
	{
		if (PdfView.CoreWebView2 == null)
		{
			return;
		}
		try
		{
			await PdfView.CoreWebView2.ExecuteScriptAsync($"window.HB_setFuzziness && window.HB_setFuzziness({fuzziness});");
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "Fuzziness: ExecuteScriptAsync failed");
		}
	}

	public async Task PrewarmAsync(IPathResolver paths)
	{
		_ = 1;
		try
		{
			await EnsureWebViewInitializedAsync(paths);
			await EnsureViewerPageLoadedAsync(ViewerMode.Pdf);
			Log.Information("TIMING: PdfJsHost prewarm complete");
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "PdfJsHost prewarm failed (next OpenAsync will retry)");
		}
	}

	public async Task OpenAsync(IPathResolver paths, string absolutePdfPath, int page = 1, string? highlightXml = null, IReadOnlyList<string>? matchedTerms = null, bool keepHistory = false)
	{
		CancellationToken ct = BeginOpenGeneration();
		if (!keepHistory)
		{
			_navHistory.Clear();
		}
		Stopwatch tSw = Stopwatch.StartNew();
		Log.Information("TIMING: PdfJsHost.OpenAsync start path={Path}", absolutePdfPath);
		Stopwatch initSw = Stopwatch.StartNew();
		await EnsureWebViewInitializedAsync(paths);
		Log.Information("TIMING:   EnsureWebViewInitialized done at +{Elapsed}ms (took {Step}ms, {InitState})", tSw.ElapsedMilliseconds, initSw.ElapsedMilliseconds, (initSw.ElapsedMilliseconds > 50) ? "FIRST-INIT" : "cached");
		Stopwatch viewSw = Stopwatch.StartNew();
		if (!(await EnsureViewerPageLoadedAsync(ViewerMode.Pdf, ct)))
		{
			Log.Information("PdfJsHost.OpenAsync superseded during viewer load (newer open in flight) — aborting {Path}", absolutePdfPath);
			return;
		}
		Log.Information("TIMING:   EnsureViewerPageLoaded done at +{Elapsed}ms (took {Step}ms)", tSw.ElapsedMilliseconds, viewSw.ElapsedMilliseconds);
		SetActiveView(ViewerMode.Pdf);
		_currentPdfPath = absolutePdfPath;
		string pdfsRoot = paths.PdfsRoot;
		string value;
		string relative2;
		if (TryMakeRelative(pdfsRoot, absolutePdfPath, out string relative))
		{
			string text = string.Join('/', relative.Split('/').Select(Uri.EscapeDataString));
			value = "https://books.local/" + text;
		}
		else if (TryMakeRelative(MarkedDir, absolutePdfPath, out relative2))
		{
			string text2 = string.Join('/', relative2.Split('/').Select(Uri.EscapeDataString));
			value = "https://marks.local/" + text2;
		}
		else
		{
			if (string.IsNullOrEmpty(paths.PersonalRoot) || !TryMakeRelative(paths.PersonalRoot, absolutePdfPath, out string relative3))
			{
				throw new InvalidOperationException($"PDF '{absolutePdfPath}' is not under PdfsRoot '{pdfsRoot}', marked dir '{MarkedDir}', or PersonalRoot '{paths.PersonalRoot}'.");
			}
			string text3 = string.Join('/', relative3.Split('/').Select(Uri.EscapeDataString));
			value = "https://personal.local/" + text3;
		}
		ShowLoading(show: true);
		string value2 = JsonSerializer.Serialize(value);
		string value3 = JsonSerializer.Serialize(highlightXml ?? string.Empty);
		string value4 = JsonSerializer.Serialize(matchedTerms?.Where((string t) => !string.IsNullOrWhiteSpace(t)).ToArray() ?? Array.Empty<string>());
		string value5 = (App.IsProtectMode ? "true" : "false");
		string value6 = JsonSerializer.Serialize(App.ResolvedLanguage);
		string javaScript = $"window.HB_setLang && window.HB_setLang({value6}); window.HB_setProtectMode && window.HB_setProtectMode({value5}); window.HB_setFuzziness && window.HB_setFuzziness({CurrentFuzziness}); window.HB_loadPdf && window.HB_loadPdf({value2}, {page}, {value3}, {value4});";
		if (ct.IsCancellationRequested)
		{
			Log.Information("PdfJsHost.OpenAsync superseded before HB_loadPdf — aborting {Path}", absolutePdfPath);
			return;
		}
		Stopwatch execSw = Stopwatch.StartNew();
		await PdfView.CoreWebView2.ExecuteScriptAsync(javaScript);
		Log.Information("TIMING: PdfJsHost.OpenAsync total {Elapsed}ms (ExecuteScript {Step}ms — JS HB_loadPdf has been invoked; PDF parse/render still in flight in JS)", tSw.ElapsedMilliseconds, execSw.ElapsedMilliseconds);
		_lastPaths = paths;
		await TrySetCitationsAsync(paths, absolutePdfPath);
		await SetBackVisibleAsync();
	}

	public async Task OpenTextAsync(IPathResolver paths, string relativePath, string title = "", IReadOnlyList<string>? terms = null, int? anchorIndex = null)
	{
		CancellationToken ct = BeginOpenGeneration();
		await EnsureTextViewInitializedAsync(paths);
		if (!(await EnsureTextPageLoadedAsync(ct)))
		{
			Log.Information("PdfJsHost.OpenTextAsync superseded during viewer load (newer open in flight) — aborting {Path}", relativePath);
			return;
		}
		string text = string.Join('/', relativePath.Replace('\\', '/').Split('/').Select(Uri.EscapeDataString));
		string value = "https://otzraya.local/" + text;
		ShowLoading(show: true);
		string value2 = JsonSerializer.Serialize(value);
		string value3 = JsonSerializer.Serialize(title ?? string.Empty);
		string value4 = JsonSerializer.Serialize(terms?.Where((string t) => !string.IsNullOrWhiteSpace(t)).ToArray() ?? Array.Empty<string>());
		string value5 = (anchorIndex.HasValue ? anchorIndex.Value.ToString() : "null");
		string value6 = (App.IsProtectMode ? "true" : "false");
		string javaScript = $"window.HB_setProtectMode && window.HB_setProtectMode({value6}); window.HB_loadText && window.HB_loadText({value2}, {value3}, {value4}, {value5});";
		if (ct.IsCancellationRequested)
		{
			Log.Information("PdfJsHost.OpenTextAsync superseded before HB_loadText — aborting {Path}", relativePath);
			return;
		}
		await TextView.CoreWebView2.ExecuteScriptAsync(javaScript);
		SetActiveView(ViewerMode.Text);
		_textRestoreState = (paths, relativePath, title ?? string.Empty, terms, anchorIndex);
	}

	private async Task TrySetCitationsAsync(IPathResolver paths, string absolutePdfPath)
	{
		_ = 1;
		try
		{
			if (PdfView.CoreWebView2 == null)
			{
				return;
			}
			string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(absolutePdfPath);
			if (_citationsJsonCache.TryGetValue(fileNameWithoutExtension, out string value))
			{
				await PdfView.CoreWebView2.ExecuteScriptAsync("window.HB_setCitations && window.HB_setCitations(" + value + ");");
				return;
			}
			string citeDbPath = paths.CiteDbPath;
			Dictionary<string, List<Dictionary<string, object>>> dictionary = new Dictionary<string, List<Dictionary<string, object>>>();
			if (!string.IsNullOrEmpty(citeDbPath) && File.Exists(citeDbPath))
			{
				using SqliteConnection sqliteConnection = new SqliteConnection("Data Source=" + citeDbPath + ";Mode=ReadOnly");
				sqliteConnection.Open();
				bool? citeHasKindCol = _citeHasKindCol;
				if (!citeHasKindCol.HasValue)
				{
					using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
					sqliteCommand.CommandText = "SELECT 1 FROM pragma_table_info('Citations') WHERE name='kind' LIMIT 1";
					_citeHasKindCol = sqliteCommand.ExecuteScalar() != null;
				}
				bool value2 = _citeHasKindCol.Value;
				using SqliteCommand sqliteCommand2 = sqliteConnection.CreateCommand();
				sqliteCommand2.CommandText = (value2 ? "SELECT src_page,x0,y0,x1,y1,tgt_fid,tgt_page,ref,kind FROM Citations WHERE src_fid=$f" : "SELECT src_page,x0,y0,x1,y1,tgt_fid,tgt_page,ref FROM Citations WHERE src_fid=$f");
				sqliteCommand2.Parameters.AddWithValue("$f", fileNameWithoutExtension);
				using SqliteDataReader sqliteDataReader = sqliteCommand2.ExecuteReader();
				while (sqliteDataReader.Read())
				{
					string key = sqliteDataReader.GetInt32(0).ToString();
					if (!dictionary.TryGetValue(key, out var value3))
					{
						value3 = (dictionary[key] = new List<Dictionary<string, object>>());
					}
					string value4 = ((value2 && !sqliteDataReader.IsDBNull(8)) ? sqliteDataReader.GetString(8) : "daf");
					value3.Add(new Dictionary<string, object>
					{
						["fid"] = sqliteDataReader.GetString(5),
						["page"] = sqliteDataReader.GetInt32(6),
						["ref"] = (sqliteDataReader.IsDBNull(7) ? "" : sqliteDataReader.GetString(7)),
						["kind"] = value4,
						["box"] = new double[4]
						{
							sqliteDataReader.GetDouble(1),
							sqliteDataReader.GetDouble(2),
							sqliteDataReader.GetDouble(3),
							sqliteDataReader.GetDouble(4)
						}
					});
				}
			}
			string text = JsonSerializer.Serialize(dictionary);
			_citationsJsonCache[fileNameWithoutExtension] = text;
			await PdfView.CoreWebView2.ExecuteScriptAsync("window.HB_setCitations && window.HB_setCitations(" + text + ");");
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "PdfJsHost: set citations failed");
		}
	}

	private void OpenCrossRef(string fileId, int page, int srcPage)
	{
		OpenCrossRefAsync(fileId, page, srcPage);
	}

	private async Task OpenCrossRefAsync(string fileId, int page, int srcPage)
	{
		if (_lastPaths == null)
		{
			return;
		}
		try
		{
			string target = Path.Combine(_lastPaths.PdfsRoot, fileId + ".pdf");
			if (!(await EnsureCrossRefLocalAsync(fileId)))
			{
				Log.Warning("OpenCrossRef: target not found {Path}", target);
				return;
			}
			if (!string.IsNullOrEmpty(_currentPdfPath) && srcPage > 0)
			{
				_navHistory.Push((_currentPdfPath, srcPage));
			}
			await OpenAsync(_lastPaths, target, page, null, null, keepHistory: true);
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "OpenCrossRef failed for {FileId}", fileId);
		}
	}

	private async Task<bool> EnsureCrossRefLocalAsync(string fileId)
	{
		if (_lastPaths == null)
		{
			return false;
		}
		string target = Path.Combine(_lastPaths.PdfsRoot, fileId + ".pdf");
		if (File.Exists(target))
		{
			return true;
		}
		if (!int.TryParse(fileId, out var _))
		{
			return false;
		}
		try
		{
			OnDemandBookService onDemand = (OnDemandBookService)App.Services.GetService(typeof(OnDemandBookService));
			ICatalogRepository catalogRepository = (ICatalogRepository)App.Services.GetService(typeof(ICatalogRepository));
			string name = null;
			try
			{
				name = (await catalogRepository.GetByFileIdAsync(fileId))?.BookName;
			}
			catch
			{
			}
			Window window = Window.GetWindow(this);
			await onDemand.EnsureLocalAsync(new Book
			{
				FileID = fileId,
				BookName = name,
				Folder = null
			}, window);
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "EnsureCrossRefLocal failed for {FileId}", fileId);
		}
		return File.Exists(target);
	}

	private async Task OpenCrossRefInNewWindowAsync(string fileId, int page)
	{
		if (_lastPaths == null)
		{
			return;
		}
		try
		{
			string target = Path.Combine(_lastPaths.PdfsRoot, fileId + ".pdf");
			if (!(await EnsureCrossRefLocalAsync(fileId)))
			{
				Log.Warning("OpenCrossRefInNewWindow: target not found {Path}", target);
				return;
			}
			PdfViewerWindow window = (PdfViewerWindow)App.Services.GetService(typeof(PdfViewerWindow));
			window.Owner = Window.GetWindow(this);
			string title = fileId;
			try
			{
				Book book = await ((ICatalogRepository)App.Services.GetService(typeof(ICatalogRepository))).GetByFileIdAsync(fileId);
				if ((object)book != null && !string.IsNullOrWhiteSpace(book.BookName))
				{
					title = book.BookName;
				}
			}
			catch
			{
			}
			window.Show();
			await window.OpenAsync(fileId, title, target, null, null, page);
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "OpenCrossRefInNewWindow failed for {FileId}", fileId);
		}
	}

	private async Task HandleOpenBookAsync(string payloadJson)
	{
		if (_lastPaths == null || PdfView.CoreWebView2 == null)
		{
			return;
		}
		try
		{
			string title = "";
			int srcPage = 0;
			List<string> list = new List<string>();
			double[] anchorRect = null;
			bool newWindow = false;
			using (JsonDocument jsonDocument = JsonDocument.Parse(payloadJson))
			{
				JsonElement rootElement = jsonDocument.RootElement;
				if (rootElement.TryGetProperty("title", out var value))
				{
					title = value.GetString() ?? "";
				}
				if (rootElement.TryGetProperty("srcPage", out var value2))
				{
					srcPage = value2.GetInt32();
				}
				if (rootElement.TryGetProperty("newWindow", out var value3))
				{
					newWindow = value3.ValueKind == JsonValueKind.True;
				}
				if (rootElement.TryGetProperty("fids", out var value4) && value4.ValueKind == JsonValueKind.Array)
				{
					foreach (JsonElement item in value4.EnumerateArray())
					{
						string text = item.GetString();
						if (!string.IsNullOrWhiteSpace(text))
						{
							list.Add(text.Trim());
						}
					}
				}
				if (rootElement.TryGetProperty("anchorRect", out var value5) && value5.ValueKind == JsonValueKind.Array)
				{
					anchorRect = (from e in value5.EnumerateArray()
						select e.GetDouble()).ToArray();
				}
			}
			List<string> list2 = list.Distinct().ToList();
			if (list2.Count == 0)
			{
				Log.Information("hb-open-book: no candidates for '{Title}'", title);
				return;
			}
			if (list2.Count == 1)
			{
				if (newWindow)
				{
					OpenCrossRefInNewWindowAsync(list2[0], 1);
				}
				else
				{
					OpenCrossRef(list2[0], 1, srcPage);
				}
				return;
			}
			ICatalogRepository catalog = (ICatalogRepository)App.Services.GetService(typeof(ICatalogRepository));
			List<object> rows = new List<object>(list2.Count);
			foreach (string fid in list2)
			{
				Book b = null;
				try
				{
					b = await catalog.GetByFileIdAsync(fid);
				}
				catch
				{
				}
				rows.Add(new
				{
					fid = fid,
					name = (b?.BookName ?? fid),
					author = (b?.AuthorName ?? ""),
					year = (b?.PrintYear ?? ""),
					place = (b?.PrintPlace ?? "")
				});
			}
			string text2 = JsonSerializer.Serialize(new
			{
				title = title,
				candidates = rows,
				srcPage = srcPage,
				anchorRect = anchorRect,
				newWindow = newWindow
			});
			await PdfView.CoreWebView2.ExecuteScriptAsync("window.HB_showBookPicker && window.HB_showBookPicker(" + text2 + ");");
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "hb-open-book handler failed");
		}
	}

	private void GoBack()
	{
		if (_lastPaths == null || _navHistory.Count == 0)
		{
			return;
		}
		var (absolutePdfPath, page) = _navHistory.Pop();
		try
		{
			OpenAsync(_lastPaths, absolutePdfPath, page, null, null, keepHistory: true);
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "GoBack failed");
		}
	}

	private Task SetBackVisibleAsync()
	{
		if (PdfView.CoreWebView2 == null)
		{
			return Task.CompletedTask;
		}
		return PdfView.CoreWebView2.ExecuteScriptAsync("window.HB_setBackVisible && window.HB_setBackVisible(" + ((_navHistory.Count > 0) ? "true" : "false") + ");");
	}

	private static bool TryMakeRelative(string root, string absolutePath, out string relative)
	{
		relative = string.Empty;
		if (string.IsNullOrEmpty(root))
		{
			return false;
		}
		string text;
		string fullPath;
		try
		{
			text = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			fullPath = Path.GetFullPath(absolutePath);
		}
		catch
		{
			return false;
		}
		if (fullPath.Length <= text.Length)
		{
			return false;
		}
		if (!fullPath.StartsWith(text, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		char c = fullPath[text.Length];
		if (c != Path.DirectorySeparatorChar && c != Path.AltDirectorySeparatorChar)
		{
			return false;
		}
		relative = fullPath.Substring(text.Length + 1).Replace('\\', '/');
		return true;
	}

	public void ShowLoading(bool show)
	{
		LoadingOverlay.Visibility = ((!show) ? Visibility.Collapsed : Visibility.Visible);
	}

	public Task ClearAsync()
	{
		if (PdfView.CoreWebView2 == null)
		{
			return Task.CompletedTask;
		}
		return PdfView.CoreWebView2.ExecuteScriptAsync("window.HB_clearDocument && window.HB_clearDocument();");
	}

	public Task StartRegionCopyAsync()
	{
		if (!_viewerLoaded || PdfView.CoreWebView2 == null || _currentMode != ViewerMode.Pdf)
		{
			return Task.CompletedTask;
		}
		return PdfView.CoreWebView2.ExecuteScriptAsync("window.HB_startRegionCopy && window.HB_startRegionCopy();");
	}

	public Task StartRegionCopyTextAsync()
	{
		if (!_viewerLoaded || PdfView.CoreWebView2 == null || _currentMode != ViewerMode.Pdf)
		{
			return Task.CompletedTask;
		}
		return PdfView.CoreWebView2.ExecuteScriptAsync("window.HB_startRegionCopyText && window.HB_startRegionCopyText();");
	}

	public Task NextTextHitAsync()
	{
		if (!_textViewInited || TextView.CoreWebView2 == null || _currentMode != ViewerMode.Text)
		{
			return Task.CompletedTask;
		}
		return TextView.CoreWebView2.ExecuteScriptAsync("window.HB_nextHit && window.HB_nextHit();");
	}

	public Task PrevTextHitAsync()
	{
		if (!_textViewInited || TextView.CoreWebView2 == null || _currentMode != ViewerMode.Text)
		{
			return Task.CompletedTask;
		}
		return TextView.CoreWebView2.ExecuteScriptAsync("window.HB_prevHit && window.HB_prevHit();");
	}

	public Task GoToPageAsync(int page)
	{
		if (ActiveView.CoreWebView2 == null)
		{
			return Task.CompletedTask;
		}
		string javaScript = $"window.HB_goToPage && window.HB_goToPage({page});";
		return ActiveView.CoreWebView2.ExecuteScriptAsync(javaScript);
	}

	public Task SetBookTocAsync(IReadOnlyList<TocEntry> entries)
	{
		if (!_viewerLoaded || PdfView.CoreWebView2 == null || _currentMode != ViewerMode.Pdf)
		{
			return Task.CompletedTask;
		}
		string text = JsonSerializer.Serialize(entries);
		string javaScript = "window.HB_setBookToc && window.HB_setBookToc(" + text + ");";
		return PdfView.CoreWebView2.ExecuteScriptAsync(javaScript);
	}

	public async Task<int> GetCurrentPageAsync()
	{
		if (!_viewerLoaded || PdfView.CoreWebView2 == null || _currentMode != ViewerMode.Pdf)
		{
			return 0;
		}
		int result;
		return int.TryParse((await PdfView.CoreWebView2.ExecuteScriptAsync("String(Number((window.HB_getCurrentPage && window.HB_getCurrentPage()) || 0))"))?.Trim('"') ?? "0", out result) ? result : 0;
	}

	public Task SetHighlightXmlAsync(string? highlightXml, IReadOnlyList<string>? matchedTerms = null)
	{
		if (!_viewerLoaded || PdfView.CoreWebView2 == null)
		{
			return Task.CompletedTask;
		}
		string value = JsonSerializer.Serialize(highlightXml ?? string.Empty);
		string value2 = JsonSerializer.Serialize(matchedTerms?.Where((string t) => !string.IsNullOrWhiteSpace(t)).ToArray() ?? Array.Empty<string>());
		string javaScript = $"if (typeof window.HB_setHighlightXml === 'function') window.HB_setHighlightXml({value}, {value2});";
		return PdfView.CoreWebView2.ExecuteScriptAsync(javaScript);
	}

	private async void TryPrintCurrentPdf()
	{
		if (PdfView.CoreWebView2 == null)
		{
			return;
		}
		try
		{
			string text = await PdfView.CoreWebView2.ExecuteScriptAsync("String(Number((window.HB_getPageCount && window.HB_getPageCount()) || 0))");
			if (!int.TryParse(text?.Trim('"') ?? "0", out var totalPages) || totalPages <= 0)
			{
				Log.Warning("Print: viewer reported no pages ({Json})", text);
				return;
			}
			int result;
			int currentPage = ((!int.TryParse((await PdfView.CoreWebView2.ExecuteScriptAsync("String(Number((window.HB_getCurrentPage && window.HB_getCurrentPage()) || 1))"))?.Trim('"') ?? "1", out result)) ? 1 : Math.Clamp(result, 1, totalPages));
			PrintWindow printWindow = new PrintWindow(totalPages, RenderPdfPageForPrintAsync, currentPage, _currentPdfPath);
			Window window = Window.GetWindow(this);
			if (window != null)
			{
				printWindow.Owner = window;
			}
			printWindow.Show();
		}
		catch (Exception ex)
		{
			Log.Error(ex, "Print failed");
			HebrewMessageBox.Show(SharedStrings.S9058 + ex.Message, SharedStrings.S558, MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private async Task TryPrintCurrentTextAsync()
	{
		if (TextView.CoreWebView2 == null)
		{
			return;
		}
		(IPathResolver paths, string relativePath, string title, IReadOnlyList<string>? terms, int? anchor)? restoreState = _textRestoreState;
		if (!restoreState.HasValue)
		{
			Log.Warning("Print(Text): no text-restore state — refusing to print");
			return;
		}
		Directory.CreateDirectory(PrintCacheDir);
		string fileName = $"hb-textprint-{Guid.NewGuid():N}.pdf";
		string tempPdf = Path.Combine(PrintCacheDir, fileName);
		try
		{
			await TextView.CoreWebView2.ExecuteScriptAsync("window.HB_preparePrint && window.HB_preparePrint();");
			await Task.Delay(150);
			if (!(await TextView.CoreWebView2.PrintToPdfAsync(tempPdf)) || !File.Exists(tempPdf))
			{
				Log.Warning("Print(Text): PrintToPdfAsync returned false / no file");
				TextView.CoreWebView2.ExecuteScriptAsync("window.HB_endPrint && window.HB_endPrint();");
				return;
			}
			await EnsureWebViewInitializedAsync(restoreState.Value.paths);
			await EnsureViewerPageLoadedAsync(ViewerMode.Pdf);
			SetActiveView(ViewerMode.Pdf);
			string value = JsonSerializer.Serialize("https://printcache.local/" + fileName);
			string value2 = (App.IsProtectMode ? "true" : "false");
			await PdfView.CoreWebView2.ExecuteScriptAsync($"window.HB_setProtectMode && window.HB_setProtectMode({value2}); window.HB_loadPdf && window.HB_loadPdf({value}, 1, '', []);");
			int totalPages = 0;
			for (int i = 0; i < 60; i++)
			{
				await Task.Delay(100);
				if (int.TryParse((await PdfView.CoreWebView2.ExecuteScriptAsync("String(Number((window.HB_getPageCount && window.HB_getPageCount()) || 0))"))?.Trim('"') ?? "0", out var result) && result > 0)
				{
					totalPages = result;
					break;
				}
			}
			if (totalPages <= 0)
			{
				Log.Warning("Print(Text): temp PDF didn't load (no page count)");
				await RestoreTextViewAsync(restoreState.Value);
				try
				{
					File.Delete(tempPdf);
					return;
				}
				catch
				{
					return;
				}
			}
			PrintWindow printWindow = new PrintWindow(totalPages, RenderPdfPageForPrintAsync, 1, tempPdf);
			Window window = Window.GetWindow(this);
			if (window != null)
			{
				printWindow.Owner = window;
			}
			printWindow.Closed += async delegate
			{
				try
				{
					await RestoreTextViewAsync(restoreState.Value);
				}
				catch (Exception exception)
				{
					Log.Warning(exception, "Print(Text): restore failed");
				}
				try
				{
					File.Delete(tempPdf);
				}
				catch
				{
				}
			};
			printWindow.Show();
		}
		catch (Exception ex)
		{
			Log.Error(ex, "Print(Text) failed");
			try
			{
				File.Delete(tempPdf);
			}
			catch
			{
			}
			HebrewMessageBox.Show(SharedStrings.S9058 + ex.Message, SharedStrings.S558, MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private Task RestoreTextViewAsync((IPathResolver paths, string relativePath, string title, IReadOnlyList<string>? terms, int? anchor) state)
	{
		return OpenTextAsync(state.paths, state.relativePath, state.title, state.terms, state.anchor);
	}

	public async Task<BitmapSource?> RenderPdfPageForPrintAsync(int page, int dpi)
	{
		if (PdfView.CoreWebView2 == null)
		{
			return null;
		}
		await _printRenderLock.WaitAsync();
		try
		{
			TaskCompletionSource<string?> tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
			_pendingPrintPage = page;
			_pendingPrintRender = tcs;
			string javaScript = $"(async () => {{  let url = null;  try {{ if (window.HB_renderPageForPrint) url = await window.HB_renderPageForPrint({page}, {dpi}); }}  catch (e) {{ url = null; }}  try {{ window.chrome.webview.postMessage('hb-pagedata:{page}:' + (url || '')); }} catch (e) {{}}}})()";
			PdfView.CoreWebView2.ExecuteScriptAsync(javaScript);
			string dataUrl = null;
			if (await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(20.0))) == tcs.Task)
			{
				dataUrl = tcs.Task.Result;
			}
			_pendingPrintRender = null;
			if (string.IsNullOrEmpty(dataUrl))
			{
				Log.Warning("Print p{Page}: no data url returned (timeout/render failure)", page);
				return null;
			}
			int num = dataUrl.IndexOf(',');
			if (!dataUrl.StartsWith("data:image/", StringComparison.Ordinal) || num < 0)
			{
				Log.Warning("Print p{Page}: not a data URL", page);
				return null;
			}
			byte[] buffer;
			try
			{
				string text = dataUrl;
				int num2 = num + 1;
				buffer = Convert.FromBase64String(text.Substring(num2, text.Length - num2));
			}
			catch (Exception exception)
			{
				Log.Warning(exception, "Print p{Page}: base64 decode failed", page);
				return null;
			}
			BitmapImage bitmapImage = new BitmapImage();
			using (MemoryStream streamSource = new MemoryStream(buffer))
			{
				bitmapImage.BeginInit();
				bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
				bitmapImage.StreamSource = streamSource;
				bitmapImage.EndInit();
			}
			bitmapImage.Freeze();
			return bitmapImage;
		}
		finally
		{
			_printRenderLock.Release();
		}
	}

	private async Task EnsureWebViewInitializedAsync(IPathResolver paths)
	{
		CoreWebView2Environment environment = await WebViewEnvironment.GetAsync().ConfigureAwait(continueOnCapturedContext: true);
		await PdfView.EnsureCoreWebView2Async(environment);
		try
		{
			PdfView.CoreWebView2.Settings.IsZoomControlEnabled = false;
		}
		catch
		{
		}
		if (App.IsProtectMode)
		{
			try
			{
				CoreWebView2Settings settings = PdfView.CoreWebView2.Settings;
				settings.AreDevToolsEnabled = false;
				settings.AreDefaultContextMenusEnabled = false;
				settings.AreBrowserAcceleratorKeysEnabled = false;
			}
			catch
			{
			}
		}
		if (_viewerLoaded)
		{
			return;
		}
		if (_viewerReady == null)
		{
			_viewerReady = new TaskCompletionSource<bool>();
			try
			{
				string pdfjsDir = Path.Combine(AppContext.BaseDirectory, "pdfjs");
				if (!Directory.Exists(pdfjsDir))
				{
					throw new DirectoryNotFoundException("PDF.js assets missing at '" + pdfjsDir + "'. Check lib/pdfjs is copied to the bin output.");
				}
				string customViewerDir = Path.Combine(AppContext.BaseDirectory, "Resources", "PdfViewer", "custom");
				if (!Directory.Exists(customViewerDir))
				{
					throw new DirectoryNotFoundException("Custom viewer assets missing at '" + customViewerDir + "'. Check the csproj copies Resources\\PdfViewer\\custom to bin.");
				}
				PdfView.CoreWebView2.SetVirtualHostNameToFolderMapping("pdfjs.local", pdfjsDir, CoreWebView2HostResourceAccessKind.Allow);
				PdfView.CoreWebView2.SetVirtualHostNameToFolderMapping("viewer.local", customViewerDir, CoreWebView2HostResourceAccessKind.Allow);
				try
				{
					Directory.CreateDirectory(paths.PdfsRoot);
				}
				catch
				{
				}
				TryMapHost("books.local", paths.PdfsRoot);
				Directory.CreateDirectory(MarkedDir);
				TryMapHost("marks.local", MarkedDir);
				Directory.CreateDirectory(PrintCacheDir);
				TryMapHost("printcache.local", PrintCacheDir);
				TryMapHost("otzraya.local", paths.OtzrayaRoot);
				TryMapHost("personal.local", paths.PersonalRoot);
				PdfView.CoreWebView2.AddWebResourceRequestedFilter("https://pdfjs.local/*", CoreWebView2WebResourceContext.All);
				PdfView.CoreWebView2.AddWebResourceRequestedFilter("https://viewer.local/*", CoreWebView2WebResourceContext.All);
				PdfView.CoreWebView2.WebResourceRequested += delegate(object? _, CoreWebView2WebResourceRequestedEventArgs args)
				{
					try
					{
						Uri uri = new Uri(args.Request.Uri);
						string host = uri.Host;
						string text = ((host == "pdfjs.local") ? pdfjsDir : ((!(host == "viewer.local")) ? null : customViewerDir));
						string text2 = text;
						if (text2 != null)
						{
							string text3 = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant() switch
							{
								".mjs" => "text/javascript; charset=utf-8", 
								".js" => "text/javascript; charset=utf-8", 
								".map" => "application/json; charset=utf-8", 
								_ => null, 
							};
							if (text3 != null)
							{
								string path = Path.Combine(text2, Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')).Replace('/', Path.DirectorySeparatorChar));
								if (File.Exists(path))
								{
									FileStream content = File.OpenRead(path);
									args.Response = PdfView.CoreWebView2.Environment.CreateWebResourceResponse(content, 200, "OK", "Content-Type: " + text3);
								}
							}
						}
					}
					catch
					{
					}
				};
				_bridgeReady = new TaskCompletionSource<bool>();
				PdfView.CoreWebView2.WebMessageReceived += delegate(object? _, CoreWebView2WebMessageReceivedEventArgs args)
				{
					OnHostWebMessage(args, isText: false);
				};
				_viewerLoaded = true;
				_viewerReady.TrySetResult(result: true);
				return;
			}
			catch (Exception exception)
			{
				_viewerReady.TrySetException(exception);
				_viewerReady = null;
				throw;
			}
		}
		await _viewerReady.Task;
		void TryMapHost(string host, string folder)
		{
			if (!string.IsNullOrEmpty(folder))
			{
				if (Directory.Exists(folder))
				{
					try
					{
						PdfView.CoreWebView2.SetVirtualHostNameToFolderMapping(host, folder, CoreWebView2HostResourceAccessKind.Allow);
						return;
					}
					catch (Exception exception2)
					{
						Log.Warning(exception2, "WebView2: failed to map {Host} → {Folder}", host, folder);
						return;
					}
				}
				Log.Information("WebView2: skipping {Host} → {Folder} (folder not found; opens from there will 404 until it exists)", host, folder);
			}
		}
	}

	private void OnHostWebMessage(CoreWebView2WebMessageReceivedEventArgs args, bool isText)
	{
		try
		{
			string text = args.TryGetWebMessageAsString();
			if (string.Equals(text, "hb-viewer-ready", StringComparison.Ordinal))
			{
				if (isText)
				{
					_textBridgeReady?.TrySetResult(result: true);
				}
				else
				{
					_bridgeReady?.TrySetResult(result: true);
				}
			}
			else if (string.Equals(text, "hb-loaded", StringComparison.Ordinal))
			{
				ShowLoading(show: false);
			}
			else if (string.Equals(text, "hb-print", StringComparison.Ordinal))
			{
				if (_currentMode == ViewerMode.Text)
				{
					TryPrintCurrentTextAsync();
				}
				else
				{
					TryPrintCurrentPdf();
				}
			}
			else if (string.Equals(text, "hb-immersive-toggle", StringComparison.Ordinal))
			{
				this.ImmersiveToggleRequested?.Invoke(this, EventArgs.Empty);
			}
			else if (string.Equals(text, "hb-immersive-exit", StringComparison.Ordinal))
			{
				this.ImmersiveExitRequested?.Invoke(this, EventArgs.Empty);
			}
			else if (string.Equals(text, "hb-toc-add", StringComparison.Ordinal))
			{
				if (!App.IsProtectMode)
				{
					this.TocQuickAddRequested?.Invoke(this, EventArgs.Empty);
				}
			}
			else if (string.Equals(text, "hb-toc-edit", StringComparison.Ordinal))
			{
				if (!App.IsProtectMode)
				{
					this.TocEditRequested?.Invoke(this, EventArgs.Empty);
				}
			}
			else if (text != null && text.StartsWith("hb-pagedata:", StringComparison.Ordinal))
			{
				string text2 = text.Substring("hb-pagedata:".Length);
				int num = text2.IndexOf(':');
				if (num > 0 && int.TryParse(text2.AsSpan(0, num), out var result) && result == _pendingPrintPage)
				{
					string text3 = text2;
					int num2 = num + 1;
					string text4 = text3.Substring(num2, text3.Length - num2);
					_pendingPrintRender?.TrySetResult(string.IsNullOrEmpty(text4) ? null : text4);
				}
			}
			else if (text != null && text.StartsWith("hb-highlight-progress:", StringComparison.Ordinal))
			{
				if (int.TryParse(text.Substring("hb-highlight-progress:".Length), out var result2))
				{
					this.HighlightProgressChanged?.Invoke(this, result2);
				}
			}
			else if (text != null && text.StartsWith("hb-page:", StringComparison.Ordinal))
			{
				if (int.TryParse(text.Substring("hb-page:".Length), out var result3) && result3 > 0)
				{
					this.CurrentPageChanged?.Invoke(this, result3);
				}
			}
			else if (text != null && text.StartsWith("hb-verified-hit-pages:", StringComparison.Ordinal))
			{
				int result7;
				List<int> list = (from s in text.Substring("hb-verified-hit-pages:".Length).Split(',', StringSplitOptions.RemoveEmptyEntries)
					select int.TryParse(s, out result7) ? result7 : 0 into n
					where n > 0
					select n).ToList();
				if (list.Count > 0)
				{
					this.VerifiedHitPagesReceived?.Invoke(this, list);
				}
			}
			else if (text != null && text.StartsWith("hb-fuzzy-final-pages:", StringComparison.Ordinal))
			{
				int result7;
				List<int> e = (from s in text.Substring("hb-fuzzy-final-pages:".Length).Split(',', StringSplitOptions.RemoveEmptyEntries)
					select int.TryParse(s, out result7) ? result7 : 0 into n
					where n > 0
					select n).ToList();
				this.FuzzyFinalPagesReceived?.Invoke(this, e);
			}
			else if (text != null && text.StartsWith("hb-shortcut:", StringComparison.Ordinal))
			{
				this.ShortcutRequested?.Invoke(this, text.Substring("hb-shortcut:".Length));
			}
			else if (string.Equals(text, "hb-chrome-show", StringComparison.Ordinal))
			{
				this.ChromeRevealRequested?.Invoke(this, e: true);
			}
			else if (string.Equals(text, "hb-chrome-hide", StringComparison.Ordinal))
			{
				this.ChromeRevealRequested?.Invoke(this, e: false);
			}
			else if (text != null && text.StartsWith("hb-open-ref-new:", StringComparison.Ordinal))
			{
				string[] array = text.Substring("hb-open-ref-new:".Length).Split(':');
				if (array.Length >= 2 && int.TryParse(array[1], out var result4))
				{
					OpenCrossRefInNewWindowAsync(array[0], result4);
				}
			}
			else if (text != null && text.StartsWith("hb-open-ref:", StringComparison.Ordinal))
			{
				string[] array2 = text.Substring("hb-open-ref:".Length).Split(':');
				if (array2.Length >= 2 && int.TryParse(array2[1], out var result5))
				{
					int result6;
					int srcPage = ((array2.Length >= 3 && int.TryParse(array2[2], out result6)) ? result6 : 0);
					OpenCrossRef(array2[0], result5, srcPage);
				}
			}
			else if (text != null && text.StartsWith("hb-open-book:", StringComparison.Ordinal))
			{
				HandleOpenBookAsync(text.Substring("hb-open-book:".Length));
			}
			else if (string.Equals(text, "hb-nav-back", StringComparison.Ordinal))
			{
				GoBack();
			}
			else if (text != null && text.StartsWith("hb-debug:", StringComparison.Ordinal))
			{
				Log.Debug("PdfJsHost JS: {Message}", text.Substring("hb-debug:".Length));
			}
		}
		catch
		{
		}
	}

	private async Task EnsureTextViewInitializedAsync(IPathResolver paths)
	{
		if (_textViewInited)
		{
			return;
		}
		if (_textViewReady == null)
		{
			_textViewReady = new TaskCompletionSource<bool>();
			try
			{
				CoreWebView2Environment environment = await WebViewEnvironment.GetAsync().ConfigureAwait(continueOnCapturedContext: true);
				await TextView.EnsureCoreWebView2Async(environment);
				try
				{
					TextView.CoreWebView2.Settings.IsZoomControlEnabled = false;
				}
				catch
				{
				}
				if (App.IsProtectMode)
				{
					try
					{
						CoreWebView2Settings settings = TextView.CoreWebView2.Settings;
						settings.AreDevToolsEnabled = false;
						settings.AreDefaultContextMenusEnabled = false;
						settings.AreBrowserAcceleratorKeysEnabled = false;
					}
					catch
					{
					}
				}
				string customViewerDir = Path.Combine(AppContext.BaseDirectory, "Resources", "PdfViewer", "custom");
				if (!Directory.Exists(customViewerDir))
				{
					throw new DirectoryNotFoundException("Custom viewer assets missing at '" + customViewerDir + "'.");
				}
				TextView.CoreWebView2.SetVirtualHostNameToFolderMapping("viewer.local", customViewerDir, CoreWebView2HostResourceAccessKind.Allow);
				if (!string.IsNullOrEmpty(paths.OtzrayaRoot) && Directory.Exists(paths.OtzrayaRoot))
				{
					try
					{
						TextView.CoreWebView2.SetVirtualHostNameToFolderMapping("otzraya.local", paths.OtzrayaRoot, CoreWebView2HostResourceAccessKind.Allow);
					}
					catch (Exception exception)
					{
						Log.Warning(exception, "TextView: failed to map otzraya.local → {Folder}", paths.OtzrayaRoot);
					}
				}
				else
				{
					Log.Information("TextView: skipping otzraya.local (folder not found at {Folder})", paths.OtzrayaRoot);
				}
				TextView.CoreWebView2.AddWebResourceRequestedFilter("https://viewer.local/*", CoreWebView2WebResourceContext.All);
				TextView.CoreWebView2.WebResourceRequested += delegate(object? _, CoreWebView2WebResourceRequestedEventArgs args)
				{
					try
					{
						Uri uri = new Uri(args.Request.Uri);
						if (string.Equals(uri.Host, "viewer.local", StringComparison.Ordinal))
						{
							string text = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant() switch
							{
								".mjs" => "text/javascript; charset=utf-8", 
								".js" => "text/javascript; charset=utf-8", 
								".map" => "application/json; charset=utf-8", 
								_ => null, 
							};
							if (text != null)
							{
								string path = Path.Combine(customViewerDir, Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')).Replace('/', Path.DirectorySeparatorChar));
								if (File.Exists(path))
								{
									FileStream content = File.OpenRead(path);
									args.Response = TextView.CoreWebView2.Environment.CreateWebResourceResponse(content, 200, "OK", "Content-Type: " + text);
								}
							}
						}
					}
					catch
					{
					}
				};
				_textBridgeReady = new TaskCompletionSource<bool>();
				TextView.CoreWebView2.WebMessageReceived += delegate(object? _, CoreWebView2WebMessageReceivedEventArgs args)
				{
					OnHostWebMessage(args, isText: true);
				};
				_textViewInited = true;
				_textViewReady.TrySetResult(result: true);
				return;
			}
			catch (Exception exception2)
			{
				_textViewReady.TrySetException(exception2);
				_textViewReady = null;
				throw;
			}
		}
		await _textViewReady.Task;
	}

	private async Task<bool> EnsureTextPageLoadedAsync(CancellationToken ct = default(CancellationToken))
	{
		if (_textPageLoaded)
		{
			return true;
		}
		if (ct.IsCancellationRequested)
		{
			return false;
		}
		_textBridgeReady = new TaskCompletionSource<bool>();
		TaskCompletionSource<bool> navTcs = new TaskCompletionSource<bool>();
		TextView.CoreWebView2.NavigationCompleted += OnNav;
		TextView.CoreWebView2.Navigate("https://viewer.local/text-viewer.html");
		try
		{
			await navTcs.Task.WaitAsync(ct);
		}
		catch (OperationCanceledException)
		{
			TextView.CoreWebView2.NavigationCompleted -= OnNav;
			return false;
		}
		await Task.WhenAny(_textBridgeReady.Task, Task.Delay(TimeSpan.FromSeconds(8.0), ct));
		if (ct.IsCancellationRequested)
		{
			return false;
		}
		_textPageLoaded = true;
		try
		{
			string text = (App.IsProtectMode ? "true" : "false");
			await TextView.CoreWebView2.ExecuteScriptAsync("window.HB_setProtectMode && window.HB_setProtectMode(" + text + ");");
		}
		catch
		{
		}
		await PushViewerThemeAsync(TextView, CurrentViewerThemeJson);
		return true;
		void OnNav(object? s, CoreWebView2NavigationCompletedEventArgs ev)
		{
			TextView.CoreWebView2.NavigationCompleted -= OnNav;
			navTcs.TrySetResult(ev.IsSuccess);
		}
	}

	private async Task<bool> EnsureViewerPageLoadedAsync(ViewerMode mode, CancellationToken ct = default(CancellationToken))
	{
		if (_pdfPageLoaded)
		{
			return true;
		}
		if (ct.IsCancellationRequested)
		{
			return false;
		}
		_bridgeReady = new TaskCompletionSource<bool>();
		TaskCompletionSource<bool> navTcs = new TaskCompletionSource<bool>();
		PdfView.CoreWebView2.NavigationCompleted += OnNav;
		PdfView.CoreWebView2.Navigate("https://viewer.local/viewer.html");
		try
		{
			await navTcs.Task.WaitAsync(ct);
		}
		catch (OperationCanceledException)
		{
			PdfView.CoreWebView2.NavigationCompleted -= OnNav;
			return false;
		}
		await Task.WhenAny(_bridgeReady.Task, Task.Delay(TimeSpan.FromSeconds(8.0), ct));
		if (ct.IsCancellationRequested)
		{
			return false;
		}
		_pdfPageLoaded = true;
		string currentHighlightColor = CurrentHighlightColor;
		if (!string.IsNullOrWhiteSpace(currentHighlightColor))
		{
			string text = JsonSerializer.Serialize(currentHighlightColor);
			try
			{
				await PdfView.CoreWebView2.ExecuteScriptAsync("window.HB_setHighlightColor && window.HB_setHighlightColor(" + text + ");");
			}
			catch
			{
			}
		}
		try
		{
			string text2 = (CurrentPageRailEnabled ? "true" : "false");
			await PdfView.CoreWebView2.ExecuteScriptAsync("window.HB_setPageRailEnabled && window.HB_setPageRailEnabled(" + text2 + ");");
		}
		catch
		{
		}
		await PushViewerThemeAsync(PdfView, CurrentViewerThemeJson);
		return true;
		void OnNav(object? s, CoreWebView2NavigationCompletedEventArgs ev)
		{
			PdfView.CoreWebView2.NavigationCompleted -= OnNav;
			navTcs.TrySetResult(ev.IsSuccess);
		}
	}

	public void Dispose()
	{
		try
		{
			PdfView?.Dispose();
		}
		catch
		{
		}
		try
		{
			TextView?.Dispose();
		}
		catch
		{
		}
	}


}
