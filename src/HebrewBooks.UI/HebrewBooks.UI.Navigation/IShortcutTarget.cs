namespace HebrewBooks.UI.Navigation;

public interface IShortcutTarget
{
	void HandleShortcut(ShortcutAction action);
}
