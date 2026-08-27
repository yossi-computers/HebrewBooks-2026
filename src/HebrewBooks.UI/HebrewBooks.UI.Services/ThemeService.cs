using System;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using HebrewBooks.Infrastructure.Settings;
using HebrewBooks.UI.Controls;
using HebrewBooks.UI.Services.Theming;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace HebrewBooks.UI.Services;

public sealed class ThemeService
{
	private readonly JsonSettingsStore _store;

	private readonly ResourceDictionary _overrides = new ResourceDictionary();

	public ThemeService(JsonSettingsStore store)
	{
		_store = store;
	}

	public void ApplyFromSettings()
	{
		BookshelfOptions bookshelfOptions = _store.Load();
		Apply(bookshelfOptions.View.Theme);
	}

	public void Apply(string? paletteId)
	{
		ApplyPalette(Resolve(paletteId));
	}

	public void ApplyAndPersist(string paletteId)
	{
		BookshelfOptions bookshelfOptions = _store.Load();
		bookshelfOptions.View.Theme = paletteId;
		_store.Save(bookshelfOptions);
		Apply(paletteId);
	}

	private void ApplyPalette(Palette palette)
	{
		WindowBackdropType backgroundEffect = ((!palette.HasOverrides) ? WindowBackdropType.Mica : WindowBackdropType.None);
		ApplicationThemeManager.Apply(palette.BaseMode, backgroundEffect, !palette.Accent.HasValue);
		Color? accent = palette.Accent;
		if (accent.HasValue)
		{
			Color valueOrDefault = accent.GetValueOrDefault();
			ApplicationAccentColorManager.Apply(valueOrDefault, palette.BaseMode);
		}
		Color accent2 = palette.Accent ?? ApplicationAccentColorManager.SystemAccent;
		ApplyOverrides(palette, accent2);
		PdfJsHost.BroadcastViewerTheme(JsonSerializer.Serialize(ViewerThemeMap.Build(palette, accent2)));
	}

	private void ApplyOverrides(Palette palette, Color accent)
	{
		Application current = Application.Current;
		if (current == null)
		{
			return;
		}
		Collection<ResourceDictionary> mergedDictionaries = current.Resources.MergedDictionaries;
		mergedDictionaries.Remove(_overrides);
		_overrides.Clear();
		foreach (var (key, c) in PaletteRoleMap.Expand(palette))
		{
			_overrides[key] = Frozen(c);
		}
		Color color = palette.AccentForeground ?? (IsLight(accent) ? C("#FF1A1A1A") : C("#FFFFFFFF"));
		_overrides["TextOnAccentFillColorPrimaryBrush"] = Frozen(color);
		_overrides["TextOnAccentFillColorSecondaryBrush"] = Frozen(color);
		_overrides["AccentTextFillColorPrimaryBrush"] = Frozen(accent);
		_overrides["AccentTextFillColorSecondaryBrush"] = Frozen(accent);
		_overrides["AccentTextFillColorTertiaryBrush"] = Frozen(accent);
		_overrides["HbSelectionBrush"] = Frozen(palette.Selection ?? accent);
		_overrides["HbSelectionForegroundBrush"] = Frozen(palette.SelectionForeground ?? color);
		mergedDictionaries.Add(_overrides);
	}

	private static SolidColorBrush Frozen(Color c)
	{
		SolidColorBrush solidColorBrush = new SolidColorBrush(c);
		solidColorBrush.Freeze();
		return solidColorBrush;
	}

	private static Color C(string hex)
	{
		return (Color)ColorConverter.ConvertFromString(hex);
	}

	private static bool IsLight(Color c)
	{
		return (0.299 * (double)(int)c.R + 0.587 * (double)(int)c.G + 0.114 * (double)(int)c.B) / 255.0 > 0.6;
	}

	private static Palette Resolve(string? id)
	{
		if (string.Equals(id, "System", StringComparison.Ordinal))
		{
			if (DetectSystemTheme() != ApplicationTheme.Dark)
			{
				return PaletteRegistry.ClassicLight;
			}
			return PaletteRegistry.ClassicDark;
		}
		Palette palette = PaletteRegistry.Find(id);
		if ((object)palette == null)
		{
			if (DetectSystemTheme() != ApplicationTheme.Dark)
			{
				return PaletteRegistry.ClassicLight;
			}
			palette = PaletteRegistry.ClassicDark;
		}
		return palette;
	}

	private static ApplicationTheme DetectSystemTheme()
	{
		try
		{
			ApplicationTheme result;
			switch (ApplicationThemeManager.GetSystemTheme())
			{
			case SystemTheme.Dark:
			case SystemTheme.Glow:
			case SystemTheme.CapturedMotion:
			case SystemTheme.Sunrise:
				result = ApplicationTheme.Dark;
				break;
			case SystemTheme.HCWhite:
			case SystemTheme.HCBlack:
			case SystemTheme.HC1:
			case SystemTheme.HC2:
				result = ApplicationTheme.HighContrast;
				break;
			default:
				result = ApplicationTheme.Light;
				break;
			}
			return result;
		}
		catch
		{
			return ApplicationTheme.Light;
		}
	}
}
