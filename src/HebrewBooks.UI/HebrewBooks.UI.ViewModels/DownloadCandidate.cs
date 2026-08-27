using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;

namespace HebrewBooks.UI.ViewModels;

public partial class DownloadCandidate : ObservableObject
{
	[ObservableProperty]
	private bool _isSelected;

	[ObservableProperty]
	private bool _isVisible = true;

	public int FileId { get; }

	public string BookName { get; }

	public string? AuthorName { get; }

	public string Display
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(AuthorName))
			{
				return BookName + " · " + AuthorName;
			}
			return BookName;
		}
	}



	public DownloadCandidate(int fileId, string bookName, string? authorName)
	{
		FileId = fileId;
		BookName = bookName;
		AuthorName = authorName;
	}
}
