using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace HebrewBooks.UI.Services.Theming;

public sealed record Palette
{
	public required string Id { get; init; }

	public required string DisplayName { get; init; }

	public required ApplicationTheme BaseMode { get; init; }

	public Color? Accent { get; init; }

	public Color? AccentForeground { get; init; }

	public Color? Selection { get; init; }

	public Color? SelectionForeground { get; init; }

	public Color? Background { get; init; }

	public Color? Surface { get; init; }

	public Color? SurfaceAlt { get; init; }

	public Color? Card { get; init; }

	public Color? ControlFill { get; init; }

	public Color? SubtleHover { get; init; }

	public Color? Border { get; init; }

	public Color? Divider { get; init; }

	public Color? TextPrimary { get; init; }

	public Color? TextSecondary { get; init; }

	public Color? TextTertiary { get; init; }

	public bool HasOverrides
	{
		get
		{
			if (!Accent.HasValue && !Background.HasValue && !Surface.HasValue && !SurfaceAlt.HasValue && !Card.HasValue && !ControlFill.HasValue && !SubtleHover.HasValue && !Border.HasValue && !Divider.HasValue && !TextPrimary.HasValue && !TextSecondary.HasValue)
			{
				return TextTertiary.HasValue;
			}
			return true;
		}
	}

	[CompilerGenerated]
	[SetsRequiredMembers]
	private Palette(Palette original)
	{
		Id = original.Id;
		DisplayName = original.DisplayName;
		BaseMode = original.BaseMode;
		Accent = original.Accent;
		AccentForeground = original.AccentForeground;
		Selection = original.Selection;
		SelectionForeground = original.SelectionForeground;
		Background = original.Background;
		Surface = original.Surface;
		SurfaceAlt = original.SurfaceAlt;
		Card = original.Card;
		ControlFill = original.ControlFill;
		SubtleHover = original.SubtleHover;
		Border = original.Border;
		Divider = original.Divider;
		TextPrimary = original.TextPrimary;
		TextSecondary = original.TextSecondary;
		TextTertiary = original.TextTertiary;
	}

	public Palette()
	{
	}
}
