using System.Diagnostics;
using JobParser.Data;
using JobParser.Models;
using JobParser.Parsers;

namespace JobParser.Services;

public sealed class ParsingCoordinator(ParserRegistry registry, CsvVacancyStore store, ParserRunLogStore logs)
{
    public async Task<ParserRunResult> RunAsync(string id, CancellationToken ct = default)
    {
        var parser = registry.Find(id) ?? throw new ArgumentException("Парсер не найден.");
        var sw = Stopwatch.StartNew();
        var started = DateTime.Now;
        try
        {
            var parsed = await parser.ParseAsync(ct);
            var valid = parsed.Where(ProfessionFilter.IsAllowed).ToList();
            var added = store.AddRange(valid);
            sw.Stop();
            var result = new ParserRunResult { ParserId = parser.Id, Site = parser.SiteName, Method = parser.Method,
                Found = valid.Count, Added = added, ElapsedMs = sw.ElapsedMilliseconds,
                Status = "Успешно", StartedAt = started };
            logs.Add(result);
            return result;
        }
        catch (Exception e)
        {
            sw.Stop();
            var result = new ParserRunResult { ParserId = parser.Id, Site = parser.SiteName, Method = parser.Method, ElapsedMs = sw.ElapsedMilliseconds, Status = "Ошибка", Error = e.Message, StartedAt = started }; logs.Add(result); return result;
        }
    }

    public async Task<IReadOnlyList<ParserRunResult>> RunAllAsync(CancellationToken ct = default)
    {
        var results = new List<ParserRunResult>();
        foreach (var parser in registry.All)
            results.Add(await RunAsync(parser.Id, ct));
        return results;
    }
}
