using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using CommunityToolkit.Mvvm.Input;
using HebrewBooks.Core.Models;
using HebrewBooks.Services.Catalog;
using HebrewBooks.UI.Resources;

namespace HebrewBooks.UI.ViewModels;

public sealed partial class DeleteBookViewModel : ObservableObject
{
	private readonly BookDeletionService _deletion;

	private int _challengeA;

	private int _challengeB;

	[ObservableProperty]
	private string _challengeText = "";

	[ObservableProperty]
	[NotifyCanExecuteChangedFor("ConfirmCommand")]
	private string _answerText = "";

	[ObservableProperty]
	private string _statusText = "";

	[ObservableProperty]
	[NotifyCanExecuteChangedFor("ConfirmCommand")]
	private bool _isDeleting;

	[ObservableProperty]
	private bool _isComplete;


	public IReadOnlyList<Book> Books { get; set; } = Array.Empty<Book>();

	public string BookNamesDisplay
	{
		get
		{
			if (Books.Count != 0)
			{
				return string.Join("\n", Books.Select((Book b) => "• " + (b.BookName ?? b.FileID ?? "?")));
			}
			return "";
		}
	}

	public int BookCount => Books.Count;

	public bool IsMultiple => Books.Count > 1;

	public bool AnyDeleted { get; private set; }







	public DeleteBookViewModel(BookDeletionService deletion)
	{
		_deletion = deletion;
		GenerateChallenge();
	}

	private void GenerateChallenge()
	{
		Random shared = Random.Shared;
		_challengeA = shared.Next(10, 50);
		_challengeB = shared.Next(10, 50);
		ChallengeText = $"{SharedStrings.S2023}{_challengeA} + {_challengeB} = ?";
		AnswerText = "";
	}

	private bool CanConfirm()
	{
		if (!IsDeleting && !IsComplete && int.TryParse(AnswerText, out var result))
		{
			return result == _challengeA + _challengeB;
		}
		return false;
	}

	[RelayCommand(CanExecute = "CanConfirm")]
	private async Task ConfirmAsync()
	{
		if (Books.Count == 0)
		{
			return;
		}
		try
		{
			IsDeleting = true;
			StatusText = SharedStrings.S617;
			IReadOnlyList<DeletionResult> obj = await _deletion.DeleteAsync(Books);
			int num = 0;
			int num2 = 0;
			List<string> list = new List<string>();
			foreach (DeletionResult item in obj)
			{
				if (item.Outcome == DeletionOutcome.Ok)
				{
					num++;
					continue;
				}
				num2++;
				list.Add(item.Book.BookName + ": " + item.Reason);
			}
			AnyDeleted = num > 0;
			StatusText = ((num2 == 0) ? $"{SharedStrings.S2024}{num}{SharedStrings.S2025}" : ($"{SharedStrings.S2026}{num}{SharedStrings.S2027}{num2}:\n" + string.Join("\n", list)));
			IsComplete = true;
		}
		catch (Exception ex)
		{
			StatusText = SharedStrings.S2028 + ex.Message;
		}
		finally
		{
			IsDeleting = false;
		}
	}
}
