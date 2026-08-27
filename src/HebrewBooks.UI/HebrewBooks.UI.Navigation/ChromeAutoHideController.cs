using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using HebrewBooks.UI.Controls;
using HebrewBooks.UI.Resources;
using HebrewBooks.UI.ViewModels;
using Wpf.Ui.Controls;

namespace HebrewBooks.UI.Navigation;

public sealed class ChromeAutoHideController
{
	private struct NativePoint
	{
		public int X;

		public int Y;
	}

	private static readonly TimeSpan HideDelay = TimeSpan.FromMilliseconds(2500.0);

	private static readonly Duration AnimDuration = new Duration(TimeSpan.FromMilliseconds(170.0));

	private static readonly IEasingFunction AnimEase = new CubicEase
	{
		EasingMode = EasingMode.EaseOut
	};

	private readonly MainViewModel _main;

	private readonly INotifyPropertyChanged _surfaceVm;

	private readonly Func<bool> _bookOpen;

	private readonly FrameworkElement _chromeBar;

	private readonly PdfJsHost _host;

	private readonly ButtonBase _pinButton;

	private readonly SymbolIcon _pinIcon;

	private readonly TextBoxBase _inBookBox;

	private readonly DispatcherTimer _hideTimer;

	private bool _hover;

	private bool _focus;

	private bool _revealed;

	private bool _detached;

	private double _naturalHeight;

	private bool? _shown;

	private int _animSeq;

	public ChromeAutoHideController(MainViewModel main, INotifyPropertyChanged surfaceVm, Func<bool> bookOpen, FrameworkElement chromeBar, PdfJsHost host, ButtonBase pinButton, SymbolIcon pinIcon, TextBoxBase inBookBox)
	{
		_main = main;
		_surfaceVm = surfaceVm;
		_bookOpen = bookOpen;
		_chromeBar = chromeBar;
		_host = host;
		_pinButton = pinButton;
		_pinIcon = pinIcon;
		_inBookBox = inBookBox;
		_hideTimer = new DispatcherTimer
		{
			Interval = HideDelay
		};
		_hideTimer.Tick += OnHideTick;
		_chromeBar.ClipToBounds = true;
		_chromeBar.SizeChanged += OnBarSizeChanged;
		_pinButton.Click += OnPinClick;
		_main.PropertyChanged += OnMainChanged;
		_surfaceVm.PropertyChanged += OnSurfaceChanged;
		_host.ChromeRevealRequested += OnHostReveal;
		_chromeBar.MouseEnter += OnBarMouseEnter;
		_chromeBar.MouseLeave += OnBarMouseLeave;
		_inBookBox.GotKeyboardFocus += OnBoxGotFocus;
		_inBookBox.LostKeyboardFocus += OnBoxLostFocus;
		UpdatePinVisual();
		Recompute();
	}

	public void Reveal()
	{
		_revealed = true;
		StopTimer();
		Recompute();
	}

	public void Detach()
	{
		if (!_detached)
		{
			_detached = true;
			StopTimer();
			_hideTimer.Tick -= OnHideTick;
			_pinButton.Click -= OnPinClick;
			_main.PropertyChanged -= OnMainChanged;
			_surfaceVm.PropertyChanged -= OnSurfaceChanged;
			_host.ChromeRevealRequested -= OnHostReveal;
			_chromeBar.MouseEnter -= OnBarMouseEnter;
			_chromeBar.MouseLeave -= OnBarMouseLeave;
			_inBookBox.GotKeyboardFocus -= OnBoxGotFocus;
			_inBookBox.LostKeyboardFocus -= OnBoxLostFocus;
			_chromeBar.SizeChanged -= OnBarSizeChanged;
			_chromeBar.BeginAnimation(FrameworkElement.HeightProperty, null);
			_chromeBar.Height = double.NaN;
		}
	}

	private void OnPinClick(object sender, RoutedEventArgs e)
	{
		_main.ToggleChromeAutoHideCommand.Execute(null);
	}

