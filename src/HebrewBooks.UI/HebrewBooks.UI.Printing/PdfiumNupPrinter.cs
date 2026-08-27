using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.UI.Resources;
using PdfiumViewer;
using Serilog;

namespace HebrewBooks.UI.Printing;

public static class PdfiumNupPrinter
{
	private static readonly int[] FallbackRungs = new int[7] { 500, 400, 300, 250, 200, 160, 120 };

	public const int DefaultDpi = 300;

	private const int MaxDpi = 600;

	private const double DipToHundredthIn = 1.0416666666666667;

	private static int[] LadderFrom(int dpi)
	{
		List<int> list = new List<int> { dpi };
		int[] fallbackRungs = FallbackRungs;
		foreach (int num in fallbackRungs)
		{
			if (num < dpi)
			{
				list.Add(num);
			}
		}
		return list.ToArray();
	}

	public static void Print(string pdfPath, string printerName, int fromPage, int toPage, int copies, NupPrintSettings settings, bool landscape, int dpi = 0, Action<int, int>? onProgress = null, CancellationToken cancel = default(CancellationToken))
	{
		int n = ((settings.PagesPerSheet < 1) ? 1 : settings.PagesPerSheet);
		var (cols, rows) = GridFor(n);
		PdfDocument doc;
		List<int> pages;
		double cell0W;
		double cell0H;
		long docLoadMs;
		double[] aspects;
		int renderDpi;
		using (PrintDocument printDocument = new PrintDocument
		{
			DocumentName = "HebrewBooks"
		})
		{
			printDocument.PrinterSettings.PrinterName = printerName;
			if (!printDocument.PrinterSettings.IsValid)
			{
				throw new InvalidOperationException(SharedStrings.S2008 + printerName);
			}
			printDocument.PrinterSettings.Copies = (short)Math.Clamp(copies, 1, 99);
			printDocument.DefaultPageSettings.Landscape = landscape;
			printDocument.OriginAtMargins = false;
			printDocument.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
			Stopwatch stopwatch = Stopwatch.StartNew();
			PdfiumTileRenderer tiler = null;
			doc = null;
			try
			{
				tiler = new PdfiumTileRenderer(pdfPath);
			}
			catch (Exception exception)
			{
				Log.Warning(exception, "Pdfium print: native open failed — falling back to managed PdfiumViewer load");
			}
			int num = ((tiler != null) ? tiler.PageCount : (doc = PdfDocument.Load(pdfPath)).PageCount);
			long elapsedMilliseconds = stopwatch.ElapsedMilliseconds;
			if (num <= 0)
			{
				tiler?.Dispose();
				doc?.Dispose();
				return;
			}
			int num2 = Math.Clamp(fromPage, 1, num);
			int num3 = Math.Clamp(toPage, 1, num);
			if (num3 < num2)
			{
				int num4 = num3;
				num3 = num2;
				num2 = num4;
			}
			pages = new List<int>();
			for (int i = num2; i <= num3; i++)
			{
				pages.Add(i - 1);
			}
			double m = ((n == 1) ? 0.0 : (settings.Margin * 1.0416666666666667));
			double gut = ((n == 1) ? 0.0 : (settings.Gutter * 1.0416666666666667));
			Rectangle bounds = printDocument.DefaultPageSettings.Bounds;
			cell0W = ((double)bounds.Width - 2.0 * m - (double)(cols - 1) * gut) / (double)cols;
			cell0H = ((double)bounds.Height - 2.0 * m - (double)(rows - 1) * gut) / (double)rows;
			docLoadMs = 0L;
			aspects = new double[pages.Count];
			for (int j = 0; j < pages.Count; j++)
			{
				double num5;
				double num6;
				if (tiler != null)
				{
					(num5, num6) = tiler.GetPageSize(pages[j]);
				}
				else
				{
					SizeF sizeF = EnsureDoc().PageSizes[pages[j]];
					num5 = sizeF.Width;
					num6 = sizeF.Height;
				}
				aspects[j] = ((num6 > 0.0) ? (num5 / num6) : 0.7);
			}
			long renderMs = 0L;
			long drawMs = 0L;
			Stopwatch stopwatch2 = Stopwatch.StartNew();
			int num7 = (renderDpi = GetPrinterDpi(printDocument));
			if (dpi > 0)
			{
				renderDpi = Math.Min(renderDpi, dpi);
			}
			renderDpi = Math.Clamp(renderDpi, 120, 600);
			long num8 = (long)(cell0W / 100.0 * (double)renderDpi) * (long)(cell0H / 100.0 * (double)renderDpi) * 4;
			bool useTiling = tiler != null && num8 > 25165824;
			bool usePipeline = !useTiling && num8 > 0 && num8 <= 12582912;
			int num9 = (int)((!usePipeline) ? 1 : Math.Clamp(50331648 / Math.Max(num8, 1L), 1L, 3L));
			BlockingCollection<Image?> buffer = null;
			Task task = null;
			if (usePipeline)
			{
				BlockingCollection<Image?> localBuffer = new BlockingCollection<Image>(num9);
				buffer = localBuffer;
				task = Task.Run(delegate
				{
					Stopwatch stopwatch3 = new Stopwatch();
					try
					{
						for (int k = 0; k < pages.Count; k++)
						{
							if (cancel.IsCancellationRequested)
							{
								break;
							}
							stopwatch3.Restart();
							Image image = RenderTile(k);
							renderMs += stopwatch3.ElapsedMilliseconds;
							try
							{
								localBuffer.Add(image, cancel);
							}
							catch (OperationCanceledException)
							{
								image?.Dispose();
								break;
							}
						}
					}
					catch (OperationCanceledException)
					{
					}
					finally
					{
						localBuffer.CompleteAdding();
					}
				});
			}
			int cursor = 0;
			printDocument.PrintPage += delegate(object _, PrintPageEventArgs e)
			{
				if (cancel.IsCancellationRequested)
				{
					e.Cancel = true;
				}
				else
				{
					Graphics graphics = e.Graphics;
					graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
					graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
					Rectangle marginBounds = e.MarginBounds;
					double num10 = ((double)marginBounds.Width - 2.0 * m - (double)(cols - 1) * gut) / (double)cols;
					double num11 = ((double)marginBounds.Height - 2.0 * m - (double)(rows - 1) * gut) / (double)rows;
					Stopwatch stopwatch3 = Stopwatch.StartNew();
					for (int k = 0; k < n && cursor + k < pages.Count; k++)
					{
						int num12 = cursor + k;
						Image image = null;
						if (usePipeline)
						{
							try
							{
								image = buffer.Take(cancel);
							}
							catch (Exception ex) when (((ex is OperationCanceledException || ex is InvalidOperationException) ? 1 : 0) != 0)
							{
								e.Cancel = true;
								return;
							}
						}
						else if (cancel.IsCancellationRequested)
						{
							e.Cancel = true;
							return;
						}
						using (image)
						{
							if (!(num10 <= 1.0) && !(num11 <= 1.0))
							{
								int num13;
								int num14;
								switch (settings.Order)
								{
								case NupOrderMode.LeftToRightThenDown:
									num13 = k / cols;
									num14 = k % cols;
									break;
								case NupOrderMode.TopToBottomThenLeft:
									num13 = k % rows;
									num14 = cols - 1 - k / rows;
									break;
								case NupOrderMode.TopToBottomThenRight:
									num13 = k % rows;
									num14 = k / rows;
									break;
								default:
									num13 = k / cols;
									num14 = cols - 1 - k % cols;
									break;
								}
								double num15 = (double)marginBounds.Left + m + (double)num14 * (num10 + gut);
								double num16 = (double)marginBounds.Top + m + (double)num13 * (num11 + gut);
								double num17 = aspects[num12];
								double num18 = num10;
								double num19 = num10 / num17;
								if (num19 > num11)
								{
									num19 = num11;
									num18 = num11 * num17;
								}
								double num20 = num15 + (num10 - num18) / 2.0;
								double num21 = num16 + (num11 - num19) / 2.0;
								RectangleF rectangleF = new RectangleF((float)num20, (float)num21, (float)num18, (float)num19);
								if (useTiling)
								{
									bool flag = false;
									try
									{
										flag = tiler.DrawPage(pages[num12], graphics, rectangleF, renderDpi, 25165824L);
									}
									catch (Exception exception2)
									{
										Log.Warning(exception2, "Pdfium print: tiled render failed for page {Page} — falling back to single bitmap", pages[num12] + 1);
									}
									if (!flag)
									{
										using Image image2 = RenderTile(num12);
										if (image2 != null)
										{
											graphics.DrawImage(image2, rectangleF);
										}
										else
										{
											Log.Warning("Pdfium print: page {Page} produced no raster at any DPI", pages[num12] + 1);
										}
									}
								}
								else if (usePipeline)
								{
									if (image != null)
									{
										graphics.DrawImage(image, rectangleF);
									}
									else
									{
										Log.Warning("Pdfium print: page {Page} produced no raster at any DPI", pages[num12] + 1);
									}
								}
								else
								{
									Stopwatch stopwatch4 = Stopwatch.StartNew();
									using Image image3 = RenderTile(num12);
									renderMs += stopwatch4.ElapsedMilliseconds;
									if (image3 != null)
									{
										graphics.DrawImage(image3, rectangleF);
									}
									else
									{
										Log.Warning("Pdfium print: page {Page} produced no raster at any DPI", pages[num12] + 1);
									}
								}
							}
						}
					}
					drawMs += stopwatch3.ElapsedMilliseconds;
					cursor += n;
					onProgress?.Invoke(Math.Min(cursor, pages.Count), pages.Count);
					e.HasMorePages = cursor < pages.Count;
				}
			};
			try
			{
				printDocument.Print();
			}
			finally
			{
				if (buffer != null)
				{
					foreach (Image item in buffer.GetConsumingEnumerable())
					{
						item?.Dispose();
					}
				}
				task?.GetAwaiter().GetResult();
				buffer?.Dispose();
				tiler?.Dispose();
				doc?.Dispose();
			}
			stopwatch2.Stop();
			bool isCancellationRequested = cancel.IsCancellationRequested;
			Log.Information("Pdfium print: {Done}/{Pages} pages{Cancelled}, render@{RenderDpi}dpi (printer {PrinterDpi}, ceiling {Ceiling}), load {LoadMs}ms (meta) + doc {DocLoadMs}ms + render {RenderMs}ms + draw/spool {DrawMs}ms, wall {WallMs}ms ({Mode}, cap {Cap}, ~{Mb}MB/page)", Math.Min(cursor, pages.Count), pages.Count, isCancellationRequested ? " (CANCELLED)" : "", renderDpi, num7, (dpi > 0) ? dpi.ToString() : "auto", elapsedMilliseconds, docLoadMs, renderMs, drawMs, stopwatch2.ElapsedMilliseconds, useTiling ? "tiled" : (usePipeline ? "pipelined" : "inline"), num9, num8 / 1048576);
		}
		PdfDocument EnsureDoc()
		{
			if (doc == null)
			{
				Stopwatch stopwatch3 = Stopwatch.StartNew();
				doc = PdfDocument.Load(pdfPath);
				docLoadMs += stopwatch3.ElapsedMilliseconds;
			}
			return doc;
		}
		Image? RenderTile(int num11)
		{
			double num10 = aspects[num11];
			double num12 = cell0W;
			double num13 = cell0W / num10;
			if (num13 > cell0H)
			{
				num13 = cell0H;
				num12 = cell0H * num10;
			}
			if (!(cell0W > 1.0) || !(cell0H > 1.0))
			{
				return null;
			}
			return RenderBestEffort(EnsureDoc(), pages[num11], num12 / 100.0, num13 / 100.0, renderDpi);
		}
	}

