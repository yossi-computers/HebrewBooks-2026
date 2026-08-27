using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using Serilog;

namespace HebrewBooks.UI.Services;

public sealed class TabletModeService
{
	private const int SM_CONVERTIBLESLATEMODE = 8195;

	private const int SM_MAXIMUMTOUCHES = 95;

	private const int WM_SETTINGCHANGE = 26;

	public bool IsTabletMode { get; private set; }

	public bool HasTouch { get; private set; }

	public event Action<bool>? Changed;

	[DllImport("user32.dll")]
	private static extern int GetSystemMetrics(int nIndex);

	public void Attach(Window window)
	{
		HasTouch = Safe(() => GetSystemMetrics(95)) > 0;
		IsTabletMode = Probe();
		Log.Information("TabletMode: initial={Mode} hasTouch={Touch}", IsTabletMode, HasTouch);
		if (PresentationSource.FromVisual(window) is HwndSource hwndSource)
		{
			hwndSource.AddHook(WndProc);
		}
	}

	private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
	{
		if (msg == 26)
		{
			Refresh();
		}
		return IntPtr.Zero;
	}

	private void Refresh()
	{
		bool flag = Probe();
		if (flag == IsTabletMode)
		{
			return;
		}
		IsTabletMode = flag;
		Log.Information("TabletMode: changed to {Mode}", flag);
		try
		{
			this.Changed?.Invoke(flag);
		}
		catch (Exception exception)
		{
			Log.Warning(exception, "TabletMode: a Changed handler threw");
		}
	}

	private bool Probe()
	{
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\ImmersiveShell");
			if (registryKey?.GetValue("TabletMode") is int num)
			{
				return num == 1;
			}
		}
		catch
		{
		}
		if (HasTouch && Safe(() => GetSystemMetrics(8195), -1) == 0)
		{
			return true;
		}
		return false;
	}

	private static int Safe(Func<int> f, int fallback = 0)
	{
		try
		{
			return f();
		}
		catch
		{
			return fallback;
		}
	}
}
