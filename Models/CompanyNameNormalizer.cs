using System.Net;
using System.Text.RegularExpressions;

namespace JobParser.Models;

/// <summary>Normalizes employer names collected from vacancy cards and SEO titles.</summary>
public static class CompanyNameNormalizer
{
    public static string Clean(string? value)
    {
        var company = WebUtility.HtmlDecode(value ?? "").Replace('\u00a0', ' ');
        company = Regex.Replace(company, @"\s+", " ").Trim();
        if (company.Length == 0) return "";

        // Habr Career appends its brand to the employer in card/title text:
        // "СОГАЗ» — Хабр Карьера" / "Джетлин» — Хабр Карьера".
        company = Regex.Replace(company,
            @"\s*(?:[—–|]|-)\s*Хабр\s+Карьера\s*$", "",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();

        // Some cards expose the whole SEO title instead of the employer field.
        var employer = Regex.Match(company,
            @"(?i)работа\s+в\s+компании\s+(.+?)(?=\s*[—–-]\s*Хабр\s+Карьера|,\s*(?:полный|неполный)\s+рабочий\s+день|,\s*опубликована|$)");
        if (employer.Success) company = employer.Groups[1].Value.Trim();

        company = company.Trim(' ', '"', '\'', '«', '»', '“', '”', '„', '‟', ',', ';', '|', '—', '–', '-');
        if (company.Length == 0) return "";
        if (Regex.IsMatch(company,
            @"(?i)проверено\s+(?:работой\.ру|rabota\.ru)|работой\.ру|проверенный?\s+работодатель|полный\s+рабочий\s+день|неполный\s+рабочий\s+день|опубликована\s+\d"))
            return "";
        return company;
    }
}
