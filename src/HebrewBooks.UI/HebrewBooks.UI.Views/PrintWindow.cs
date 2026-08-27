using System;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Printing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HebrewBooks.Infrastructure.Settings;
using HebrewBooks.UI.Printing;
using HebrewBooks.UI.Resources;
using HebrewBooks.UI.Services;
using Microsoft.Win32;
using Serilog;
using Wpf.Ui.Controls;

namespace HebrewBooks.UI.Views;

public partial class PrintWindow : FluentWindow
{
	public sealed partial class SheetVm : INotifyPropertyChanged
	{
		private ImageSource? _image;

		private string _caption = "";

		public int[] Pages { get; init; } = Array.Empty<int>();

		public ImageSource? Image
		{
			get
			{
				return _image;
			}
			set
			{
				_image = value;
				this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Image"));
			}
		}

		public string Caption
		{
			get
			{
				return _caption;
			}
			set
			{
				_caption = value;
				this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Caption"));
			}
		}

		public event PropertyChangedEventHandler? PropertyChanged;
	}

	private sealed partial class PrinterItem
	{
		public string Name { get; init; } = "";

		public PrintQueue Queue { get; init; }

		public override string ToString()
		{
			return Name;
		}
	}

	private readonly int _totalPages;

	private readonly int _currentPage;

	private readonly Func<int, int, Task<BitmapSource?>> _renderPage;

	private readonly string? _pdfPath;

	private readonly Dictionary<(int page, int dpi), BitmapSource> _cache = new Dictionary<(int, int), BitmapSource>();

	private readonly HashSet<int> _rendering = new HashSet<int>();

	private const int PreviewDpi = 110;

	private const int PrintDpi = 300;

	private int _planGen;

	private bool _loaded;

	private bool _printing;

	private JsonSettingsStore? _settings;

	private CancellationTokenSource? _printCts;

	private static readonly SemaphoreSlim _printGate = new SemaphoreSlim(1, 1);


















	public ObservableCollection<SheetVm> Sheets { get; } = new ObservableCollection<SheetVm>();

