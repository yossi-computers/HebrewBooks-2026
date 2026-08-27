using System.Collections.Generic;

namespace HebrewBooks.Core.Abstractions;

public interface IFavoritesRepository
{
	IReadOnlyList<FavoriteEntry> GetAll();

	bool IsFavorited(string fileId);

	void Add(string fileId, string folderName = "");

	void Remove(string fileId, string folderName = "");

	void RemoveAll(string fileId);

	IReadOnlyList<string> GetFolders();

	void CreateFolder(string name);

	void DeleteFolder(string name);

	void MoveBookToFolder(string fileId, string newFolderName);

	void Clear();
}
