using HebrewBooks.Core.Abstractions;
using HebrewBooks.Infrastructure.Settings;

namespace HebrewBooks.UI.Services;

public sealed class TelemetryConsent(JsonSettingsStore settings) : ITelemetryConsent
{
	public bool IsGranted => settings.Load().UsageTelemetryConsent == true;
}
