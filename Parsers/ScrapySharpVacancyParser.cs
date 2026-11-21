using JobParser.Configuration;
using JobParser.Models;
using ScrapySharp.Network;

namespace JobParser.Parsers;

public sealed class ScrapySharpVacancyParser(IHttpClientFactory factory, SiteConfig site) : IVacancyParser
{
    public string Id => $"{site.Key}-ScrapySharp";
    public string SiteKey => site.Key;
    public string SiteName => site.Name;
    public string Method => "ScrapySharp";

    public async Task<IReadOnlyList<Vacancy>> ParseAsync(CancellationToken ct = default)
    {
        var browser = new ScrapingBrowser();
        var all = new List<Vacancy>();
        foreach (var term in ProfessionFilter.SearchTerms)
        {
            ct.ThrowIfCancellationRequested();
            for (var page = 1; page <= 3; page++)
            {
                var separator = site.SearchUrl.Contains('?') ? "&" : "?";
                var url = site.SearchUrl + separator + Uri.EscapeDataString(site.QueryParameter) + "=" + Uri.EscapeDataString(term) + (page > 1 ? "&page=" + page : "");
                var html = await browser.DownloadStringAsync(new Uri(url));
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(html)) break;
                var found = HtmlVacancyExtractor.Extract(html, site);
                all.AddRange(found);
                if (found.Count == 0) break;
            }
        }
        return await VacancyDetailEnricher.EnrichAsync(factory.CreateClient("scraper"), all, ct);
    }
}
