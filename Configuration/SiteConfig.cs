namespace JobParser.Configuration;

public sealed record SiteConfig(
    string Key, string Name, string BaseUrl, string SearchUrl,
    string QueryParameter, string VacancyPath)
{
    public static IReadOnlyDictionary<string, SiteConfig> All { get; } =
        new Dictionary<string, SiteConfig>(StringComparer.OrdinalIgnoreCase)
        {
            ["HabrCareer"] = new("HabrCareer", "Habr Career",
                "https://career.habr.com", "https://career.habr.com/vacancies",
                "query", "/vacancies/"),
            ["Rabota"] = new("Rabota", "Rabota.ru",
                "https://www.rabota.ru", "https://www.rabota.ru/vacancy/",
                "query", "/vacancy/"),
            ["SuperJob"] = new("SuperJob", "SuperJob",
                "https://www.superjob.ru", "https://www.superjob.ru/vacancy/search/",
                "keywords", "/vakansii/")
        };
}
