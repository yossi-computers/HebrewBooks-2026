using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace HebrewBooks.Diagnostics.Ui;

public partial class DiagnosticsViewModel : ObservableObject
{
	private readonly Func<Action<DiagnosticResult>, CancellationToken, Task<DiagnosticReport>> _runReport;

	private readonly Func<DiagnosticFix, CancellationToken, Task<FixOutcome>>? _appOnlyFixHandler;

	[ObservableProperty]
	private bool _isRunning;

	[ObservableProperty]
	private bool _hasRun;

	[ObservableProperty]
	private string _statusText = "לחץ \"הרץ בדיקה\" כדי לסרוק את המערכת.";

	[ObservableProperty]
	private int _okCount;

	[ObservableProperty]
	private int _infoCount;

	[ObservableProperty]
	private int _warnCount;

	[ObservableProperty]
	private int _errorCount;

	private DiagnosticReport? _report;




	public ObservableCollection<DiagnosticItemViewModel> Items { get; } = new ObservableCollection<DiagnosticItemViewModel>();











	public DiagnosticsViewModel(Func<Action<DiagnosticResult>, CancellationToken, Task<DiagnosticReport>> runReport, Func<DiagnosticFix, CancellationToken, Task<FixOutcome>>? appOnlyFixHandler = null)
	{
		_runReport = runReport;
		_appOnlyFixHandler = appOnlyFixHandler;
	}

	[RelayCommand]
	public async Task RunAsync()
	{
		if (IsRunning)
		{
			return;
		}
		IsRunning = true;
		HasRun = true;
		StatusText = "מריץ בדיקות תקינות...";
		Items.Clear();
		DiagnosticsViewModel diagnosticsViewModel = this;
		DiagnosticsViewModel diagnosticsViewModel2 = this;
		DiagnosticsViewModel diagnosticsViewModel3 = this;
		int num = (ErrorCount = 0);
		int num3 = (diagnosticsViewModel3.WarnCount = num);
		int okCount = (diagnosticsViewModel2.InfoCount = num3);
		diagnosticsViewModel.OkCount = okCount;
		Dispatcher dispatcher = Application.Current?.Dispatcher;
		try
		{
			_report = await Task.Run(() => _runReport(OnResult, CancellationToken.None));
			StatusText = ((ErrorCount > 0) ? "נמצאו תקלות שכדאי לטפל בהן." : ((WarnCount > 0) ? "נמצאו אזהרות — מומלץ לעיין." : "הכול תקין! \ud83c\udf89"));
		}
		catch (Exception ex)
		{
			StatusText = "שגיאה בהרצת הבדיקות: " + ex.Message;
		}
		finally
		{
			IsRunning = false;
		}
		void OnResult(DiagnosticResult r)
		{
			if (dispatcher != null && !dispatcher.CheckAccess())
			{
				dispatcher.Invoke(delegate
				{
					AddResult(r);
				});
			}
			else
			{
				AddResult(r);
			}
		}
	}

	private void AddResult(DiagnosticResult r)
	{
		Items.Add(new DiagnosticItemViewModel(r, this));
		switch (r.Severity)
		{
		case DiagnosticSeverity.Ok:
			OkCount++;
			break;
		case DiagnosticSeverity.Info:
			InfoCount++;
			break;
		case DiagnosticSeverity.Warning:
			WarnCount++;
			break;
		case DiagnosticSeverity.Error:
			ErrorCount++;
			break;
		}
	}

	[RelayCommand]
	private void CopyReport()
	{
		if (_report == null)
		{
			return;
		}
		try
		{
			Clipboard.SetText(_report.ToText());
			StatusText = "הדוח הועתק ללוח. אפשר להדביק אותו בהודעה לתמיכה.";
		}
		catch (Exception ex)
		{
			StatusText = "לא ניתן להעתיק: " + ex.Message;
		}
	}

	[RelayCommand]
	private void SaveReport()
	{
		if (_report == null)
		{
			return;
		}
		SaveFileDialog saveFileDialog = new SaveFileDialog
		{
			Title = "שמירת דוח בדיקת תקינות",
			Filter = "קובץ טקסט|*.txt",
			DefaultExt = ".txt",
			FileName = "HebrewBooks-בדיקת-תקינות.txt"
		};
		if (saveFileDialog.ShowDialog() != true)
		{
			return;
		}
		try
		{
			File.WriteAllText(saveFileDialog.FileName, _report.ToText(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
			StatusText = "הדוח נשמר: " + saveFileDialog.FileName;
		}
		catch (Exception ex)
		{
			StatusText = "השמירה נכשלה: " + ex.Message;
		}
	}

	internal async Task ApplyFixAsync(DiagnosticItemViewModel item)
	{
		DiagnosticFix fix = item.Source.Fix;
		if (fix == null || item.IsBusy || (fix.Kind == FixKind.Confirm && MessageBox.Show("לבצע: " + fix.Label + "?", "אישור פעולה", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes))
		{
			return;
		}
		item.IsBusy = true;
		try
		{
			FixOutcome fixOutcome;
			if (fix.Kind != FixKind.AppOnly)
			{
				fixOutcome = ((fix.Run == null) ? FixOutcome.Fail("אין פעולת תיקון זמינה.") : (await fix.Run(CancellationToken.None).ConfigureAwait(continueOnCapturedContext: true)));
			}
			else
			{
				FixOutcome fixOutcome2 = ((_appOnlyFixHandler == null) ? FixOutcome.Fail("פעולה זו זמינה רק מתוך התוכנה הראשית.") : (await _appOnlyFixHandler(fix, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: true)));
				fixOutcome = fixOutcome2;
			}
			item.SetFixResult(fixOutcome);
			if (fixOutcome.Success && fix.Kind != FixKind.AppOnly)
			{
				await RunAsync().ConfigureAwait(continueOnCapturedContext: true);
			}
		}
		catch (Exception ex)
		{
			item.SetFixResult(FixOutcome.Fail(ex.Message));
		}
		finally
		{
			item.IsBusy = false;
		}
	}
}
