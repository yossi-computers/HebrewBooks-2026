using System;

namespace HebrewBooks.Core.Abstractions;

public interface ISessionContext
{
	bool IsKiosk { get; set; }

	bool IsKioskMouse { get; set; }

	bool LockPdfReader { get; set; }

	bool RightToLeft { get; set; }

	bool TrimSql { get; set; }

	int SqlOption { get; set; }

	bool RunSearch { get; set; }

	bool MasterSearch { get; set; }

	int MaxProximity { get; set; }

	bool Gershaym { get; set; }

	bool Hybur { get; set; }

	bool ResizeFont { get; set; }

	bool IncludeNumbers { get; set; }

	bool SortBySeder { get; set; }

	int MaxFilesToRetrieve { get; set; }

	bool QuickSave { get; set; }

	bool OpenRichTextOnSearch { get; set; }

	string? CurrentWorkAreaName { get; set; }

	event EventHandler? Changed;
}
