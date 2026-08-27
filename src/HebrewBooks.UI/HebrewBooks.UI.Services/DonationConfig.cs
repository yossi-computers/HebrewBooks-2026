namespace HebrewBooks.UI.Services;

public sealed record DonationConfig(string DonateUrl, double DedicationPrice, string Currency, int Days)
{
	public bool Enabled => !string.IsNullOrWhiteSpace(DonateUrl);
}