	private static int GetPrinterDpi(PrintDocument pd)
	{
		try
		{
			using Graphics graphics = pd.PrinterSettings.CreateMeasurementGraphics();
			if (graphics != null && graphics.DpiX >= 50f)
			{
				return (int)Math.Round(graphics.DpiX);
			}
		}
		catch
		{
		}
		return 300;
	}

	private static Image? RenderBestEffort(PdfDocument doc, int pageIndex, double fitWIn, double fitHIn, int startDpi)
	{
		int[] array = LadderFrom(startDpi);
		foreach (int num in array)
		{
			int num2 = Math.Max(1, (int)(fitWIn * (double)num));
			int num3 = Math.Max(1, (int)(fitHIn * (double)num));
			try
			{
				return doc.Render(pageIndex, num2, num3, num, num, PdfRenderFlags.ForPrinting | PdfRenderFlags.Annotations);
			}
			catch (Exception ex) when (((ex is ArgumentException || ex is OutOfMemoryException || ex is ExternalException) ? 1 : 0) != 0)
			{
				Log.Warning("Pdfium print: page {Page} at {Dpi}dpi ({W}x{H}) failed ({Msg}) — stepping down", pageIndex + 1, num, num2, num3, ex.Message);
			}
		}
		return null;
	}

	private static (int cols, int rows) GridFor(int n)
	{
		return n switch
		{
			2 => (cols: 2, rows: 1), 
			4 => (cols: 2, rows: 2), 
			6 => (cols: 2, rows: 3), 
			8 => (cols: 2, rows: 4), 
			9 => (cols: 3, rows: 3), 
			_ => Sqrtish(n), 
		};
	}

	private static (int cols, int rows) Sqrtish(int n)
	{
		int num = (int)Math.Ceiling(Math.Sqrt(n));
		return (cols: num, rows: (int)Math.Ceiling((double)n / (double)num));
	}
}
