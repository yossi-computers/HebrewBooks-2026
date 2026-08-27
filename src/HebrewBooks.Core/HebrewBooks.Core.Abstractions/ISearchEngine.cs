using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Models;

namespace HebrewBooks.Core.Abstractions;

public interface ISearchEngine
{
	Func<string, string?>? FileNameToCatalogId { get; set; }

	Task OpenIndexAsync(string indexPath, CancellationToken ct = default(CancellationToken));

	Task BuildIndexAsync(IndexSpec spec, IProgress<double>? progress, IProgress<IndexProgressReport>? detail = null, CancellationToken ct = default(CancellationToken));

	Task UpdateIndexForFilesAsync(IndexSpec spec, IReadOnlyList<string> changedPaths, IReadOnlyList<string> deletedPaths, IProgress<double>? progress = null, IProgress<IndexProgressReport>? detail = null, CancellationToken ct = default(CancellationToken));

	Task<IReadOnlyList<SearchHit>> SearchAsync(SearchQuery query, IProgress<SearchHit>? progress = null, CancellationToken ct = default(CancellationToken));

	Task<IReadOnlyList<HitSpan>> ExtractHighlightsAsync(SearchHit hit, CancellationToken ct = default(CancellationToken));

	ISet<string> FilterIndexedWords(IReadOnlyCollection<string> words);

	IReadOnlyDictionary<string, long> GetIndexWordCounts(IReadOnlyCollection<string> words)
	{
		return new Dictionary<string, long>(StringComparer.Ordinal);
	}

	IReadOnlyList<IndexWord> SuggestIndexWords(string word, int fuzziness = 3, int maxResults = 24)
	{
		return Array.Empty<IndexWord>();
	}

	Task<InBookHitInfo> GetInBookHitsAsync(string fileName, string queryText, bool extractTerms = true, int fuzziness = 0, CancellationToken ct = default(CancellationToken));

	Task<string?> GenerateHighlightedPdfAsync(string fileName, string queryText, CancellationToken ct = default(CancellationToken));

	void RemoveDocumentsFromIndex(string indexPath, IReadOnlyList<string> absolutePaths);

	void AddDocumentsToIndex(string indexPath, IReadOnlyList<string> absolutePaths);
}
