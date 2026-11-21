using System.Text.RegularExpressions;
using JobParser.Models;

namespace JobParser.Parsers;

public static class ProfessionFilter
{
    private static readonly (string Name, string[] Terms)[] Professions =
    {
        ("Python-разработчик", new[] { "python developer", "python-разработчик", "python разработчик", "разработчик python", "программист python", "python engineer", "backend python", "бэкенд python" }),
        ("Data Scientist", new[] { "data scientist", "дата-сайентист", "дата сайентист", "ученый по данным" }),
        ("Специалист по машинному обучению", new[] { "machine learning engineer", "ml engineer", "машинное обучение", "специалист по машинному обучению", "инженер машинного обучения", "разработчик ml", "ml-разработчик" })
    };

    public static string? Match(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        var normalized = Regex.Replace(title.ToLowerInvariant().Replace('ё', 'е'), @"[‐‑‒–—]", "-");
        foreach (var (name, terms) in Professions)
            if (terms.Any(term => normalized.Contains(term, StringComparison.OrdinalIgnoreCase)))
                return name;
        return null;
    }

    public static bool IsAllowed(Vacancy vacancy)
    {
        var profession = Match(vacancy.Title);
        if (profession is null) return false;
        vacancy.Profession = profession;
        return true;
    }

    public static IReadOnlyList<string> SearchTerms => new[] { "Python", "Data Scientist", "Machine Learning" };
}
