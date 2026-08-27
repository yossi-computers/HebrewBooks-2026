using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace HebrewBooks.UI.Services;

public static class AutoHideTaskbarFix
{
	private struct POINT
	{
		public int x;

		public int y;
	}

	private struct RECT
	{
		public int left;

		public int top;

		public int right;

		public int bottom;
	}

	private struct MINMAXINFO
	{
		public POINT ptReserved;

		public POINT ptMaxSize;

		public POINT ptMaxPosition;

		public POINT ptMinTrackSize;

		public POINT ptMaxTrackSize;
	}

	private struct MONITORINFO
	{
		public int cbSize;

		public RECT rcMonitor;

		public RECT rcWork;

		public int dwFlags;
	}

	private struct APPBARDATA
	{
		public int cbSize;

		public nint hWnd;

		public uint uCallbackMessage;

		public uint uEdge;

		public RECT rc;

		public nint lParam;
	}

	private const int WM_GETMINMAXINFO = 36;

	private const int MONITOR_DEFAULTTONEAREST = 2;

	private const int ABE_LEFT = 0;

	private const int ABE_TOP = 1;

	private const int ABE_RIGHT = 2;

	private const int ABE_BOTTOM = 3;

	private const uint ABM_GETSTATE = 4u;

	private const uint ABM_GETTASKBARPOS = 5u;

	private const long ABS_AUTOHIDE = 1L;

	public static void Apply(Window window)
	{
		if (window == null)
		{
			return;
		}
		if (PresentationSource.FromVisual(window) is HwndSource)
		{
			Hook(window);
			return;
		}
		window.SourceInitialized += delegate
		{
			Hook(window);
		};
	}

	private static void Hook(Window window)
	{
		if (PresentationSource.FromVisual(window) is HwndSource hwndSource)
		{
			hwndSource.AddHook(WndProc);
		}
	}

	private static nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
	{
		if (msg != 36)
		{
			return IntPtr.Zero;
		}
		try
		{
			nint num = MonitorFromWindow(hwnd, 2);
			if (num == IntPtr.Zero)
			{
				return IntPtr.Zero;
			}
			MONITORINFO mi = new MONITORINFO
			{
				cbSize = Marshal.SizeOf<MONITORINFO>()
			};
			if (!GetMonitorInfo(num, ref mi))
			{
				return IntPtr.Zero;
			}
			if (!TryGetAutoHideEdge(mi.rcMonitor, out var edge))
			{
				return IntPtr.Zero;
			}
			MINMAXINFO structure = Marshal.PtrToStructure<MINMAXINFO>(lParam);
			structure.ptMaxPosition.x = mi.rcWork.left - mi.rcMonitor.left;
			structure.ptMaxPosition.y = mi.rcWork.top - mi.rcMonitor.top;
			structure.ptMaxSize.x = mi.rcWork.right - mi.rcWork.left;
			structure.ptMaxSize.y = mi.rcWork.bottom - mi.rcWork.top;
			switch (edge)
			{
			case 3:
				structure.ptMaxSize.y--;
				break;
			case 1:
				structure.ptMaxSize.y--;
				structure.ptMaxPosition.y++;
				break;
			case 0:
				structure.ptMaxSize.x--;
				structure.ptMaxPosition.x++;
				break;
			case 2:
				structure.ptMaxSize.x--;
				break;
			}
			Marshal.StructureToPtr(structure, lParam, fDeleteOld: true);
			handled = true;
		}
		catch
		{
		}
		return IntPtr.Zero;
	}

	private static bool TryGetAutoHideEdge(RECT monitor, out int edge)
	{
		edge = 3;
		APPBARDATA data = new APPBARDATA
		{
			cbSize = Marshal.SizeOf<APPBARDATA>()
		};
		if ((((IntPtr)SHAppBarMessage(4u, ref data)).ToInt64() & 1) == 0L)
		{
			return false;
		}
		APPBARDATA data2 = new APPBARDATA
		{
			cbSize = Marshal.SizeOf<APPBARDATA>()
		};
		if (SHAppBarMessage(5u, ref data2) == IntPtr.Zero)
		{
			return false;
		}
		if (!Intersects(data2.rc, monitor))
		{
			return false;
		}
		edge = (int)data2.uEdge;
		return true;
	}

	private static bool Intersects(RECT a, RECT b)
	{
		if (a.left < b.right && a.right > b.left && a.top < b.bottom)
		{
			return a.bottom > b.top;
		}
		return false;
	}

	[DllImport("user32.dll")]
	private static extern nint MonitorFromWindow(nint hwnd, int flags);

	[DllImport("user32.dll")]
	private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO mi);

	[DllImport("shell32.dll")]
	private static extern nint SHAppBarMessage(uint msg, ref APPBARDATA data);
}
