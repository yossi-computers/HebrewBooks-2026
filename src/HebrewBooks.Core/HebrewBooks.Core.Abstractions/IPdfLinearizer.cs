using System.Threading;
using System.Threading.Tasks;

namespace HebrewBooks.Core.Abstractions;

public interface IPdfLinearizer
{
	bool IsAvailable { get; }

	Task<bool> LinearizeInPlaceAsync(string pdfPath, CancellationToken ct = default(CancellationToken));
}
