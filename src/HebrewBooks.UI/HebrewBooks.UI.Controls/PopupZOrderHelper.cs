using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;

namespace HebrewBooks.UI.Controls;

internal static class PopupZOrderHelper
{
	private static readonly nint HWND_TOP = IntPtr.Zero;

	private const uint SWP_NOSIZE = 1u;

	private const uint SWP_NOMOVE = 2u;

	private const uint SWP_NOACTIVATE = 16u;

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

	public static void BringToFront(Popup? popup)
	{
		if (popup == null)
		{
			return;
		}
		try
		{
			Visual child = popup.Child;
			if (child != null && PresentationSource.FromVisual(child) is HwndSource hwndSource && hwndSource.Handle != IntPtr.Zero)
			{
				SetWindowPos(hwndSource.Handle, HWND_TOP, 0, 0, 0, 0, 19u);
			}
		}
		catch
		{
		}
	}
}
