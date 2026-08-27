using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace HebrewBooks.UI.Printing;

public static class PdfNupImposer
{
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate int WriteBlock(nint pThis, nint pData, uint size);

	private struct FPDF_FILEWRITE
	{
		public int version;

		public WriteBlock WriteBlock;
	}

	private static bool _inited;

	private static readonly object _initLock = new object();

	private const CallingConvention CC = CallingConvention.StdCall;

	public static void Impose(string srcPath, string outPath, int n, NupOrderMode order, int fromPage, int toPage)
	{
		if (n < 1)
		{
			n = 1;
		}
		EnsureInit();
		nint num = FPDF_LoadDocument(srcPath, null);
		if (num == IntPtr.Zero)
		{
			throw new InvalidOperationException($"PDFium failed to open the PDF (err {FPDF_GetLastError()}).");
		}
		nint num2 = IntPtr.Zero;
		nint num3 = IntPtr.Zero;
		try
		{
			int val = FPDF_GetPageCount(num);
			int num4 = Math.Clamp(fromPage, 1, Math.Max(1, val));
			int num5 = Math.Clamp(toPage, 1, Math.Max(1, val));
			if (num5 < num4)
			{
				int num6 = num5;
				num5 = num4;
				num4 = num6;
			}
			if (!FPDF_GetPageSizeByIndex(num, num4 - 1, out var width, out var height) || width <= 0.0 || height <= 0.0)
			{
				width = 595.0;
				height = 842.0;
			}
			(int cols, int rows) tuple = GridFor(n);
			int item = tuple.cols;
			int item2 = tuple.rows;
			List<int> values = BuildRtlOrder(num4, num5, n, item, item2, order);
			num2 = FPDF_CreateNewDocument();
			if (num2 == IntPtr.Zero)
			{
				throw new InvalidOperationException("PDFium FPDF_CreateNewDocument failed.");
			}
			if (!FPDF_ImportPages(num2, num, string.Join(",", values), 0))
			{
				throw new InvalidOperationException("PDFium FPDF_ImportPages failed.");
			}
			num3 = FPDF_ImportNPagesToOne(num2, (float)((double)item * width), (float)((double)item2 * height), (uint)item, (uint)item2);
			if (num3 == IntPtr.Zero)
			{
				throw new InvalidOperationException("PDFium FPDF_ImportNPagesToOne failed.");
			}
			SaveTo(num3, outPath);
		}
		finally
		{
			if (num3 != IntPtr.Zero)
			{
				FPDF_CloseDocument(num3);
			}
			if (num2 != IntPtr.Zero)
			{
				FPDF_CloseDocument(num2);
			}
			FPDF_CloseDocument(num);
		}
	}

	private static List<int> BuildRtlOrder(int from, int to, int n, int cols, int rows, NupOrderMode order)
	{
		List<int> list = new List<int>(to - from + 1);
		List<int> list2 = new List<int>(n);
		for (int i = from; i <= to; i++)
		{
			list2.Add(i);
			if (list2.Count == n)
			{
				EmitSheet(list2, n, cols, rows, order, list);
				list2.Clear();
			}
		}
		if (list2.Count > 0)
		{
			EmitSheet(list2, list2.Count, cols, rows, order, list);
		}
		return list;
	}

	private static void EmitSheet(List<int> sheet, int count, int cols, int rows, NupOrderMode order, List<int> outList)
	{
		int[] array = new int[cols * rows];
		for (int i = 0; i < array.Length; i++)
		{
			array[i] = 0;
		}
		for (int j = 0; j < count; j++)
		{
			int num;
			int num2;
			switch (order)
			{
			case NupOrderMode.LeftToRightThenDown:
				num = j / cols;
				num2 = j % cols;
				break;
			case NupOrderMode.TopToBottomThenLeft:
				num = j % rows;
				num2 = cols - 1 - j / rows;
				break;
			case NupOrderMode.TopToBottomThenRight:
				num = j % rows;
				num2 = j / rows;
				break;
			default:
				num = j / cols;
				num2 = cols - 1 - j % cols;
				break;
			}
			array[num * cols + num2] = sheet[j];
		}
		int[] array2 = array;
		foreach (int num3 in array2)
		{
			if (num3 != 0)
			{
				outList.Add(num3);
			}
		}
	}

	private static void SaveTo(nint doc, string outPath)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(outPath));
		FileStream fs = File.Create(outPath);
		try
		{
			WriteBlock writeBlock = delegate(nint ignore, nint pData, uint size)
			{
				try
				{
					byte[] array = new byte[size];
					Marshal.Copy(pData, array, 0, (int)size);
					fs.Write(array, 0, (int)size);
					return 1;
				}
				catch
				{
					return 0;
				}
			};
			FPDF_FILEWRITE write = new FPDF_FILEWRITE
			{
				version = 1,
				WriteBlock = writeBlock
			};
			bool num = FPDF_SaveAsCopy(doc, ref write, 0u);
			GC.KeepAlive(writeBlock);
			if (!num)
			{
				throw new InvalidOperationException("PDFium FPDF_SaveAsCopy failed.");
			}
		}
		finally
		{
			if (fs != null)
			{
				((IDisposable)fs).Dispose();
			}
		}
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
	private static extern nint FPDF_LoadDocument(string file_path, string? password);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.StdCall)]
	private static extern int FPDF_GetPageCount(nint doc);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.StdCall)]
	private static extern nint FPDF_CreateNewDocument();

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
	private static extern bool FPDF_ImportPages(nint dest, nint src, string pagerange, int index);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.StdCall)]
	private static extern nint FPDF_ImportNPagesToOne(nint src, float output_width, float output_height, nuint num_pages_x, nuint num_pages_y);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.StdCall)]
	private static extern bool FPDF_GetPageSizeByIndex(nint doc, int page_index, out double width, out double height);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.StdCall)]
	private static extern bool FPDF_SaveAsCopy(nint doc, ref FPDF_FILEWRITE write, uint flags);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.StdCall)]
	private static extern void FPDF_CloseDocument(nint doc);

	[DllImport("pdfium.dll", CallingConvention = CallingConvention.StdCall)]
	private static extern uint FPDF_GetLastError();
}
