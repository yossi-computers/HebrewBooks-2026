using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.Core.Models;
using HebrewBooks.Infrastructure.Settings;
using HebrewBooks.Services.Catalog;
using HebrewBooks.Services.Downloader;
using HebrewBooks.UI.Dialogs;
using HebrewBooks.UI.Messages;
using HebrewBooks.UI.Resources;

namespace HebrewBooks.UI.Services;

public sealed class OnDemandBookService
{
	private readonly IPathResolver _paths;

	private readonly BookDownloadService _downloader;

	private readonly BookDeletionService _deletion;

	private readonly JsonSettingsStore _settings;

	public OnDemandBookService(IPathResolver paths, BookDownloadService downloader, BookDeletionService deletion, JsonSettingsStore settings)
	{
		_paths = paths;
		_downloader = downloader;
		_deletion = deletion;
		_settings = settings;
	}

	public async Task<bool> EnsureLocalAsync(Book book, Window? owner = null)
	{
		if (!int.TryParse(book.FileID, out var fileId))
		{
			return true;
		}
		string path = _paths.PdfPath(fileId, book.Folder);
		if (File.Exists(path))
		{
			return true;
		}
		string text = (string.IsNullOrWhiteSpace(book.BookName) ? $"{SharedStrings.S2013}{fileId}" : book.BookName);
		BookshelfOptions bookshelfOptions = _settings.Load();
		MissingBookAction missingBookAction = bookshelfOptions.View.MissingBookAction;
		if (bookshelfOptions.UseOnlineService && missingBookAction == MissingBookAction.Ask)
		{
			missingBookAction = MissingBookAction.AlwaysDownload;
		}
		if (missingBookAction == MissingBookAction.NeverDownload)
		{
			return false;
		}
		bool flag = false;
		bool flag2;
		if (missingBookAction == MissingBookAction.AlwaysDownload)
		{
			flag2 = true;
		}
		else
		{
			DownloadPromptResult prompt = ConfirmDownloadDialog.Show(owner, SharedStrings.S2014 + text + SharedStrings.S2015, SharedStrings.S589);
			flag2 = prompt.Download;
			if (prompt.Remember)
			{
				_settings.Update(delegate(BookshelfOptions o)
				{
					o.View.MissingBookAction = (prompt.Download ? MissingBookAction.AlwaysDownload : MissingBookAction.NeverDownload);
				});
				flag = true;
			}
		}
		if (flag2)
		{
			if (_downloader.DownloadsBlockedByProtectMode)
			{
				Info(owner, SharedStrings.S2016 + text + SharedStrings.S2017 + SharedStrings.S591, SharedStrings.S592);
				return false;
			}
			try
			{
				await _downloader.MirrorPrefetchAsync(new int[1] { fileId });
			}
			catch
			{
			}
			if (File.Exists(path))
			{
				return true;
			}
			try
			{
				await _downloader.DownloadBookAsync(fileId);
			}
			catch
			{
			}
			if (File.Exists(path))
			{
				return true;
			}
			Info(owner, SharedStrings.S593, SharedStrings.S594);
			return false;
		}
		if (flag)
		{
			return false;
		}
		if (Ask(owner, SharedStrings.S2018 + text + SharedStrings.S2019, SharedStrings.S596) == MessageBoxResult.Yes)
		{
			try
			{
				await _deletion.DeleteAsync(new Book[1] { book });
				WeakReferenceMessenger.Default.Send(new CatalogChangedMessage(1));
			}
			catch (Exception ex)
			{
				Info(owner, SharedStrings.S9065 + ex.Message, SharedStrings.S598);
			}
		}
		return false;
	}

	private static MessageBoxResult Ask(Window? owner, string text, string caption)
	{
		if (owner != null)
		{
			return HebrewMessageBox.Show(owner, text, caption, MessageBoxButton.YesNo, MessageBoxImage.Question);
		}
		return HebrewMessageBox.Show(text, caption, MessageBoxButton.YesNo, MessageBoxImage.Question);
	}

	private static void Info(Window? owner, string text, string caption)
	{
		if (owner == null)
		{
			HebrewMessageBox.Show(text, caption, MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
		else
		{
			HebrewMessageBox.Show(owner, text, caption, MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}
}
