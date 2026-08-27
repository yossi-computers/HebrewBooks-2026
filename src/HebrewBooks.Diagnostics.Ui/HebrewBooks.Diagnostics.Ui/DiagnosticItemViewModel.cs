using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using CommunityToolkit.Mvvm.Input;

namespace HebrewBooks.Diagnostics.Ui;

public partial class DiagnosticItemViewModel : ObservableObject
{
	private readonly DiagnosticsViewModel _parent;

	[ObservableProperty]
	private bool _isBusy;

	[ObservableProperty]
	private string? _fixResultMessage;

	[ObservableProperty]
	private bool _fixSucceeded;


	public DiagnosticResult Source { get; }

	public string Title => Source.Title;

	public string Category => Source.Category;

	public string Detail => Source.Detail;

	public string? Evidence => Source.Evidence;

	public bool HasEvidence => !string.IsNullOrWhiteSpace(Source.Evidence);

	public bool HasFix => Source.Fix != null;

	public string FixLabel => Source.Fix?.Label ?? "";

	public string SeverityLabel => Source.Severity switch
	{
		DiagnosticSeverity.Ok => "תקין", 
		DiagnosticSeverity.Info => "מידע", 
		DiagnosticSeverity.Warning => "אזהרה", 
		DiagnosticSeverity.Error => "תקלה", 
		_ => "", 
	};

	public Brush AccentBrush => Source.Severity switch
	{
		DiagnosticSeverity.Ok => Frozen(46, 125, 50), 
		DiagnosticSeverity.Info => Frozen(85, 110, 122), 
		DiagnosticSeverity.Warning => Frozen(245, 158, 11), 
		DiagnosticSeverity.Error => Frozen(211, 47, 47), 
		_ => Frozen(128, 128, 128), 
	};

	public bool HasFixResult => !string.IsNullOrWhiteSpace(FixResultMessage);

	public Brush FixResultBrush
	{
		get
		{
			if (!FixSucceeded)
			{
				return Frozen(211, 47, 47);
			}
			return Frozen(46, 125, 50);
		}
	}





	public DiagnosticItemViewModel(DiagnosticResult source, DiagnosticsViewModel parent)
	{
		Source = source;
		_parent = parent;
	}

	[RelayCommand]
	private Task ApplyFix()
	{
		return _parent.ApplyFixAsync(this);
	}

	internal void SetFixResult(FixOutcome outcome)
	{
		FixSucceeded = outcome.Success;
		FixResultMessage = outcome.Message;
		OnPropertyChanged("HasFixResult");
		OnPropertyChanged("FixResultBrush");
	}

	private static SolidColorBrush Frozen(byte r, byte g, byte b)
	{
		SolidColorBrush solidColorBrush = new SolidColorBrush(Color.FromRgb(r, g, b));
		solidColorBrush.Freeze();
		return solidColorBrush;
	}
}
