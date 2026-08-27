using CommunityToolkit.Mvvm.Input;

namespace HebrewBooks.UI.ViewModels;

public interface IHitStripNavigator
{
	IRelayCommand<int> GoToHitPageCommand { get; }
}
