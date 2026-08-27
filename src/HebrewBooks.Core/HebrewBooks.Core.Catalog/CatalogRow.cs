namespace HebrewBooks.Core.Catalog;

public abstract class CatalogRow
{
	public bool IsGroupHeader => this is GroupHeaderRow;
}
