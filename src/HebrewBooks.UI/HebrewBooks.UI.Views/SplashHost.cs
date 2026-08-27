using System;
using System.Threading;
using System.Windows.Threading;

namespace HebrewBooks.UI.Views;

internal sealed class SplashHost
{
	private SplashWindow? _window;

	private Dispatcher? _dispatcher;

	private volatile bool _closeRequested;

	public void Show()
	{
		Thread thread = new Thread((ThreadStart)delegate
		{
			_dispatcher = Dispatcher.CurrentDispatcher;
			_window = new SplashWindow();
			if (!_closeRequested)
			{
				_window.Show();
				Dispatcher.Run();
			}
		});
		thread.IsBackground = true;
		thread.Name = "SplashUI";
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
	}

	public void SetStatus(string text)
	{
		try
		{
			_dispatcher?.BeginInvoke((Action)delegate
			{
				_window?.SetStatus(text);
			});
		}
		catch
		{
		}
	}

	public void Close()
	{
		_closeRequested = true;
		try
		{
			_dispatcher?.BeginInvoke((Action)delegate
			{
				try
				{
					_window?.Close();
				}
				catch
				{
				}
				_dispatcher.InvokeShutdown();
			});
		}
		catch
		{
		}
	}
}
