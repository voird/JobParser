using System.Web;
using JobParser.Configuration;
using JobParser.Models;

namespace JobParser.Parsers;

public sealed class HttpClientVacancyParser(IHttpClientFactory factory, SiteConfig site) : IVacancyParser
{
    public string Id => $"{site.Key}-HttpClient";
    public string SiteKey => site.Key;
    public string SiteName => site.Name;
    public string Method => "HttpClient";

    public async Task<IReadOnlyList<Vacancy>> ParseAsync(CancellationToken ct = default)
    {
        var client = factory.CreateClient("scraper");
        var all = new List<Vacancy>();
        foreach (var term in ProfessionFilter.SearchTerms)
        {
            ct.ThrowIfCancellationRequested();
            for (var page = 1; page <= 3; page++)
            {
                var url = BuildSearchUrl(term, page);
                var response = await client.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode) break;
                var html = await response.Content.ReadAsStringAsync(ct);
                var found = HtmlVacancyExtractor.Extract(html, site);
                all.AddRange(found);
                if (found.Count == 0) break;
            }
        }
        return await VacancyDetailEnricher.EnrichAsync(client, all, ct);
    }

    private string BuildSearchUrl(string term, int page)
    {
        var separator = site.SearchUrl.Contains('?') ? "&" : "?";
        return site.SearchUrl + separator + Uri.EscapeDataString(site.QueryParameter) + "=" + Uri.EscapeDataString(term) + (page > 1 ? "&page=" + page : "");
    }
}
