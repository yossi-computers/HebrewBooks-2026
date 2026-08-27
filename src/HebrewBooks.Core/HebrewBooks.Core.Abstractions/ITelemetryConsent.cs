namespace HebrewBooks.Core.Abstractions;

public interface ITelemetryConsent
{
	bool IsGranted { get; }
}
