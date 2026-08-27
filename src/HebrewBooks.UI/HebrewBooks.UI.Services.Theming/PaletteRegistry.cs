using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using HebrewBooks.UI.Resources;
using Wpf.Ui.Appearance;

namespace HebrewBooks.UI.Services.Theming;

public static class PaletteRegistry
{
	public static readonly Palette ClassicLight = new Palette
	{
		Id = "Light",
		DisplayName = SharedStrings.S605,
		BaseMode = ApplicationTheme.Light
	};

	public static readonly Palette ClassicDark = new Palette
	{
		Id = "Dark",
		DisplayName = SharedStrings.S606,
		BaseMode = ApplicationTheme.Dark
	};

	public static readonly Palette HighContrast = new Palette
	{
		Id = "HighContrast",
		DisplayName = SharedStrings.S607,
		BaseMode = ApplicationTheme.HighContrast
	};

	public static readonly Palette IndigoLight = Make("IndigoLight", SharedStrings.S608, ApplicationTheme.Light, "#fbfcfd", "#f7f9fa", "#f1f3f5", "#e9ecef", "#e3e6e9", "#c1c8cd", "#11181c", "#69727a", "#3e63dd", "#ffffff", "#eef1fd", "#2f4bb8");

	public static readonly Palette IndigoDark = Make("IndigoDark", SharedStrings.S609, ApplicationTheme.Dark, "#141618", "#191c1e", "#1e2225", "#26292c", "#2b2f33", "#464b50", "#ecedee", "#9298a0", "#5472e4", "#ffffff", "#1b2647", "#c3d0ff");

	public static readonly Palette AmberLight = Make("Sepia", SharedStrings.S610, ApplicationTheme.Light, "#fdfcf9", "#faf8f2", "#f4f1e8", "#ece7db", "#e7e1d3", "#cabfa6", "#241d12", "#7a6f5c", "#b0700f", "#ffffff", "#f7edd7", "#875312");

	public static readonly Palette AmberDark = Make("AmberDark", SharedStrings.S611, ApplicationTheme.Dark, "#181712", "#1e1c16", "#24211a", "#2b2820", "#322e25", "#4d493c", "#efece2", "#a39a86", "#e0a23a", "#241800", "#3a2e12", "#f0c778");

	public static readonly Palette IrisLight = Make("IrisLight", SharedStrings.S612, ApplicationTheme.Light, "#fdfcfe", "#f8f7fa", "#f1eff5", "#eae7f1", "#e4e0ec", "#c9c4d6", "#1b1626", "#6f6980", "#5b5bd6", "#ffffff", "#eeecfb", "#4a45bd");

	public static readonly Palette IrisDark = Make("Iris", SharedStrings.S613, ApplicationTheme.Dark, "#161618", "#1b1b1e", "#212024", "#28282c", "#2d2d32", "#4c4b53", "#ededef", "#9d9ca6", "#6e6ade", "#ffffff", "#232349", "#cfcbff");

	public static readonly Palette JadeLight = Make("JadeLight", SharedStrings.S614, ApplicationTheme.Light, "#fbfdfc", "#f6f9f8", "#eef2f0", "#e6ebe9", "#dee4e1", "#c0c8c4", "#132019", "#63706a", "#1a7d63", "#ffffff", "#ddf1ea", "#146152");

	public static readonly Palette JadeDark = Make("JadeDark", SharedStrings.S615, ApplicationTheme.Dark, "#141715", "#191d1b", "#1e2320", "#252b27", "#2b322e", "#45504b", "#eceeed", "#93a09a", "#30a684", "#08211a", "#123a2e", "#84d8bf");

	public static readonly IReadOnlyList<Palette> All = new Palette[11]
	{
		ClassicLight, ClassicDark, IndigoLight, IndigoDark, AmberLight, AmberDark, IrisLight, IrisDark, JadeLight, JadeDark,
		HighContrast
	};

	private static Color C(string hex)
	{
		return (Color)ColorConverter.ConvertFromString(hex);
	}

	private static Palette Make(string id, string name, ApplicationTheme mode, string bg, string panel, string raised, string hover, string line, string lineStrong, string text, string muted, string accent, string onAccent, string sel, string selText)
	{
		return new Palette
		{
			Id = id,
			DisplayName = name,
			BaseMode = mode,
			Background = C(bg),
			Surface = C(panel),
			Card = C(panel),
			SurfaceAlt = C(raised),
			ControlFill = C(raised),
			SubtleHover = C(hover),
			Border = C(line),
			Divider = C(line),
			TextPrimary = C(text),
			TextSecondary = C(muted),
			TextTertiary = C(muted),
			Accent = C(accent),
			AccentForeground = C(onAccent),
			Selection = C(sel),
			SelectionForeground = C(selText)
		};
	}

	public static Palette? Find(string? id)
	{
		if (id != null)
		{
			return All.FirstOrDefault((Palette p) => string.Equals(p.Id, id, StringComparison.Ordinal));
		}
		return null;
	}
}
