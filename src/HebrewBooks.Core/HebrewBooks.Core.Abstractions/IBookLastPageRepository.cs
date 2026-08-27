namespace HebrewBooks.Core.Abstractions;

public interface IBookLastPageRepository
{
	int? GetLastPage(string fileId);

	void Save(string fileId, int lastPage);

	void Clear();
}
