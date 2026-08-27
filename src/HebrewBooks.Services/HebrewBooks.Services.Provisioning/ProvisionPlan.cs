namespace HebrewBooks.Services.Provisioning;

public sealed record ProvisionPlan(bool Index, bool Books, bool BuildIndexLocally)
{
	public bool HasWork
	{
		get
		{
			if (!Index && !Books)
			{
				return BuildIndexLocally;
			}
			return true;
		}
	}
}
