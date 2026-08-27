using System.Windows.Input;

namespace HebrewBooks.UI.Navigation;

public static class ShortcutKeyMap
{
	public static ShortcutAction? FromKey(KeyEventArgs e, bool focusInTextBox)
	{
		Key key = ((e.Key == Key.System) ? e.SystemKey : e.Key);
		bool flag = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
		bool flag2 = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
		bool flag3 = (Keyboard.Modifiers & (ModifierKeys.Alt | ModifierKeys.Control | ModifierKeys.Windows)) == 0;
		bool flag4 = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
		if (flag && flag4 && key == Key.Down)
		{
			return ShortcutAction.NextBook;
		}
		if (flag && flag4 && key == Key.Up)
		{
			return ShortcutAction.PrevBook;
		}
		if ((flag && key == Key.Down) || (flag2 && key == Key.Right))
		{
			return ShortcutAction.NextResult;
		}
		if ((flag && key == Key.Up) || (flag2 && key == Key.Left))
		{
			return ShortcutAction.PrevResult;
		}
		if (flag && flag4 && key == Key.Tab)
		{
			return ShortcutAction.PrevTab;
		}
		if (flag && !flag4 && key == Key.Tab)
		{
			return ShortcutAction.NextTab;
		}
		if (flag && key == Key.Next)
		{
			return ShortcutAction.NextTab;
		}
		if (flag && key == Key.Prior)
		{
			return ShortcutAction.PrevTab;
		}
		if (flag && !flag4 && !flag2 && key == Key.W)
		{
			return ShortcutAction.CloseActiveTab;
		}
		if (flag2 && key == Key.C)
		{
			return ShortcutAction.GoToContentSearch;
		}
		if (flag2 && key == Key.K)
		{
			return ShortcutAction.GoToCatalog;
		}
		if (flag && key == Key.F)
		{
			return ShortcutAction.FocusInBookSearch;
		}
		if (flag && key == Key.E)
		{
			return ShortcutAction.FocusMainSearch;
		}
		if (flag3 && !focusInTextBox && key == Key.F)
		{
			return ShortcutAction.FocusInBookSearch;
		}
		if (flag3 && !focusInTextBox && (key == Key.Oem2 || key == Key.Oem2))
		{
			return ShortcutAction.FocusMainSearch;
		}
		return null;
	}

	public static ShortcutAction? FromViewerToken(string token)
	{
		return token switch
		{
			"next" => ShortcutAction.NextResult, 
			"prev" => ShortcutAction.PrevResult, 
			"next-book" => ShortcutAction.NextBook, 
			"prev-book" => ShortcutAction.PrevBook, 
			"focus-inbook" => ShortcutAction.FocusInBookSearch, 
			"focus-main" => ShortcutAction.FocusMainSearch, 
			"goto-search" => ShortcutAction.GoToContentSearch, 
			"goto-catalog" => ShortcutAction.GoToCatalog, 
			"nav-back" => ShortcutAction.NavBack, 
			"nav-forward" => ShortcutAction.NavForward, 
			"tab-next" => ShortcutAction.NextTab, 
			"tab-prev" => ShortcutAction.PrevTab, 
			"tab-close" => ShortcutAction.CloseActiveTab, 
			_ => null, 
		};
	}
}
