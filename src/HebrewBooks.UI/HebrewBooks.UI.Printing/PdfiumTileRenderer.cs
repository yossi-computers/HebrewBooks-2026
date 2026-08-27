using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace HebrewBooks.UI.Printing;

public sealed class PdfiumTileRenderer : IDisposable
{
	private nint _doc;

	private const int FPDFBitmap_BGRA = 4;

	private const int FPDF_ANNOT = 1;

	private const int FPDF_PRINTING = 2048;

	private static bool _inited;

	private static readonly object _initLock = new object();

	private const CallingConvention CC = CallingConvention.StdCall;

	public int PageCount => FPDF_GetPageCount(_doc);

	public PdfiumTileRenderer(string pdfPath)
	{
		EnsureInit();
		_doc = FPDF_LoadDocument(pdfPath, null);
		if (_doc == IntPtr.Zero)
		{
			throw new InvalidOperationException($"PDFium failed to open '{pdfPath}' (err {FPDF_GetLastError()}).");
		}
	}

	public (double Width, double Height) GetPageSize(int pageIndex)
	{
		if (FPDF_GetPageSizeByIndex(_doc, pageIndex, out var width, out var height) == 0)
		{
			return (Width: 0.0, Height: 0.0);
		}
		return (Width: width, Height: height);
	}

	public bool DrawPage(int pageIndex, Graphics g, RectangleF dest, int dpi, long bandBudgetBytes)
	{
		nint num = FPDF_LoadPage(_doc, pageIndex);
		if (num == IntPtr.Zero)
		{
			return false;
		}
		try
		{
			int num2 = Math.Max(1, (int)((double)dest.Width / 100.0 * (double)dpi));
			int num3 = Math.Max(1, (int)((double)dest.Height / 100.0 * (double)dpi));
			int num4 = (int)Math.Max(1L, bandBudgetBytes / ((long)num2 * 4L));
			if (num4 > num3)
			{
				num4 = num3;
			}
			for (int i = 0; i < num3; i += num4)
			{
				int num5 = Math.Min(num4, num3 - i);
				nint num6 = FPDFBitmap_CreateEx(num2, num5, 4, IntPtr.Zero, 0);
				if (num6 == IntPtr.Zero)
				{
					continue;
				}
				try
				{
					FPDFBitmap_FillRect(num6, 0, 0, num2, num5, uint.MaxValue);
					FPDF_RenderPageBitmap(num6, num, 0, -i, num2, num3, 0, 2049);
					nint num7 = FPDFBitmap_GetBuffer(num6);
					if (num7 != IntPtr.Zero)
					{
						using Bitmap image = new Bitmap(num2, num5, num2 * 4, PixelFormat.Format32bppArgb, num7);
						float num8 = (float)i / (float)num3;
						float num9 = (float)(i + num5) / (float)num3;
						RectangleF rect = new RectangleF(dest.X, dest.Y + dest.Height * num8, dest.Width, dest.Height * (num9 - num8));
						g.DrawImage(image, rect);
					}
				}
				finally
				{
					FPDFBitmap_Destroy(num6);
				}
			}
			return true;
		}
		finally
		{
			FPDF_ClosePage(num);
		}
	}

	public void Dispose()
	{
		if (_doc != IntPtr.Zero)
		{
			FPDF_CloseDocument(_doc);
			_doc = IntPtr.Zero;
		}
	}

	private static void EnsureInit()
	{
		lock (_initLock)
		{
			if (!_inited)
			{
				string text = Path.Combine(AppContext.BaseDirectory, (IntPtr.Size == 4) ? "x86" : "x64", "pdfium.dll");
				if (File.Exists(text))
				{
					LoadLibrary(text);
				}
				FPDF_InitLibrary();
				_inited = true;
			}
		}
	}

	[DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern nint LoadLibrary(string lpFileName);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.StdCall)]
	private static extern void FPDF_InitLibrary();

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
	private static extern nint FPDF_LoadDocument(string path, string? password);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.StdCall)]
	private static extern void FPDF_CloseDocument(nint doc);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.StdCall)]
	private static extern int FPDF_GetPageCount(nint doc);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.StdCall)]
	private static extern int FPDF_GetPageSizeByIndex(nint doc, int page_index, out double width, out double height);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.StdCall)]
	private static extern uint FPDF_GetLastError();

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.StdCall)]
	private static extern nint FPDF_LoadPage(nint doc, int index);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.StdCall)]
	private static extern void FPDF_ClosePage(nint page);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.StdCall)]
	private static extern nint FPDFBitmap_CreateEx(int width, int height, int format, nint firstScan, int stride);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.StdCall)]
	private static extern void FPDFBitmap_FillRect(nint bitmap, int left, int top, int width, int height, uint color);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.StdCall)]
	private static extern void FPDF_RenderPageBitmap(nint bitmap, nint page, int startX, int startY, int sizeX, int sizeY, int rotate, int flags);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.StdCall)]
	private static extern nint FPDFBitmap_GetBuffer(nint bitmap);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.StdCall)]
	private static extern void FPDFBitmap_Destroy(nint bitmap);
}
