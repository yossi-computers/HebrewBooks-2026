using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.Core.Models;
using HebrewBooks.Services.Search;

namespace HebrewBooks.UI.Services;

public sealed class WebApiClient
{
	public sealed record Options(int Proximity, bool Hybur, bool Roots, bool Gematria, bool Spelling, bool NumberGender, bool Aramaic, bool RasheyTevot, bool RequireWordOrder, bool RashiOcr, int Fuzziness, int MaxFiles, string? Corpus, string Sort, IReadOnlyCollection<string>? RestrictFileIds, IReadOnlyCollection<KeyValuePair<string, string>>? Synonyms);

	public sealed record SearchOutcome(IReadOnlyList<SearchResultRow> Rows, string? OriginalQuery, string? CorrectedQuery, IReadOnlyList<string> HighlightTerms);

	public sealed record InBookOptions(bool Hybur, bool Roots, bool Gematria, bool Spelling, bool NumberGender, bool Aramaic, bool RasheyTevot, bool RequireWordOrder, bool RashiOcr, int Fuzziness, int Proximity = 30);

	private sealed class SearchResponseDto
	{
		[JsonPropertyName("count")]
		public int Count { get; set; }

		[JsonPropertyName("highlightTerms")]
		public string[]? HighlightTerms { get; set; }

		[JsonPropertyName("originalQuery")]
		public string? OriginalQuery { get; set; }

		[JsonPropertyName("correctedQuery")]
		public string? CorrectedQuery { get; set; }

		[JsonPropertyName("results")]
		public List<ResultDto>? Results { get; set; }
	}

	private sealed record ResultDto([property: JsonPropertyName("bookId")] int BookId, [property: JsonPropertyName("fileId")] string? FileId, [property: JsonPropertyName("bookName")] string? BookName, [property: JsonPropertyName("authorName")] string? AuthorName, [property: JsonPropertyName("printPlace")] string? PrintPlace, [property: JsonPropertyName("printYear")] string? PrintYear, [property: JsonPropertyName("sourceType")] string? SourceType, [property: JsonPropertyName("hitCount")] int HitCount, [property: JsonPropertyName("pageNumber")] int? PageNumber);

	private sealed class InBookDto
	{
		[JsonPropertyName("hitCount")]
		public int HitCount { get; set; }

		[JsonPropertyName("pages")]
		public int[]? Pages { get; set; }

		[JsonPropertyName("matchedTerms")]
		public string[]? MatchedTerms { get; set; }

		[JsonPropertyName("highlightTerms")]
		public string[]? HighlightTerms { get; set; }
	}

	private static readonly HttpClient Http = new HttpClient
	{
		Timeout = TimeSpan.FromMinutes(5.0)
	};

	private static readonly JsonSerializerOptions JsonIn = new JsonSerializerOptions(JsonSerializerDefaults.Web);

	private const int MaxScopeIds = 400;

	public async Task<SearchOutcome> SearchAsync(string siteBase, string rawQuery, Options o, IProgress<SearchResultRow>? progress, CancellationToken ct = default(CancellationToken))
	{
		string requestUri = BuildSearchUrl(siteBase, rawQuery, o);
		using HttpResponseMessage resp = await Http.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(continueOnCapturedContext: false);
		resp.EnsureSuccessStatusCode();
		SearchResponseDto searchResponseDto = (await resp.Content.ReadFromJsonAsync<SearchResponseDto>(JsonIn, ct).ConfigureAwait(continueOnCapturedContext: false)) ?? new SearchResponseDto();
		List<ResultDto>? obj = searchResponseDto.Results ?? new List<ResultDto>();
		List<SearchResultRow> list = new List<SearchResultRow>(obj.Count);
		foreach (ResultDto item in obj)
		{
			SearchResultRow searchResultRow = ToRow(item);
			list.Add(searchResultRow);
			progress?.Report(searchResultRow);
		}
		return new SearchOutcome(list, searchResponseDto.OriginalQuery, searchResponseDto.CorrectedQuery, searchResponseDto.HighlightTerms ?? Array.Empty<string>());
	}

