using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace HebrewBooks.UI.Behaviors;

public static class TouchScroll
{
	private sealed class State
	{
		public int TouchId = -1;

		public Point LastPos;

		public int LastTime;

		public double RemainderPx;

		public bool Panning;

		public double Velocity;

		public bool InertiaRunning;

		public TimeSpan LastFrame;

		public EventHandler? FrameHandler;
	}

	private const double PanThreshold = 3.0;

	private const double FlingMinVelocity = 0.06;

	private const double FlingFriction = 0.96;

	private const double FlingStopVelocity = 0.02;

	private static readonly ConditionalWeakTable<ScrollViewer, State> States = new ConditionalWeakTable<ScrollViewer, State>();

	public static void Install()
	{
		EventManager.RegisterClassHandler(typeof(ScrollViewer), UIElement.PreviewTouchDownEvent, new EventHandler<TouchEventArgs>(OnDown));
		EventManager.RegisterClassHandler(typeof(ScrollViewer), UIElement.PreviewTouchMoveEvent, new EventHandler<TouchEventArgs>(OnMove));
		EventManager.RegisterClassHandler(typeof(ScrollViewer), UIElement.PreviewTouchUpEvent, new EventHandler<TouchEventArgs>(OnUp));
	}

	private static void OnDown(object? sender, TouchEventArgs e)
	{
		if (sender is ScrollViewer scrollViewer && scrollViewer == NearestVerticalScroller(e.OriginalSource as DependencyObject))
		{
			if (scrollViewer.PanningMode != PanningMode.None)
			{
				scrollViewer.PanningMode = PanningMode.None;
			}
			State orCreateValue = States.GetOrCreateValue(scrollViewer);
			StopInertia(orCreateValue);
			orCreateValue.TouchId = e.TouchDevice.Id;
			orCreateValue.LastPos = e.GetTouchPoint(scrollViewer).Position;
			orCreateValue.LastTime = e.Timestamp;
			orCreateValue.RemainderPx = 0.0;
			orCreateValue.Velocity = 0.0;
			orCreateValue.Panning = false;
		}
	}

	private static void OnMove(object? sender, TouchEventArgs e)
	{
		if (!(sender is ScrollViewer scrollViewer) || !States.TryGetValue(scrollViewer, out State value) || e.TouchDevice.Id != value.TouchId)
		{
			return;
		}
		Point position = e.GetTouchPoint(scrollViewer).Position;
		double num = position.X - value.LastPos.X;
		double num2 = position.Y - value.LastPos.Y;
		if (value.Panning || !(Math.Abs(num) < 3.0) || !(Math.Abs(num2) < 3.0))
		{
			value.Panning = true;
			int num3 = e.Timestamp - value.LastTime;
			if (num3 > 0)
			{
				double num4 = num2 / (double)num3;
				value.Velocity = ((value.Velocity == 0.0) ? num4 : (value.Velocity * 0.7 + num4 * 0.3));
			}
			value.LastPos = position;
			value.LastTime = e.Timestamp;
			ScrollBy(scrollViewer, value, num, num2);
			e.Handled = true;
		}
	}

	private static void OnUp(object? sender, TouchEventArgs e)
	{
		if (!(sender is ScrollViewer scrollViewer) || !States.TryGetValue(scrollViewer, out State value) || e.TouchDevice.Id != value.TouchId)
		{
			return;
		}
		value.TouchId = -1;
		if (value.Panning)
		{
			e.Handled = true;
			if (Math.Abs(value.Velocity) >= 0.06)
			{
				StartInertia(scrollViewer, value);
			}
		}
		value.Panning = false;
	}

	private static bool ScrollBy(ScrollViewer sv, State st, double dx, double dy)
	{
		bool result;
		if (sv.CanContentScroll)
		{
			double num = PixelsPerLine(sv);
			st.RemainderPx += dy;
			int num2 = (int)(st.RemainderPx / num);
			st.RemainderPx -= (double)num2 * num;
			double num3 = Math.Clamp(sv.VerticalOffset - (double)num2, 0.0, sv.ScrollableHeight);
			result = num2 == 0 || num3 != sv.VerticalOffset;
			if (num2 != 0)
			{
				sv.ScrollToVerticalOffset(num3);
			}
		}
		else
		{
			double num4 = Math.Clamp(sv.VerticalOffset - dy, 0.0, sv.ScrollableHeight);
			result = num4 != sv.VerticalOffset;
			sv.ScrollToVerticalOffset(num4);
		}
		if (sv.ScrollableWidth > 0.0)
		{
			sv.ScrollToHorizontalOffset(Math.Clamp(sv.HorizontalOffset - dx, 0.0, sv.ScrollableWidth));
		}
		return result;
	}

	private static double PixelsPerLine(ScrollViewer sv)
	{
		double viewportHeight = sv.ViewportHeight;
		if (viewportHeight > 0.0 && sv.ActualHeight > 0.0)
		{
			return sv.ActualHeight / viewportHeight;
		}
		return 32.0;
	}

	private static void StartInertia(ScrollViewer sv, State st)
	{
		if (st.InertiaRunning)
		{
			return;
		}
		st.InertiaRunning = true;
		st.LastFrame = TimeSpan.Zero;
		st.FrameHandler = delegate(object? _, EventArgs ev)
		{
			TimeSpan renderingTime = ((RenderingEventArgs)ev).RenderingTime;
			double num = ((st.LastFrame == TimeSpan.Zero) ? 16.7 : (renderingTime - st.LastFrame).TotalMilliseconds);
			st.LastFrame = renderingTime;
			if (!(num <= 0.0))
			{
				bool num2 = ScrollBy(sv, st, 0.0, st.Velocity * num);
				st.Velocity *= Math.Pow(0.96, num / 16.7);
				if (!num2 || Math.Abs(st.Velocity) < 0.02)
				{
					StopInertia(st);
				}
			}
		};
		CompositionTarget.Rendering += st.FrameHandler;
	}

	private static void StopInertia(State st)
	{
		if (st.InertiaRunning)
		{
			st.InertiaRunning = false;
			if (st.FrameHandler != null)
			{
				CompositionTarget.Rendering -= st.FrameHandler;
			}
			st.FrameHandler = null;
		}
	}

	private static ScrollViewer? NearestVerticalScroller(DependencyObject? d)
	{
		DependencyObject dependencyObject = d;
		while (dependencyObject != null)
		{
			if (dependencyObject is ScrollViewer { ScrollableHeight: >0.0 } scrollViewer)
			{
				return scrollViewer;
			}
			bool flag = ((dependencyObject is Visual || dependencyObject is Visual3D) ? true : false);
			dependencyObject = (flag ? VisualTreeHelper.GetParent(dependencyObject) : LogicalTreeHelper.GetParent(dependencyObject));
		}
		return null;
	}
}
