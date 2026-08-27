using System;
using System.Collections.Generic;
using HebrewBooks.Core.Abstractions;

namespace HebrewBooks.Core;

public sealed class SessionContext : ISessionContext
{
	private bool _isKiosk;

	private bool _isKioskMouse;

	private bool _lockPdfReader;

	private bool _rightToLeft = true;

	private bool _trimSql = true;

	private int _sqlOption = 1;

	private bool _runSearch = true;

	private bool _masterSearch = true;

	private int _maxProximity = 30;

	private bool _gershaym = true;

	private bool _hybur;

	private bool _resizeFont = true;

	private bool _includeNumbers = true;

	private bool _sortBySeder;

	private int _maxFilesToRetrieve = 10000;

	private bool _quickSave = true;

	private bool _openRichTextOnSearch = true;

	private string? _currentWorkAreaName;

	public bool IsKiosk
	{
		get
		{
			return _isKiosk;
		}
		set
		{
			Set(ref _isKiosk, value);
		}
	}

	public bool IsKioskMouse
	{
		get
		{
			return _isKioskMouse;
		}
		set
		{
			Set(ref _isKioskMouse, value);
		}
	}

	public bool LockPdfReader
	{
		get
		{
			return _lockPdfReader;
		}
		set
		{
			Set(ref _lockPdfReader, value);
		}
	}

	public bool RightToLeft
	{
		get
		{
			return _rightToLeft;
		}
		set
		{
			Set(ref _rightToLeft, value);
		}
	}

	public bool TrimSql
	{
		get
		{
			return _trimSql;
		}
		set
		{
			Set(ref _trimSql, value);
		}
	}

	public int SqlOption
	{
		get
		{
			return _sqlOption;
		}
		set
		{
			Set(ref _sqlOption, value);
		}
	}

	public bool RunSearch
	{
		get
		{
			return _runSearch;
		}
		set
		{
			Set(ref _runSearch, value);
		}
	}

	public bool MasterSearch
	{
		get
		{
			return _masterSearch;
		}
		set
		{
			Set(ref _masterSearch, value);
		}
	}

	public int MaxProximity
	{
		get
		{
			return _maxProximity;
		}
		set
		{
			Set(ref _maxProximity, value);
		}
	}

	public bool Gershaym
	{
		get
		{
			return _gershaym;
		}
		set
		{
			Set(ref _gershaym, value);
		}
	}

	public bool Hybur
	{
		get
		{
			return _hybur;
		}
		set
		{
			Set(ref _hybur, value);
		}
	}

	public bool ResizeFont
	{
		get
		{
			return _resizeFont;
		}
		set
		{
			Set(ref _resizeFont, value);
		}
	}

	public bool IncludeNumbers
	{
		get
		{
			return _includeNumbers;
		}
		set
		{
			Set(ref _includeNumbers, value);
		}
	}

	public bool SortBySeder
	{
		get
		{
			return _sortBySeder;
		}
		set
		{
			Set(ref _sortBySeder, value);
		}
	}

	public int MaxFilesToRetrieve
	{
		get
		{
			return _maxFilesToRetrieve;
		}
		set
		{
			Set(ref _maxFilesToRetrieve, value);
		}
	}

	public bool QuickSave
	{
		get
		{
			return _quickSave;
		}
		set
		{
			Set(ref _quickSave, value);
		}
	}

	public bool OpenRichTextOnSearch
	{
		get
		{
			return _openRichTextOnSearch;
		}
		set
		{
			Set(ref _openRichTextOnSearch, value);
		}
	}

	public string? CurrentWorkAreaName
	{
		get
		{
			return _currentWorkAreaName;
		}
		set
		{
			Set(ref _currentWorkAreaName, value);
		}
	}

	public event EventHandler? Changed;

	private void Set<T>(ref T field, T value)
	{
		if (!EqualityComparer<T>.Default.Equals(field, value))
		{
			field = value;
			this.Changed?.Invoke(this, EventArgs.Empty);
		}
	}
}
