using System.Globalization;
using System.Text;
using JobParser.Models;

namespace JobParser.Data;

public sealed class CsvVacancyStore(IWebHostEnvironment env)
{
    private readonly object sync = new();
    private string PathName => Path.Combine(env.ContentRootPath, "Data", "vacancies.csv");
    private static readonly string[] Header = { "Id", "Source", "Profession", "Title", "Company", "Salary", "Requirements", "Url", "ParsedAt", "Region" };

    public IReadOnlyList<Vacancy> GetAll()
    {
        lock (sync)
        {
            if (!File.Exists(PathName)) return Array.Empty<Vacancy>();
            var lines = File.ReadAllLines(PathName, Encoding.UTF8);
            if (lines.Length < 2) return Array.Empty<Vacancy>();
            var headers = Split(lines[0]);
            return lines.Skip(1).Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(line => Parse(Split(line), headers))
                .Where(v => v is not null).Cast<Vacancy>()
                .OrderByDescending(v => v.ParsedAt).ToList();
        }
    }

    public int AddRange(IEnumerable<Vacancy> items)
    {
        lock (sync)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
            var existing = GetAll().ToList();
            var byKey = new Dictionary<string, Vacancy>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in existing)
            {
                row.Company = CleanCompany(row.Company);
                var existingKey = Key(row);
                if (!byKey.TryGetValue(existingKey, out var stored))
                {
                    byKey[existingKey] = row;
                    continue;
                }

                // Collapse older duplicates that differ only by search-session query strings.
                stored.Company = Prefer(row.Company, stored.Company);
                stored.Salary = Prefer(row.Salary, stored.Salary);
                stored.Requirements = Prefer(row.Requirements, stored.Requirements);
                stored.Region = Prefer(row.Region, stored.Region);
            }
            var added = 0;
            foreach (var item in items.Where(ProfessionFilterSafe))
            {
                item.Url = CanonicalUrl(item.Url);
                item.Id = CanonicalUrl(item.Id);
                var key = Key(item);
                if (byKey.TryGetValue(key, out var old))
                {
                    // Refresh missing fields on previously stored rows after detail-page enrichment.
                    old.Id = Prefer(item.Id, old.Id);
                    old.Url = Prefer(item.Url, old.Url);
                    old.Company = Prefer(CleanCompany(item.Company), CleanCompany(old.Company));
                    old.Salary = Prefer(item.Salary, old.Salary);
                    old.Requirements = Prefer(item.Requirements, old.Requirements);
                    old.Title = Prefer(item.Title, old.Title);
                    old.Region = Prefer(item.Region, old.Region);
                    old.Profession = Prefer(item.Profession, old.Profession);
                    old.ParsedAt = item.ParsedAt;
                }
                else
                {
                    byKey[key] = item;
                    added++;
                }
            }

            // Rewrite with one consistent schema; also upgrades the previous 8-column CSV.
            using var writer = new StreamWriter(PathName, false, new UTF8Encoding(true));
            writer.WriteLine(string.Join(",", Header));
            foreach (var vacancy in byKey.Values.OrderByDescending(v => v.ParsedAt))
                writer.WriteLine(string.Join(",", new[]
                {
                    vacancy.Id, vacancy.Source, vacancy.Profession, vacancy.Title,
                    vacancy.Company, vacancy.Salary, vacancy.Requirements, vacancy.Url,
                    vacancy.ParsedAt.ToString("O", CultureInfo.InvariantCulture), vacancy.Region
                }.Select(Escape)));
            return added;
        }
    }

    private static bool ProfessionFilterSafe(Vacancy v) =>
        !string.IsNullOrWhiteSpace(v.Profession) &&
        new[] { "Python-разработчик", "Data Scientist", "Специалист по машинному обучению" }
            .Contains(v.Profession, StringComparer.OrdinalIgnoreCase);

    private static string Key(Vacancy v)
    {
        if (string.IsNullOrWhiteSpace(v.Url))
            return $"{v.Source}|{v.Title}|{v.Company}";
        // Search-session parameters such as Rabota.ru's search_id change on every
        // search. They must not create a second CSV row for the same vacancy.
        return CanonicalUrl(v.Url).TrimEnd('/');
    }

    private static string CanonicalUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return uri.GetLeftPart(UriPartial.Path);
        return value.Split('?', '#')[0];
    }

    private static string Prefer(string? incoming, string? current) =>
        !string.IsNullOrWhiteSpace(incoming) ? incoming : current ?? "";

    private static string CleanCompany(string? value) => CompanyNameNormalizer.Clean(value);

    private static string CleanRegion(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0) return "";
        var parts = text.Split(new[] { ',', ';', '|', '·' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(part,
                @"(?i)^(RU|RUS|Россия|Российская Федерация|Russian Federation|Московская область|Московская обл\.)$"))
                continue;
            if (System.Text.RegularExpressions.Regex.IsMatch(part, @"^(\{|\}|\[|\]|null|undefined)$"))
                continue;
            if (System.Text.RegularExpressions.Regex.IsMatch(part,
                @"(?i)^(область|край|республика)\b"))
                continue;
            return part.Trim('"', '\'', '«', '»', '{', '}', '[', ']');
        }
        return "";
    }

    private static string Escape(string? value) =>
        "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";

    private static Vacancy? Parse(List<string> values, List<string> headers)
    {
        if (values.Count != headers.Count) return null;
        var map = headers.Select((h, i) => (h, i))
            .ToDictionary(x => x.h, x => values[x.i], StringComparer.OrdinalIgnoreCase);
        DateTime.TryParse(Get("ParsedAt"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedAt);
        var title = Get("Title");
        var profession = Get("Profession");
        if (string.IsNullOrWhiteSpace(profession))
            profession = JobParser.Parsers.ProfessionFilter.Match(title) ?? "";
        if (string.IsNullOrWhiteSpace(profession)) return null;
        return new Vacancy
        {
            Id = Get("Id"), Source = Get("Source"), Profession = profession,
            Title = title, Company = CleanCompany(Get("Company")), Salary = Get("Salary"), Region = CleanRegion(Get("Region")),
            Requirements = Get("Requirements"), Url = Get("Url"),
            ParsedAt = parsedAt == default ? DateTime.UtcNow : parsedAt
        };

        string Get(string key) => map.TryGetValue(key, out var value) ? value : "";
    }

    private static List<string> Split(string line)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"'); i++;
                }
                else quoted = !quoted;
            }
            else if (c == ',' && !quoted)
            {
                values.Add(current.ToString()); current.Clear();
            }
            else current.Append(c);
        }
        values.Add(current.ToString());
        return values;
    }
}
