using System;

namespace HebrewBooks.UI.Services;

public sealed record DonationProgress(double Goal, double Raised, string Currency)
{
	public int Percent
	{
		get
		{
			if (!(Goal <= 0.0))
			{
				return Math.Clamp((int)Math.Round(Raised / Goal * 100.0), 0, 100);
			}
			return 0;
		}
	}
}
