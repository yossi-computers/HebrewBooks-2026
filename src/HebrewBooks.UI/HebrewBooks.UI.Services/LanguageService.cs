using System.Globalization;
using System.Windows;
using HebrewBooks.Core.Resources;
using HebrewBooks.UI.Resources;

namespace HebrewBooks.UI.Services;

public static class LanguageService
{
	public const string Auto = "auto";

	public const string Hebrew = "he";

	public const string English = "en";

	public static string Resolve(string? setting)
	{
		string text = (setting ?? "auto").Trim().ToLowerInvariant();
		if ((text == "he" || text == "en") ? true : false)
		{
			return text;
		}
		if (!(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "he"))
		{
			return "en";
		}
		return "he";
	}

	public static FlowDirection FlowFor(string lang)
	{
		if (!(lang == "en"))
		{
			return FlowDirection.RightToLeft;
		}
		return FlowDirection.LeftToRight;
	}

	public static string Apply(string? setting)
	{
		string text = Resolve(setting);
		CoreStrings.Culture = (SharedStrings.Culture = ((text == "en") ? new CultureInfo("en") : CultureInfo.InvariantCulture));
		return text;
	}
}