	public async Task<InBookHitInfo> GetInBookHitsAsync(string siteBase, string fileId, string rawQuery, InBookOptions o, CancellationToken ct = default(CancellationToken))
	{
		StringBuilder stringBuilder = new StringBuilder();
		Add(stringBuilder, "fileId", fileId);
		Add(stringBuilder, "q", rawQuery);
		Add(stringBuilder, "maxProximity", ((o.Proximity <= 0) ? 30 : o.Proximity).ToString());
		Add(stringBuilder, "fuzziness", Math.Clamp(o.Fuzziness, 0, 10).ToString());
		Add(stringBuilder, "hybur", Bool(o.Hybur));
		Add(stringBuilder, "roots", Bool(o.Roots));
		Add(stringBuilder, "gematria", Bool(o.Gematria));
		Add(stringBuilder, "spelling", Bool(o.Spelling));
		Add(stringBuilder, "numberGender", Bool(o.NumberGender));
		Add(stringBuilder, "aramaic", Bool(o.Aramaic));
		Add(stringBuilder, "rasheyTevot", Bool(o.RasheyTevot));
		Add(stringBuilder, "rashiOcr", Bool(o.RashiOcr));
		Add(stringBuilder, "requireWordOrder", Bool(o.RequireWordOrder));
		string requestUri = $"{ApiBase(siteBase)}/search/inbook?{stringBuilder}";
		using HttpResponseMessage resp = await Http.GetAsync(requestUri, ct).ConfigureAwait(continueOnCapturedContext: false);
		resp.EnsureSuccessStatusCode();
		InBookDto inBookDto = (await resp.Content.ReadFromJsonAsync<InBookDto>(JsonIn, ct).ConfigureAwait(continueOnCapturedContext: false)) ?? new InBookDto();
		string[] matchedTerms = inBookDto.MatchedTerms ?? inBookDto.HighlightTerms ?? Array.Empty<string>();
		int[] array = inBookDto.Pages ?? Array.Empty<int>();
		string highlightXml = ((array.Length == 0) ? string.Empty : string.Concat(array.Select((int p) => $"<loc pg=\"{Math.Max(0, p - 1)}\" pos=\"0\" len=\"1\"></loc>")));
		return new InBookHitInfo(inBookDto.HitCount, array, matchedTerms, highlightXml);
	}

	public async Task<bool> IsHealthyAsync(string siteBase, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			using HttpResponseMessage httpResponseMessage = await Http.GetAsync(ApiBase(siteBase) + "/health", ct).ConfigureAwait(continueOnCapturedContext: false);
			return httpResponseMessage.IsSuccessStatusCode;
		}
		catch
		{
			return false;
		}
	}

	private static string ApiBase(string siteBase)
	{
		return siteBase.TrimEnd('/') + "/api";
	}

	private static string BuildSearchUrl(string siteBase, string rawQuery, Options o)
	{
		StringBuilder stringBuilder = new StringBuilder();
		Add(stringBuilder, "q", rawQuery);
		Add(stringBuilder, "hybur", Bool(o.Hybur));
		Add(stringBuilder, "rasheyTevot", Bool(o.RasheyTevot));
		Add(stringBuilder, "roots", Bool(o.Roots));
		Add(stringBuilder, "numberGender", Bool(o.NumberGender));
		Add(stringBuilder, "gematria", Bool(o.Gematria));
		Add(stringBuilder, "spelling", Bool(o.Spelling));
		Add(stringBuilder, "aramaic", Bool(o.Aramaic));
		Add(stringBuilder, "rashiOcr", Bool(o.RashiOcr));
		Add(stringBuilder, "requireWordOrder", Bool(o.RequireWordOrder));
		Add(stringBuilder, "maxProximity", Math.Max(1, o.Proximity).ToString());
		Add(stringBuilder, "fuzziness", Math.Clamp(o.Fuzziness, 0, 10).ToString());
		Add(stringBuilder, "maxFiles", Math.Max(1, o.MaxFiles).ToString());
		Add(stringBuilder, "includeNumbers", "true");
		if (!string.IsNullOrWhiteSpace(o.Corpus))
		{
			Add(stringBuilder, "corpus", o.Corpus);
		}
		if (!string.IsNullOrWhiteSpace(o.Sort))
		{
			Add(stringBuilder, "sort", o.Sort);
		}
		IReadOnlyCollection<KeyValuePair<string, string>> synonyms = o.Synonyms;
		if (synonyms != null && synonyms.Count > 0)
		{
			foreach (KeyValuePair<string, string> synonym in o.Synonyms)
			{
				Add(stringBuilder, "syn", synonym.Key + "|" + synonym.Value);
			}
		}
		IReadOnlyCollection<string> restrictFileIds = o.RestrictFileIds;
		if (restrictFileIds != null && restrictFileIds.Count > 0 && restrictFileIds.Count <= 400)
		{
			Add(stringBuilder, "restrictFileIds", string.Join(",", restrictFileIds));
		}
		return $"{ApiBase(siteBase)}/search?{stringBuilder}";
	}

	private static string Bool(bool b)
	{
		if (!b)
		{
			return "false";
		}
		return "true";
	}

	private static void Add(StringBuilder qs, string key, string value)
	{
		if (qs.Length > 0)
		{
			qs.Append('&');
		}
		qs.Append(key).Append('=').Append(Uri.EscapeDataString(value));
	}

	private static SearchResultRow ToRow(ResultDto d)
	{
		return new SearchResultRow(new Book
		{
			ID = d.BookId,
			FileID = d.FileId,
			BookName = d.BookName,
			AuthorName = d.AuthorName,
			PrintPlace = d.PrintPlace,
			PrintYear = d.PrintYear,
			SourceType = (string.IsNullOrEmpty(d.SourceType) ? "PDF" : d.SourceType)
		}, d.HitCount, d.FileId ?? string.Empty, d.PageNumber);
	}
}
