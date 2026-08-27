using System.Collections.Generic;
using System.Globalization;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace HebrewBooks.UI.Services.Theming;

internal static class ViewerThemeMap
{
	private static readonly Color White = Color.FromRgb(238, 238, 238);

	private static readonly Color Black = Color.FromRgb(32, 32, 32);

	public static Dictionary<string, string> Build(Palette p, Color accent)
	{
		if (!p.Background.HasValue)
		{
			return OriginalDefaults();
		}
		bool flag = p.BaseMode == ApplicationTheme.Dark;
		Color value = p.Background.Value;
		Color color = p.Surface ?? value;
		Color color2 = p.SurfaceAlt ?? color;
		Color c = p.Card ?? color;
		Color color3 = p.ControlFill ?? color2;
		Color c2 = p.SubtleHover ?? color3;
		Color c3 = p.Border ?? color2;
		Color color4 = p.TextPrimary ?? (flag ? White : Black);
		Color color5 = p.TextSecondary ?? color4;
		Color c4 = p.TextTertiary ?? color5;
		return new Dictionary<string, string>
		{
			["--tb-bg"] = Hex(color2),
			["--tb-fg"] = Hex(color4),
			["--tb-dim"] = Hex(c4),
			["--tb-hover"] = Hex(c2),
			["--tb-active"] = Hex(color3),
			["--tb-border"] = Hex(c3),
			["--side-bg"] = Hex(color),
			["--side-fg"] = Hex(color4),
			["--side-dim"] = Hex(color5),
			["--side-border"] = Hex(c3),
			["--side-hover"] = Hex(c2),
			["--side-current-bg"] = Rgba(accent, 0.2),
			["--side-current-fg"] = Hex(color4),
			["--accent"] = Hex(accent),
			["--viewer-bg"] = (flag ? Hex(value) : Hex(color2)),
			["--paper-bg"] = (flag ? "#f4f1ea" : Hex(c)),
			["--field-bg"] = Hex(color3),
			["--field-border"] = Hex(c3)
		};
	}

	private static Dictionary<string, string> OriginalDefaults()
	{
		return new Dictionary<string, string>
		{
			["--tb-bg"] = "#2b2b2b",
			["--tb-fg"] = "#e6e6e6",
			["--tb-dim"] = "#9a9a9a",
			["--tb-hover"] = "#3d3d3d",
			["--tb-active"] = "#555555",
			["--tb-border"] = "#1a1a1a",
			["--side-bg"] = "#f5f5f5",
			["--side-fg"] = "#222222",
			["--side-dim"] = "#666666",
			["--side-border"] = "#d0d0d0",
			["--side-hover"] = "#dcdcdc",
			["--side-current-bg"] = "#d6e6f5",
			["--side-current-fg"] = "#000000",
			["--accent"] = "#5b9bd5",
			["--viewer-bg"] = "#525659",
			["--paper-bg"] = "#fdfdfa",
			["--field-bg"] = "#1f1f1f",
			["--field-border"] = "#444"
		};
	}

	private static string Hex(Color c)
	{
		return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
	}

	private static string Rgba(Color c, double a)
	{
		return $"rgba({c.R},{c.G},{c.B},{a.ToString(CultureInfo.InvariantCulture)})";
	}
}