	private void OnMainChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (!(e.PropertyName != "ChromeAutoHide"))
		{
			UpdatePinVisual();
			if (_main.ChromeAutoHide)
			{
				_revealed = true;
				RestartTimer();
			}
			else
			{
				StopTimer();
				_revealed = (_hover = (_focus = false));
			}
			Recompute();
		}
	}

	private void OnSurfaceChanged(object? sender, PropertyChangedEventArgs e)
	{
		Recompute();
	}

	private void OnHostReveal(object? sender, bool near)
	{
		if (near)
		{
			_revealed = true;
			StopTimer();
			Recompute();
		}
		else
		{
			RestartTimer();
		}
	}

	private void OnBarMouseEnter(object sender, EventArgs e)
	{
		_hover = true;
		StopTimer();
		Recompute();
	}

	private void OnBarMouseLeave(object sender, EventArgs e)
	{
		_hover = false;
		RestartTimer();
	}

	private void OnBoxGotFocus(object sender, EventArgs e)
	{
		_focus = true;
		StopTimer();
		Recompute();
	}

	private void OnBoxLostFocus(object sender, EventArgs e)
	{
		_focus = false;
		RestartTimer();
	}

	private void OnHideTick(object? sender, EventArgs e)
	{
		StopTimer();
		if (!_hover && !_focus)
		{
			if (CursorIsOverBar())
			{
				_hover = true;
				RestartTimer();
			}
			else
			{
				_revealed = false;
				Recompute();
			}
		}
	}

	private bool CursorIsOverBar()
	{
		if (!_chromeBar.IsVisible)
		{
			return false;
		}
		if (_chromeBar.ActualWidth <= 0.0 || _chromeBar.ActualHeight <= 0.0)
		{
			return false;
		}
		try
		{
			if (!GetCursorPos(out var lpPoint))
			{
				return false;
			}
			Point point = _chromeBar.PointFromScreen(new Point(lpPoint.X, lpPoint.Y));
			return point.X >= 0.0 && point.Y >= 0.0 && point.X <= _chromeBar.ActualWidth && point.Y <= _chromeBar.ActualHeight;
		}
		catch
		{
			return false;
		}
	}

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetCursorPos(out NativePoint lpPoint);

	private void RestartTimer()
	{
		if (_main.ChromeAutoHide)
		{
			_hideTimer.Stop();
			_hideTimer.Start();
		}
	}

	private void StopTimer()
	{
		_hideTimer.Stop();
	}

	private void Recompute()
	{
		bool show;
		bool animate;
		if (!_bookOpen())
		{
			show = false;
			animate = false;
		}
		else if (!_main.ChromeAutoHide)
		{
			show = true;
			animate = false;
		}
		else
		{
			show = _hover || _focus || _revealed;
			animate = true;
		}
		ApplyBarState(show, animate);
	}

	private void ApplyBarState(bool show, bool animate)
	{
		if (_shown == show)
		{
			return;
		}
		_shown = show;
		int seq = ++_animSeq;
		if (animate && _naturalHeight <= 0.0)
		{
			animate = false;
		}
		if (show)
		{
			_chromeBar.Visibility = Visibility.Visible;
			if (!animate)
			{
				_chromeBar.BeginAnimation(FrameworkElement.HeightProperty, null);
				_chromeBar.Height = double.NaN;
				return;
			}
			DoubleAnimation doubleAnimation = new DoubleAnimation(_chromeBar.ActualHeight, _naturalHeight, AnimDuration)
			{
				EasingFunction = AnimEase
			};
			doubleAnimation.Completed += delegate
			{
				if (seq == _animSeq)
				{
					_chromeBar.BeginAnimation(FrameworkElement.HeightProperty, null);
					_chromeBar.Height = double.NaN;
				}
			};
			_chromeBar.BeginAnimation(FrameworkElement.HeightProperty, doubleAnimation);
			return;
		}
		if (!animate)
		{
			_chromeBar.BeginAnimation(FrameworkElement.HeightProperty, null);
			_chromeBar.Height = double.NaN;
			_chromeBar.Visibility = Visibility.Collapsed;
			return;
		}
		double actualHeight = _chromeBar.ActualHeight;
		if (actualHeight > 0.0)
		{
			_naturalHeight = actualHeight;
		}
		DoubleAnimation doubleAnimation2 = new DoubleAnimation((actualHeight > 0.0) ? actualHeight : _naturalHeight, 0.0, AnimDuration)
		{
			EasingFunction = AnimEase
		};
		doubleAnimation2.Completed += delegate
		{
			if (seq == _animSeq)
			{
				_chromeBar.Visibility = Visibility.Collapsed;
				_chromeBar.BeginAnimation(FrameworkElement.HeightProperty, null);
				_chromeBar.Height = double.NaN;
			}
		};
		_chromeBar.BeginAnimation(FrameworkElement.HeightProperty, doubleAnimation2);
	}

	private void OnBarSizeChanged(object sender, SizeChangedEventArgs e)
	{
		if (_chromeBar.Visibility == Visibility.Visible && double.IsNaN(_chromeBar.Height) && _chromeBar.ActualHeight > 0.0)
		{
			_naturalHeight = _chromeBar.ActualHeight;
		}
	}

	private void UpdatePinVisual()
	{
		if (_main.ChromeAutoHide)
		{
			_pinIcon.Symbol = SymbolRegular.PinOff24;
			_pinButton.ToolTip = SharedStrings.S573;
		}
		else
		{
			_pinIcon.Symbol = SymbolRegular.Pin24;
			_pinButton.ToolTip = SharedStrings.S574;
		}
	}
}
