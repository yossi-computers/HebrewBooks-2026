using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace HebrewBooks.Services.Toc;

public sealed record TocBundle(string Format, IReadOnlyList<TocBundleEntry> Books)
{
	public const string CurrentFormat = "HebrewBooks-TOC-v1";

	[CompilerGenerated]
	private TocBundle(TocBundle original)
	{
		Format = original.Format;
		Books = original.Books;
	}
}
