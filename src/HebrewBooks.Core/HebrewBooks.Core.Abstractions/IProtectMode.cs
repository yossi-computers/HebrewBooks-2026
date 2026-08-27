namespace HebrewBooks.Core.Abstractions;

public interface IProtectMode
{
	bool IsActive { get; }

	bool AllowsBookFetch { get; }
}
