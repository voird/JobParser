using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using JobParser.Models;

namespace JobParser.Parsers;

public static class VacancyDetailEnricher
{
    public static async Task<IReadOnlyList<Vacancy>> EnrichAsync(
        HttpClient client, IEnumerable<Vacancy> vacancies, CancellationToken ct = default)
    {
        var input = vacancies.GroupBy(v => v.Url, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First()).ToList();
        using var gate = new SemaphoreSlim(5);
        var tasks = input.Select(async vacancy =>
        {
            if (string.IsNullOrWhiteSpace(vacancy.Url)) return vacancy;
            await gate.WaitAsync(ct);
            try
            {
                using var response = await client.GetAsync(vacancy.Url, ct);
                if (!response.IsSuccessStatusCode) return vacancy;
                var html = await response.Content.ReadAsStringAsync(ct);
                return Enrich(vacancy, html);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { return vacancy; }
            finally { gate.Release(); }
        });
        return await Task.WhenAll(tasks);
    }

    public static Vacancy Enrich(Vacancy vacancy, string html)
    {
        if (IsPlaceholder(vacancy.Company)) vacancy.Company = "";
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var rabotaCompany = ExtractRabotaCompany(doc);
        var rabotaRegion = ExtractRabotaRegion(doc);

        var structured = new Vacancy();
        var jsonRoots = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");
        if (jsonRoots != null)
        {
            foreach (var node in jsonRoots)
            {
                try { ReadJson(JsonDocument.Parse(WebUtility.HtmlDecode(node.InnerText)).RootElement, structured); }
                catch { /* Some sites emit malformed or partial JSON-LD. */ }
            }
        }

        var pageCompany = ByCompanySelectors(doc);
        vacancy.Company = First(CleanCompanyName(rabotaCompany), CleanCompanyName(structured.Company),
            ExtractMetaCompany(doc), CleanCompanyName(vacancy.Company), CleanCompanyName(pageCompany));
        vacancy.Region = First(rabotaRegion, NormalizeCity(structured.Region), NormalizeCity(vacancy.Region));
        vacancy.Salary = First(structured.Salary, vacancy.Salary,
            Meta(doc, "salary"), Meta(doc, "og:salary"),
            BySelectors(doc, "//*[contains(@class,'salary') or contains(@class,'compensation') or contains(@data-qa,'salary')]", 250),
            SalaryFromText(Clean(doc.DocumentNode.InnerText)));
        vacancy.Requirements = First(structured.Requirements, vacancy.Requirements,
            BySelectors(doc, "//*[contains(@class,'description') or contains(@class,'requirement') or contains(@data-qa,'description')]"),
            Meta(doc, "description"));
        vacancy.Company = CleanCompanyName(vacancy.Company);
        vacancy.Salary = Clean(vacancy.Salary);
        vacancy.Requirements = Clean(vacancy.Requirements);
        return vacancy;
    }

    private static void ReadJson(JsonElement e, Vacancy v)
    {
        if (e.ValueKind == JsonValueKind.Array) { foreach (var x in e.EnumerateArray()) ReadJson(x, v); return; }
        if (e.ValueKind != JsonValueKind.Object) return;
        foreach (var p in e.EnumerateObject())
        {
            if (p.Name.Equals("hiringOrganization", StringComparison.OrdinalIgnoreCase) ||
                p.Name.Equals("employer", StringComparison.OrdinalIgnoreCase))
                v.Company = First(v.Company, ReadName(p.Value));
            if (p.Name.Equals("jobLocation", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(v.Region))
                v.Region = ReadLocation(p.Value);
            if (p.Name.Equals("baseSalary", StringComparison.OrdinalIgnoreCase) ||
                p.Name.Equals("salary", StringComparison.OrdinalIgnoreCase))
                v.Salary = First(v.Salary, ReadSalary(p.Value));
            if (p.Name.Equals("description", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(v.Requirements))
                v.Requirements = Clean(p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ToString());
            if (p.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array) ReadJson(p.Value, v);
        }
    }

    private static string ReadLocation(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Array)
            return string.Join(" · ", e.EnumerateArray().Select(ReadLocation).Where(x => x.Length > 0));
        if (e.ValueKind != JsonValueKind.Object) return "";

        if (e.TryGetProperty("address", out var address))
        {
            if (address.ValueKind == JsonValueKind.String)
                return NormalizeCity(address.GetString());
            if (address.ValueKind == JsonValueKind.Object)
            {
                var locality = Get(address, "addressLocality");
                if (!string.IsNullOrWhiteSpace(locality)) return NormalizeCity(locality);
                var fallback = Get(address, "name");
                return NormalizeCity(fallback);
            }
        }
        if (e.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
            return NormalizeCity(name.GetString());
        return "";
    }

    private static string NormalizeCity(string? value)
    {
        var text = Clean(value);
        if (text.Length == 0) return "";
        var parts = text.Split(new[] { ',', ';', '|', '·' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "RU", "RUS", "Россия", "Российская Федерация", "Russian Federation",
            "Московская область", "Московская обл.", "Россия."
        };
        var city = parts.FirstOrDefault(p => !ignored.Contains(p) &&
            !Regex.IsMatch(p, @"^(?:\{|\}|\[|\]|null|undefined)$") &&
            !Regex.IsMatch(p, @"(?i)^(?:область|край|республика)\b"));
        return city?.Trim(' ', '"', '\'', '«', '»', '“', '”', '{', '}', '[', ']') ?? "";
    }

    private static string ExtractMetaCompany(HtmlDocument doc)
    {
        var title = First(Meta(doc, "og:title"), Clean(doc.DocumentNode.SelectSingleNode("//title")?.InnerText));
        if (title.Length == 0) return "";
        var match = Regex.Match(title,
            @"(?i)работа\s+в\s+компании\s+(.+?)(?=,\s*(?:полный|неполный)\s+рабочий\s+день|,\s*опубликована|\s+[—–-]\s*Хабр\s+Карьера|$)");
        return match.Success ? CleanCompanyName(match.Groups[1].Value) : "";
    }

    private static string CleanCompanyName(string? value) => CompanyNameNormalizer.Clean(value);
    private static string ExtractRabotaCompany(HtmlDocument doc)
    {

        var selectors = new[]
        {
            "//*[contains(concat(' ', normalize-space(@class), ' '), ' vacancy-company-stats__name ')]//a[@itemprop='legalName']",
            "//a[@itemprop='legalName']",
            "//*[contains(concat(' ', normalize-space(@class), ' '), ' vacancy-company-stats__name ')]//meta[@itemprop='name']",
            "//meta[@itemprop='name']"
        };

        foreach (var xpath in selectors)
        {
            var nodes = doc.DocumentNode.SelectNodes(xpath);
            if (nodes == null) continue;
            foreach (var node in nodes)
            {
                var value = node.Name.Equals("meta", StringComparison.OrdinalIgnoreCase)
                    ? node.GetAttributeValue("content", "")
                    : node.InnerText;
                value = Clean(value);
                if (!string.IsNullOrWhiteSpace(value) && !IsPlaceholder(value) &&
                    !value.Contains("Проверено", StringComparison.OrdinalIgnoreCase))
                    return value;
            }
        }

        var title = Meta(doc, "og:title");
        var match = Regex.Match(title, @"(?i)работа\s+в\s+компании\s+(.+?)\s*$");
        return match.Success ? Clean(match.Groups[1].Value) : "";
    }

    private static string ExtractRabotaRegion(HtmlDocument doc)
    {
        var city = doc.DocumentNode.SelectSingleNode(
            "//*[contains(concat(' ', normalize-space(@class), ' '), ' vacancy-requirements__city ')]");
        var value = Clean(city?.InnerText);
        if (!string.IsNullOrWhiteSpace(value))
            return value.Trim(' ', ',', '·', '|');

        var address = doc.DocumentNode.SelectSingleNode(
            "//*[contains(concat(' ', normalize-space(@class), ' '), ' vacancy-locations__address ')]");
        value = Clean(address?.InnerText);
        if (string.IsNullOrWhiteSpace(value)) return "";

        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length > 1 ? parts[^1] : value;
    }

    private static string ByCompanySelectors(HtmlDocument doc)
    {
        var nodes = doc.DocumentNode.SelectNodes(
            "//*[contains(@class,'company') or contains(@class,'employer') or contains(@data-qa,'employer')]");
        if (nodes == null) return "";

        foreach (var node in nodes.OrderBy(n => n.InnerText?.Length ?? int.MaxValue))
        {
            var classes = node.GetAttributeValue("class", "");
            if (classes.Contains("verified-employer", StringComparison.OrdinalIgnoreCase) ||
                classes.Contains("badge", StringComparison.OrdinalIgnoreCase))
                continue;

            var nameNode = node.SelectSingleNode(".//a[@itemprop='legalName'] | .//*[@itemprop='name']");
            var value = node.InnerText;
            if (nameNode is not null)
            {
                var content = nameNode.GetAttributeValue("content", "");
                value = string.IsNullOrWhiteSpace(content) ? nameNode.InnerText : content;
            }
            value = Clean(value);
            if (value.Length > 0 && value.Length <= 250 && !IsPlaceholder(value))
                return value;
        }
        return "";
    }

    private static bool IsPlaceholder(string? s) => !string.IsNullOrWhiteSpace(s) && Regex.IsMatch(s, @"(?i)проверено\s+(?:работой\.ру|rabota\.ru)|работой\.ру|проверенный?\s+работодатель");
    private static string ReadName(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.String) return e.GetString() ?? "";
        if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("name", out var n)) return n.ToString();
        return "";
    }

    private static string ReadSalary(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.String) return e.GetString() ?? "";
        if (e.ValueKind != JsonValueKind.Object) return "";
        var currency = Get(e, "currency");
        var value = e.TryGetProperty("value", out var val) ? val : e;
        if (value.ValueKind == JsonValueKind.Object)
        {
            var min = Get(value, "minValue"); var max = Get(value, "maxValue");
            if (min.Length > 0 || max.Length > 0) return $"{min}–{max} {currency}".Trim();
            var single = Get(value, "value");
            return single.Length > 0 ? $"{single} {currency}".Trim() : "";
        }
        return value.ToString() + " " + currency;
    }

    private static string Get(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var x) ? x.ToString() : "";

    private static string Meta(HtmlDocument d, string key) =>
        Clean(d.DocumentNode.SelectSingleNode($"//meta[@property='{key}' or @name='{key}']")?.GetAttributeValue("content", ""));

    private static string BySelectors(HtmlDocument d, string xpath, int maxLength = 10000)
    {
        var nodes = d.DocumentNode.SelectNodes(xpath);
        if (nodes == null) return "";
        return nodes.Select(n => Clean(n.InnerText))
            .Where(value => value.Length > 0 && value.Length <= maxLength)
            .OrderBy(value => value.Length)
            .FirstOrDefault() ?? "";
    }

    private static string SalaryFromText(string text)
    {
        var m = Regex.Match(text, @"(?i)(?:от|до)?\s?\d[\d\s\u00a0]{2,}(?:[.,]\d{1,2})?\s?(?:₽|руб\.?|RUB|USD|EUR|€|\$)(?:\s*(?:—|–|-)\s*(?:от|до)?\s?\d[\d\s\u00a0]{2,}(?:[.,]\d{1,2})?\s?(?:₽|руб\.?|RUB|USD|EUR|€|\$))?");
        return m.Success ? m.Value.Trim() : "";
    }

    private static string First(params string[] values) => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";
    private static string Clean(string? value) => Regex.Replace(WebUtility.HtmlDecode(value ?? "").Replace('\u00a0',' '), @"\s+", " ").Trim();
}
