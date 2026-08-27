using System;
using System.Collections.Generic;
using System.IO;
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

public sealed class RemoteSearchClient
{
	public sealed record Options(int Proximity, bool Hybur, bool Roots, bool Gematria, bool Spelling, bool NumberGender, bool Aramaic, bool RasheyTevot, bool RequireWordOrder, bool RashiOcr, int Fuzziness, int MaxFiles, IReadOnlyCollection<string>? Corpora, IReadOnlyCollection<string>? RestrictFileIds);

	public sealed record InBookOptions(bool Hybur, bool Roots, bool Gematria, bool Spelling, bool NumberGender, bool Aramaic, bool RasheyTevot, bool RequireWordOrder, bool RashiOcr, int Fuzziness, int Proximity = 30);

	private sealed class InBookDto
	{
		[JsonPropertyName("hitCount")]
		public int HitCount { get; set; }

		[JsonPropertyName("pages")]
		public int[]? Pages { get; set; }

		[JsonPropertyName("matchedTerms")]
		public string[]? MatchedTerms { get; set; }

		[JsonPropertyName("highlightXml")]
		public string? HighlightXml { get; set; }
	}

	private sealed record ResultDto([property: JsonPropertyName("fileId")] string? FileId, [property: JsonPropertyName("bookName")] string? BookName, [property: JsonPropertyName("authorName")] string? AuthorName, [property: JsonPropertyName("printPlace")] string? PrintPlace, [property: JsonPropertyName("printYear")] string? PrintYear, [property: JsonPropertyName("countPage")] int? CountPage, [property: JsonPropertyName("categories")] string? Categories, [property: JsonPropertyName("sourceType")] string? SourceType, [property: JsonPropertyName("relativePath")] string? RelativePath, [property: JsonPropertyName("hitCount")] int HitCount, [property: JsonPropertyName("firstHitPage")] int? FirstHitPage);

	private static readonly HttpClient Http = new HttpClient
	{
		Timeout = TimeSpan.FromMinutes(5.0)
	};

	private static readonly JsonSerializerOptions JsonIn = new JsonSerializerOptions(JsonSerializerDefaults.Web);

	public async Task<IReadOnlyList<SearchResultRow>> SearchAsync(string baseUrl, string rawQuery, Options opts, IProgress<SearchResultRow>? progress, CancellationToken ct = default(CancellationToken))
	{
		int proximity = Math.Max(1, opts.Proximity);
		int fuzziness = Math.Clamp(opts.Fuzziness, 0, 10);
		int max = Math.Max(1, opts.MaxFiles);
		bool hybur = opts.Hybur;
		bool roots = opts.Roots;
		bool gematria = opts.Gematria;
		bool spelling = opts.Spelling;
		bool numberGender = opts.NumberGender;
		bool aramaic = opts.Aramaic;
		bool rasheyTevot = opts.RasheyTevot;
		bool requireWordOrder = opts.RequireWordOrder;
		bool rashiOcr = opts.RashiOcr;
		IReadOnlyCollection<string> corpora = opts.Corpora;
		string[] corpus = ((corpora != null && corpora.Count > 0) ? opts.Corpora.ToArray() : null);
		corpora = opts.RestrictFileIds;
		var inputValue = new
		{
			q = rawQuery,
			proximity = proximity,
			fuzziness = fuzziness,
			max = max,
			hybur = hybur,
			roots = roots,
			gematria = gematria,
			spelling = spelling,
			numberGender = numberGender,
			aramaic = aramaic,
			rashetevot = rasheyTevot,
			requireWordOrder = requireWordOrder,
			rashiOcr = rashiOcr,
			corpus = corpus,
			restrictFileIds = ((corpora != null && corpora.Count > 0) ? opts.RestrictFileIds.ToArray() : null)
		};
		List<SearchResultRow> rows = new List<SearchResultRow>();
		using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, Combine(baseUrl, "search"))
		{
			Content = JsonContent.Create(inputValue)
		};
		using HttpResponseMessage resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(continueOnCapturedContext: false);
		resp.EnsureSuccessStatusCode();
		IReadOnlyList<SearchResultRow> result;
		await using (Stream stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(continueOnCapturedContext: false))
		{
			using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
			while (true)
			{
				string text = await reader.ReadLineAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
				if (text == null)
				{
					break;
				}
				if (text.Length != 0)
				{
					ResultDto resultDto;
					try
					{
						resultDto = JsonSerializer.Deserialize<ResultDto>(text, JsonIn);
					}
					catch
					{
						continue;
					}
					if ((object)resultDto != null)
					{
						SearchResultRow searchResultRow = ToRow(resultDto);
						rows.Add(searchResultRow);
						progress?.Report(searchResultRow);
					}
				}
			}
			result = rows;
		}
		return result;
	}

	public async Task<InBookHitInfo> GetInBookHitsAsync(string baseUrl, string fileName, string rawQuery, InBookOptions o, string? displayQuery = null, CancellationToken ct = default(CancellationToken))
	{
		var inputValue = new
		{
			fileName = fileName,
			q = rawQuery,
			displayQuery = (displayQuery ?? rawQuery),
			proximity = ((o.Proximity <= 0) ? 30 : o.Proximity),
			fuzziness = Math.Clamp(o.Fuzziness, 0, 10),
			hybur = o.Hybur,
			roots = o.Roots,
			gematria = o.Gematria,
			spelling = o.Spelling,
			numberGender = o.NumberGender,
			aramaic = o.Aramaic,
			rashetevot = o.RasheyTevot,
			requireWordOrder = o.RequireWordOrder,
			rashiOcr = o.RashiOcr
		};
		using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, Combine(baseUrl, "inbook"))
		{
			Content = JsonContent.Create(inputValue)
		};
		using HttpResponseMessage resp = await Http.SendAsync(req, ct).ConfigureAwait(continueOnCapturedContext: false);
		resp.EnsureSuccessStatusCode();
		InBookDto inBookDto = (await resp.Content.ReadFromJsonAsync<InBookDto>(ct).ConfigureAwait(continueOnCapturedContext: false)) ?? new InBookDto();
		return new InBookHitInfo(inBookDto.HitCount, inBookDto.Pages ?? Array.Empty<int>(), inBookDto.MatchedTerms ?? Array.Empty<string>(), inBookDto.HighlightXml ?? string.Empty);
	}

	public async Task<bool> IsHealthyAsync(string baseUrl, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			using HttpResponseMessage httpResponseMessage = await Http.GetAsync(Combine(baseUrl, "health"), ct).ConfigureAwait(continueOnCapturedContext: false);
			return httpResponseMessage.IsSuccessStatusCode;
		}
		catch
		{
			return false;
		}
	}

	private static string Combine(string baseUrl, string path)
	{
		return baseUrl.TrimEnd('/') + "/" + path;
	}

	private static SearchResultRow ToRow(ResultDto d)
	{
		return new SearchResultRow(new Book
		{
			FileID = d.FileId,
			BookName = d.BookName,
			AuthorName = d.AuthorName,
			PrintPlace = d.PrintPlace,
			PrintYear = d.PrintYear,
			CountPage = d.CountPage,
			Categories = d.Categories,
			SourceType = (string.IsNullOrEmpty(d.SourceType) ? "PDF" : d.SourceType),
			RelativePath = d.RelativePath
		}, d.HitCount, d.FileId ?? string.Empty, d.FirstHitPage);
	}
}
