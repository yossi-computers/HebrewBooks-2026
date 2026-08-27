using System;
using System.Collections.Generic;
using System.Linq;

namespace HebrewBooks.UI.Navigation;

public sealed class NavigationHistory
{
	private const int MaxStackDepth = 50;

	private readonly Stack<NavigationEntry> _back = new Stack<NavigationEntry>();

	private readonly Stack<NavigationEntry> _forward = new Stack<NavigationEntry>();

	private NavigationEntry? _current;

	public NavigationEntry? Current => _current;

	public bool CanGoBack => _back.Count > 0;

	public bool CanGoForward => _forward.Count > 0;

	public event Action? StateChanged;

	public void RecordNavigation(NavigationEntry newState)
	{
		if ((object)_current != null)
		{
			_back.Push(_current);
		}
		_current = newState;
		_forward.Clear();
		TrimToCap(_back);
		this.StateChanged?.Invoke();
	}

	private static void TrimToCap(Stack<NavigationEntry> stack)
	{
		if (stack.Count > 50)
		{
			NavigationEntry[] array = stack.Take(50).Reverse().ToArray();
			stack.Clear();
			NavigationEntry[] array2 = array;
			foreach (NavigationEntry item in array2)
			{
				stack.Push(item);
			}
		}
	}

	public void UpdateCurrentPage(int page)
	{
		if ((object)_current != null && _current.Page != page)
		{
			_current = _current with
			{
				Page = page
			};
		}
	}

	public NavigationEntry? GoBack()
	{
		if (_back.Count == 0)
		{
			return null;
		}
		if ((object)_current != null)
		{
			_forward.Push(_current);
		}
		_current = _back.Pop();
		TrimToCap(_forward);
		this.StateChanged?.Invoke();
		return _current;
	}

	public NavigationEntry? GoForward()
	{
		if (_forward.Count == 0)
		{
			return null;
		}
		if ((object)_current != null)
		{
			_back.Push(_current);
		}
		_current = _forward.Pop();
		TrimToCap(_back);
		this.StateChanged?.Invoke();
		return _current;
	}

	public NavigationHistorySnapshot Snapshot()
	{
		return new NavigationHistorySnapshot(_current, _back.ToArray(), _forward.ToArray());
	}

	public void Clear()
	{
		_back.Clear();
		_forward.Clear();
		_current = null;
		this.StateChanged?.Invoke();
	}

	public void Restore(NavigationHistorySnapshot? snapshot)
	{
		_back.Clear();
		_forward.Clear();
		_current = null;
		if ((object)snapshot == null)
		{
			this.StateChanged?.Invoke();
			return;
		}
		NavigationEntry[] back = snapshot.Back;
		if (back != null && back.Length > 0)
		{
			for (int num = Math.Min(back.Length, 50) - 1; num >= 0; num--)
			{
				_back.Push(back[num]);
			}
		}
		NavigationEntry[] forward = snapshot.Forward;
		if (forward != null && forward.Length > 0)
		{
			for (int num2 = Math.Min(forward.Length, 50) - 1; num2 >= 0; num2--)
			{
				_forward.Push(forward[num2]);
			}
		}
		_current = snapshot.Current;
		this.StateChanged?.Invoke();
	}
}
