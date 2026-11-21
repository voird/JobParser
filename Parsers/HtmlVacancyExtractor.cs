using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using JobParser.Configuration;
using JobParser.Models;

namespace JobParser.Parsers;

public static class HtmlVacancyExtractor
{
    public static IReadOnlyList<Vacancy> Extract(string html, SiteConfig site)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var result = new List<Vacancy>();
        var links = doc.DocumentNode.SelectNodes("//a[@href]") ?? new HtmlNodeCollection(doc.DocumentNode);

        foreach (var link in links)
        {
            var title = Clean(link.InnerText);
            var profession = ProfessionFilter.Match(title);
            if (profession is null) continue;

            var href = WebUtility.HtmlDecode(link.GetAttributeValue("href", "")).Trim();
            if (!IsVacancyUrl(href, site)) continue;
            if (!Uri.TryCreate(new Uri(site.BaseUrl), href, out var absolute)) continue;

            var card = FindCard(link, title);
            if (card is null) continue;
            var text = Clean(card.InnerText);
            if (text.Length < title.Length || text.Length > 12000) continue;

            var company = FindByClass(card, "company", "employer", "firm");
            var salary = FindByClass(card, "salary", "compensation", "payment");
            var description = FindByClass(card, "description", "requirement", "snippet");
            result.Add(new Vacancy
            {
                Id = absolute.ToString(),
                Source = site.Name,
                Profession = profession,
                Title = title,
                Company = CleanCompany(company),
                Region = ExtractRegion(text),
                Salary = salary,
                Requirements = description,
                Url = absolute.ToString()
            });
        }

        return result
            .GroupBy(v => v.Url, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private static string CleanCompany(string value) => CompanyNameNormalizer.Clean(value);

    private static string ExtractRegion(string text)
    {
        var patterns = new[] { @"Москва", @"Санкт[- ]Петербург", @"Новосибирск", @"Екатеринбург",
            @"Казань", @"Нижний Новгород", @"Краснодар", @"Самара", @"Челябинск", @"Ростов[- ]на[- ]Дону",
            @"Уфа", @"Пермь", @"Воронеж", @"Красноярск", @"Тюмень", @"Томск", @"Иркутск",
            @"Владивосток", @"Ярославль", @"Омск", @"Россия,?\s*([^,;]+)" };
        foreach (var pattern in patterns) { var m=Regex.Match(text, pattern, RegexOptions.IgnoreCase); if(m.Success) return m.Value; }
        return "";
    }

    private static bool IsVacancyUrl(string href, SiteConfig site)
    {
        if (string.IsNullOrWhiteSpace(href) || href.StartsWith("#") ||
            href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)) return false;
        if (!Uri.TryCreate(new Uri(site.BaseUrl), href, out var uri)) return false;

        var baseHost = new Uri(site.BaseUrl).Host;
        if (!uri.Host.Equals(baseHost, StringComparison.OrdinalIgnoreCase) &&
            !uri.Host.EndsWith("." + baseHost, StringComparison.OrdinalIgnoreCase)) return false;

        var path = Uri.UnescapeDataString(uri.AbsolutePath);
        if (site.Key.Equals("HabrCareer", StringComparison.OrdinalIgnoreCase))
            return Regex.IsMatch(path, @"^/vacancies/\d+(?:-[^/]+)?/?$", RegexOptions.IgnoreCase);

        if (site.Key.Equals("SuperJob", StringComparison.OrdinalIgnoreCase))
            return Regex.IsMatch(path, @"^/vakansii/(?!search(?:/|$))[^/]*-\d+\.html$", RegexOptions.IgnoreCase)
                || Regex.IsMatch(path, @"^/vacancy/[^/]*\d+[^/]*\.html$", RegexOptions.IgnoreCase);

        if (site.Key.Equals("Rabota", StringComparison.OrdinalIgnoreCase))
            return Regex.IsMatch(path, @"^/vacancy/(?:\d+|[^/]*-\d+)/?$", RegexOptions.IgnoreCase)
                || Regex.IsMatch(path, @"^/vacancy/[^/]*\d+[^/]*\.html$", RegexOptions.IgnoreCase);

        return path.Contains(site.VacancyPath, StringComparison.OrdinalIgnoreCase)
            && path.TrimEnd('/') != site.VacancyPath.TrimEnd('/');
    }

    private static HtmlNode? FindCard(HtmlNode link, string title)
    {
        var node = link;
        for (var depth = 0; depth < 7 && node.ParentNode is not null; depth++, node = node.ParentNode)
        {
            var text = Clean(node.InnerText);
            if (text.Contains(title, StringComparison.OrdinalIgnoreCase) &&
                text.Length <= 12000 && node.Descendants("a").Count() <= 20)
                return node;
        }
        return null;
    }

    private static string FindByClass(HtmlNode node, params string[] tokens)
    {
        var candidate = node.Descendants()
            .Where(n => tokens.Any(t =>
                (n.GetAttributeValue("class", "") + " " + n.GetAttributeValue("data-qa", ""))
                .Contains(t, StringComparison.OrdinalIgnoreCase)))
            .Select(n => CleanCompany(n.InnerText))
            .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s) && s.Length < 1500);
        return candidate ?? "";
    }

    private static string Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var decoded = HtmlEntity.DeEntitize(value).Replace('\u00a0', ' ');
        return Regex.Replace(decoded, @"\s+", " ").Trim();
    }
}