	public PrintWindow(int totalPages, Func<int, int, Task<BitmapSource?>> renderPage, int currentPage = 1, string? pdfPath = null)
	{
		_totalPages = Math.Max(1, totalPages);
		_currentPage = Math.Clamp(currentPage, 1, _totalPages);
		_renderPage = renderPage;
		_pdfPath = pdfPath;
		InitializeComponent();
		if (App.IsProtectMode)
		{
			SavePdfBtn.Visibility = Visibility.Collapsed;
		}
		this.OpenAtScaledSize(1080.0, 780.0);
		base.DataContext = this;
		base.Loaded += OnLoaded;
		base.Closing += delegate
		{
			if (_printing)
			{
				_printCts?.Cancel();
			}
		};
	}

	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		if (_loaded)
		{
			return;
		}
		_loaded = true;
		List<PrinterItem> list = new List<PrinterItem>();
		PrinterItem printerItem = null;
		try
		{
			string text = null;
			try
			{
				text = LocalPrintServer.GetDefaultPrintQueue()?.FullName;
			}
			catch
			{
			}
			using LocalPrintServer localPrintServer = new LocalPrintServer();
			foreach (PrintQueue printQueue in localPrintServer.GetPrintQueues())
			{
				PrinterItem printerItem2 = new PrinterItem
				{
					Name = printQueue.FullName,
					Queue = printQueue
				};
				list.Add(printerItem2);
				if (printerItem == null && text != null && printQueue.FullName == text)
				{
					printerItem = printerItem2;
				}
			}
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "Print: enumerate printers failed");
		}
		PrinterBox.ItemsSource = list;
		PrinterBox.SelectedItem = printerItem ?? ((list.Count > 0) ? list[0] : null);
		FromBox.Maximum = _totalPages;
		ToBox.Maximum = _totalPages;
		FromBox.Value = _currentPage;
		ToBox.Value = _currentPage;
		RangeBox.SelectedIndex = 0;
		RangePanel.IsEnabled = false;
		PrinterBox.SelectionChanged += delegate
		{
			Replan();
		};
		NupBox.SelectionChanged += delegate
		{
			Replan();
		};
		OrientBox.SelectionChanged += delegate
		{
			Replan();
		};
		OrderBox.SelectionChanged += delegate
		{
			Replan();
		};
		RangeBox.SelectionChanged += delegate
		{
			RangePanel.IsEnabled = RangeBox.SelectedIndex == 1;
			Replan();
		};
		FromBox.LostFocus += delegate
		{
			Replan();
		};
		ToBox.LostFocus += delegate
		{
			Replan();
		};
		try
		{
			_settings = App.Services?.GetService(typeof(JsonSettingsStore)) as JsonSettingsStore;
			int num = _settings?.Load().View.PrintDpi ?? 0;
			foreach (object item in (IEnumerable)DpiBox.Items)
			{
				if (item is ComboBoxItem { Tag: var tag } comboBoxItem && int.TryParse(tag?.ToString(), out var result) && result == num)
				{
					DpiBox.SelectedItem = comboBoxItem;
					break;
				}
			}
		}
		catch (Exception exception2)
		{
			Log.Warning(exception2, "Print: restore DPI failed");
		}
		DpiBox.SelectionChanged += delegate
		{
			try
			{
				_settings?.Update(delegate(BookshelfOptions o)
				{
					o.View.PrintDpi = Dpi();
				});
			}
			catch (Exception exception3)
			{
				Log.Warning(exception3, "Print: persist DPI failed");
			}
		};
		Replan();
	}

	private (int from, int to) Range()
	{
		if (RangeBox.SelectedIndex != 1)
		{
			return (from: 1, to: _totalPages);
		}
		int num = (int)Math.Clamp(FromBox.Value ?? 1.0, 1.0, _totalPages);
		int num2 = (int)Math.Clamp(ToBox.Value ?? ((double)_totalPages), 1.0, _totalPages);
		if (num2 < num)
		{
			int num3 = num2;
			num2 = num;
			num = num3;
		}
		return (from: num, to: num2);
	}

	private int Nup()
	{
		if (!(NupBox.SelectedItem is ComboBoxItem { Tag: var tag }) || !int.TryParse(tag?.ToString(), out var result) || result < 1)
		{
			return 1;
		}
		return result;
	}

	private NupOrderMode Order()
	{
		if (!(OrderBox.SelectedItem is ComboBoxItem { Tag: var tag }) || !int.TryParse(tag?.ToString(), out var result) || !Enum.IsDefined(typeof(NupOrderMode), result))
		{
			return NupOrderMode.RightToLeftThenDown;
		}
		return (NupOrderMode)result;
	}

	private int Dpi()
	{
		if (!(DpiBox.SelectedItem is ComboBoxItem { Tag: var tag }) || !int.TryParse(tag?.ToString(), out var result) || result < 0)
		{
			return 0;
		}
		return result;
	}

	private NupPrintSettings Settings()
	{
		return new NupPrintSettings(Nup(), Order());
	}

	private (double w, double h) SheetSize()
	{
		double num = 793.0;
		double num2 = 1122.0;
		if (PrinterBox.SelectedItem is PrinterItem printerItem)
		{
			try
			{
				PageImageableArea pageImageableArea = printerItem.Queue.GetPrintCapabilities().PageImageableArea;
				if (pageImageableArea != null && pageImageableArea.ExtentWidth > 0.0 && pageImageableArea.ExtentHeight > 0.0)
				{
					num = pageImageableArea.ExtentWidth;
					num2 = pageImageableArea.ExtentHeight;
				}
			}
			catch
			{
			}
		}
		bool num3 = OrientBox.SelectedIndex == 1;
		if (num3 && num < num2)
		{
			double num4 = num2;
			num2 = num;
			num = num4;
		}
		if (!num3 && num2 < num)
		{
			double num5 = num2;
			num2 = num;
			num = num5;
		}
		return (w: num, h: num2);
	}

	private void Replan()
	{
		if (!_loaded)
		{
			return;
		}
		int gen = ++_planGen;
		_rendering.Clear();
		(int from, int to) tuple = Range();
		int item = tuple.from;
		int item2 = tuple.to;
		int num = Nup();
		Sheets.Clear();
		List<int> list = new List<int>(num);
		int num2 = 0;
		for (int i = item; i <= item2; i++)
		{
			list.Add(i);
			if (list.Count == num)
			{
				Sheets.Add(new SheetVm
				{
					Pages = list.ToArray(),
					Caption = $"{SharedStrings.S2372}{++num2}"
				});
				list.Clear();
			}
		}
		if (list.Count > 0)
		{
			Sheets.Add(new SheetVm
			{
				Pages = list.ToArray(),
				Caption = $"{SharedStrings.S2373}{++num2}"
			});
		}
		bool flag = Sheets.Count > 0 && PrinterBox.SelectedItem is PrinterItem;
		PrintBtn.IsEnabled = flag && !_printing;
		StatusText.Text = ((Sheets.Count == 0) ? SharedStrings.S1054 : $"{Sheets.Count}{SharedStrings.S2374}");
		LargeView.Source = null;
		if (Sheets.Count <= 0)
		{
			return;
		}
		int num3 = 0;
		for (int j = 0; j < Sheets.Count; j++)
		{
			if (Sheets[j].Pages.Contains(_currentPage))
			{
				num3 = j;
				break;
			}
			if (Sheets[j].Pages[0] <= _currentPage)
			{
				num3 = j;
			}
		}
		Thumbs.SelectedIndex = num3;
		Thumbs.ScrollIntoView(Sheets[num3]);
		RenderSheetAsync(num3, gen);
	}

	private void OnSheetSelected(object sender, SelectionChangedEventArgs e)
	{
		int selectedIndex = Thumbs.SelectedIndex;
		if (selectedIndex >= 0 && selectedIndex < Sheets.Count)
		{
			SheetVm sheetVm = Sheets[selectedIndex];
			if (sheetVm.Image != null)
			{
				LargeView.Source = sheetVm.Image;
				Busy.Visibility = Visibility.Collapsed;
			}
			else
			{
				RenderSheetAsync(selectedIndex, _planGen);
			}
		}
	}

	private async Task RenderSheetAsync(int idx, int gen)
	{
		if (idx < 0 || idx >= Sheets.Count)
		{
			return;
		}
		SheetVm s = Sheets[idx];
		if (s.Image != null || !_rendering.Add(idx))
		{
			return;
		}
		if (Thumbs.SelectedIndex == idx)
		{
			Busy.Visibility = Visibility.Visible;
		}
		try
		{
			List<BitmapSource> imgs = new List<BitmapSource>();
			int[] pages = s.Pages;
			foreach (int pg in pages)
			{
				if (gen != _planGen)
				{
					return;
				}
				if (!_cache.TryGetValue((pg, 110), out BitmapSource value))
				{
					value = await _renderPage(pg, 110);
					if (gen != _planGen)
					{
						return;
					}
					if (value != null)
					{
						_cache[(pg, 110)] = value;
					}
				}
				if (value != null)
				{
					imgs.Add(value);
				}
			}
			if (imgs.Count == 0)
			{
				return;
			}
			(double w, double h) tuple = SheetSize();
			double item = tuple.w;
			double item2 = tuple.h;
			FixedDocument fixedDocument = NupSheetComposer.BuildDocument(imgs, new Size(item, item2), Settings());
			if (fixedDocument.Pages.Count == 0 || gen != _planGen)
			{
				return;
			}
			using DocumentPage documentPage = fixedDocument.DocumentPaginator.GetPage(0);
			double num = documentPage.Size.Width;
			double num2 = documentPage.Size.Height;
			if (double.IsNaN(num) || num <= 0.0)
			{
				num = item;
			}
			if (double.IsNaN(num2) || num2 <= 0.0)
			{
				num2 = item2;
			}
			double num3 = 1000.0 / Math.Max(num, num2);
			int num4 = (int)Math.Ceiling(num * num3);
			int num5 = (int)Math.Ceiling(num2 * num3);
			DrawingVisual drawingVisual = new DrawingVisual();
			using (DrawingContext drawingContext = drawingVisual.RenderOpen())
			{
				Rect rectangle = new Rect(0.0, 0.0, num4, num5);
				drawingContext.DrawRectangle(Brushes.White, null, rectangle);
				drawingContext.DrawRectangle(new VisualBrush(documentPage.Visual)
				{
					Stretch = Stretch.Uniform
				}, null, rectangle);
			}
			RenderTargetBitmap renderTargetBitmap = new RenderTargetBitmap(num4, num5, 96.0, 96.0, PixelFormats.Pbgra32);
			renderTargetBitmap.Render(drawingVisual);
			renderTargetBitmap.Freeze();
			if (gen != _planGen)
			{
				return;
			}
			s.Image = renderTargetBitmap;
			if (Thumbs.SelectedIndex == idx)
			{
				LargeView.Source = renderTargetBitmap;
			}
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "Print preview: sheet {Idx} render failed", idx);
		}
		finally
		{
			_rendering.Remove(idx);
			if (Thumbs.SelectedIndex == idx)
			{
				Busy.Visibility = Visibility.Collapsed;
			}
		}
	}

	private async void OnPrint(object sender, RoutedEventArgs e)
	{
		if (_printing)
		{
			return;
		}
		object selectedItem = PrinterBox.SelectedItem;
		if (!(selectedItem is PrinterItem pi) || Sheets.Count == 0)
		{
			return;
		}
		_printing = true;
		PrintBtn.IsEnabled = false;
		Busy.Visibility = Visibility.Visible;
		_printCts?.Dispose();
		_printCts = new CancellationTokenSource();
		CancellationToken ct = _printCts.Token;
		bool gateHeld = false;
		try
		{
			(int, int) tuple = Range();
			int from = tuple.Item1;
			int to = tuple.Item2;
			int copies = (int)Math.Clamp(CopiesBox.Value ?? 1.0, 1.0, 99.0);
			StatusText.Text = SharedStrings.S1056;
			await _printGate.WaitAsync(ct);
			gateHeld = true;
			if (!string.IsNullOrEmpty(_pdfPath) && File.Exists(_pdfPath))
			{
				try
				{
					string path = _pdfPath;
					string printer = pi.Name;
					NupPrintSettings settings = Settings();
					bool landscape = OrientBox.SelectedIndex == 1;
					int dpi = Dpi();
					Dispatcher dispatcher = base.Dispatcher;
					Action<int, int> progress = delegate(int sent, int tot)
					{
						dispatcher.BeginInvoke((Func<string>)(() => StatusText.Text = $"{SharedStrings.S2375}{sent}{SharedStrings.S2376}{tot}{SharedStrings.S2377}"));
					};
					await Task.Run(delegate
					{
						PdfiumNupPrinter.Print(path, printer, from, to, copies, settings, landscape, dpi, progress, ct);
					}, ct);
					Log.Information("Print(Pdfium): p{From}-{To} x{Copies} n={N} dpi={Dpi} -> {Printer}", from, to, copies, settings.PagesPerSheet, dpi, pi.Name);
					Close();
					return;
				}
				catch (OperationCanceledException)
				{
					Log.Information("Print(Pdfium): cancelled by user");
					StatusText.Text = SharedStrings.S1058;
					return;
				}
				catch (Exception exception)
				{
					Log.Warning(exception, "Print(Pdfium) failed — falling back to raster path");
					StatusText.Text = SharedStrings.S1059;
				}
			}
			await PrintViaRasterAsync(pi, from, to, copies, Dpi(), ct);
		}
		catch (OperationCanceledException)
		{
			Log.Information("Print: cancelled by user");
		}
		catch (Exception ex3)
		{
			Log.Error(ex3, "Print: send to printer failed");
			StatusText.Text = SharedStrings.S9089 + ex3.Message;
		}
		finally
		{
			if (gateHeld)
			{
				_printGate.Release();
			}
			_printing = false;
			PrintBtn.IsEnabled = Sheets.Count > 0 && PrinterBox.SelectedItem is PrinterItem;
			Busy.Visibility = Visibility.Collapsed;
		}
	}

	private async Task PrintViaRasterAsync(PrinterItem pi, int from, int to, int copies, int dpi, CancellationToken ct)
	{
		if (dpi <= 0)
		{
			dpi = 300;
		}
		int total = to - from + 1;
		int done = 0;
		List<BitmapSource> imgs = new List<BitmapSource>();
		for (int p = from; p <= to; p++)
		{
			ct.ThrowIfCancellationRequested();
			done++;
			if (!_cache.TryGetValue((p, dpi), out BitmapSource value))
			{
				StatusText.Text = $"{SharedStrings.S2378}{done}{SharedStrings.S2379}{total}";
				value = await _renderPage(p, dpi);
				if (value != null)
				{
					_cache[(p, dpi)] = value;
				}
			}
			if (value != null)
			{
				imgs.Add(value);
			}
		}
		if (imgs.Count == 0)
		{
			StatusText.Text = SharedStrings.S1062;
			return;
		}
		(double w, double h) tuple = SheetSize();
		double item = tuple.w;
		double item2 = tuple.h;
		FixedDocument fixedDocument = NupSheetComposer.BuildDocument(imgs, new Size(item, item2), Settings());
		if (fixedDocument.Pages.Count == 0)
		{
			StatusText.Text = SharedStrings.S1063;
			return;
		}
		PrintDialog printDialog = new PrintDialog
		{
			PrintQueue = pi.Queue
		};
		try
		{
			PrintTicket printTicket = printDialog.PrintTicket;
			printTicket.CopyCount = copies;
			printDialog.PrintTicket = printTicket;
		}
		catch
		{
		}
		printDialog.PrintDocument(fixedDocument.DocumentPaginator, "HebrewBooks");
		Log.Information("Print(raster): {Sheets} sheets x{Copies} -> {Printer}", fixedDocument.Pages.Count, copies, pi.Name);
		Close();
	}

	private void OnCancel(object sender, RoutedEventArgs e)
	{
		Close();
	}

	private async void OnSavePdf(object sender, RoutedEventArgs e)
	{
		if (App.IsProtectMode || _printing)
		{
			return;
		}
		if (string.IsNullOrEmpty(_pdfPath) || !File.Exists(_pdfPath))
		{
			StatusText.Text = SharedStrings.S1064;
			return;
		}
		(int, int) tuple = Range();
		int from = tuple.Item1;
		int to = tuple.Item2;
		int n = Nup();
		NupOrderMode order = Order();
		SaveFileDialog saveFileDialog = new SaveFileDialog
		{
			Title = SharedStrings.S1065,
			Filter = "PDF|*.pdf",
			DefaultExt = ".pdf",
			FileName = $"{Path.GetFileNameWithoutExtension(_pdfPath)}-{n}up.pdf"
		};
		if (saveFileDialog.ShowDialog(this) != true)
		{
			return;
		}
		string outPath = saveFileDialog.FileName;
		_printing = true;
		SavePdfBtn.IsEnabled = false;
		PrintBtn.IsEnabled = false;
		Busy.Visibility = Visibility.Visible;
		StatusText.Text = SharedStrings.S1066;
		try
		{
			string src = _pdfPath;
			await Task.Run(delegate
			{
				PdfNupImposer.Impose(src, outPath, n, order, from, to);
			});
			StatusText.Text = SharedStrings.S9090 + outPath;
			Log.Information("Save N-up PDF: {From}-{To} n={N} -> {Out}", from, to, n, outPath);
			try
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = "explorer.exe",
					Arguments = "/select,\"" + outPath + "\"",
					UseShellExecute = true
				});
			}
			catch
			{
			}
			Close();
		}
		catch (Exception ex)
		{
			Log.Error(ex, "Save N-up PDF failed");
			StatusText.Text = SharedStrings.S9091 + ex.Message;
		}
		finally
		{
			_printing = false;
			SavePdfBtn.IsEnabled = true;
			PrintBtn.IsEnabled = Sheets.Count > 0 && PrinterBox.SelectedItem is PrinterItem;
			Busy.Visibility = Visibility.Collapsed;
		}
	}


}
