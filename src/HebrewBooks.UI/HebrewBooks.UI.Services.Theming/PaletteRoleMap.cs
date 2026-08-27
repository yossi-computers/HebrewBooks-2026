using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace HebrewBooks.UI.Services.Theming;

internal static class PaletteRoleMap
{
	private static readonly (Func<Palette, Color?> Role, string[] BrushKeys)[] Map = new(Func<Palette, Color?>, string[])[11]
	{
		((Palette p) => p.Background, new string[3] { "ApplicationBackgroundBrush", "SolidBackgroundFillColorBaseBrush", "SolidBackgroundFillColorBaseAltBrush" }),
		((Palette p) => p.Surface, new string[4] { "SolidBackgroundFillColorSecondaryBrush", "LayerFillColorDefaultBrush", "LayerOnAcrylicFillColorDefaultBrush", "LayerOnMicaBaseAltFillColorDefaultBrush" }),
		((Palette p) => p.SurfaceAlt, new string[5] { "SolidBackgroundFillColorTertiaryBrush", "SolidBackgroundFillColorQuarternaryBrush", "LayerFillColorAltBrush", "LayerOnMicaBaseAltFillColorSecondaryBrush", "LayerOnMicaBaseAltFillColorTertiaryBrush" }),
		((Palette p) => p.Card, new string[2] { "CardBackgroundFillColorDefaultBrush", "CardBackgroundFillColorSecondaryBrush" }),
		((Palette p) => p.ControlFill, new string[8] { "ControlFillColorDefaultBrush", "ControlFillColorSecondaryBrush", "ControlFillColorTertiaryBrush", "ControlFillColorInputActiveBrush", "ControlAltFillColorSecondaryBrush", "ControlAltFillColorTertiaryBrush", "ControlAltFillColorQuarternaryBrush", "ControlSolidFillColorDefaultBrush" }),
		((Palette p) => p.SubtleHover, new string[3] { "SubtleFillColorSecondaryBrush", "SubtleFillColorTertiaryBrush", "ControlStrongFillColorDefaultBrush" }),
		((Palette p) => p.Border, new string[14]
		{
			"ControlStrokeColorDefaultBrush", "ControlStrokeColorSecondaryBrush", "ControlStrokeColorTertiaryBrush", "ControlStrongStrokeColorDefaultBrush", "CardStrokeColorDefaultBrush", "CardStrokeColorDefaultSolidBrush", "CardBorderBrush", "SurfaceStrokeColorDefaultBrush", "SurfaceStrokeColorFlyoutBrush", "ControlElevationBorderBrush",
			"TextControlElevationBorderBrush", "CircleElevationBorderBrush", "NavigationViewContentGridBorderBrush", "NavigationViewItemBorderBrush"
		}),
		((Palette p) => p.Divider, new string[2] { "DividerStrokeColorDefaultBrush", "SeparatorBorderBrush" }),
		((Palette p) => p.TextPrimary, new string[1] { "TextFillColorPrimaryBrush" }),
		((Palette p) => p.TextSecondary, new string[1] { "TextFillColorSecondaryBrush" }),
		((Palette p) => p.TextTertiary, new string[2] { "TextFillColorTertiaryBrush", "TextPlaceholderColorBrush" })
	};

	public static IEnumerable<(string BrushKey, Color Color)> Expand(Palette palette)
	{
		(Func<Palette, Color?> Role, string[] BrushKeys)[] map = Map;
		for (int i = 0; i < map.Length; i++)
		{
			(Func<Palette, Color?> Role, string[] BrushKeys) tuple = map[i];
			Func<Palette, Color?> item = tuple.Role;
			string[] item2 = tuple.BrushKeys;
			Color? color = item(palette);
			if (color.HasValue)
			{
				Color c = color.GetValueOrDefault();
				string[] array = item2;
				foreach (string item3 in array)
				{
					yield return (BrushKey: item3, Color: c);
				}
			}
		}
	}
}
