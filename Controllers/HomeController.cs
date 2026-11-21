using Microsoft.AspNetCore.Mvc; using System.Text.RegularExpressions; using JobParser.Models; using JobParser.Data; using JobParser.Services; namespace JobParser.Controllers;
public sealed class HomeController(ParserRegistry registry,CsvVacancyStore store,ParsingCoordinator coordinator,ParserRunLogStore logs):Controller {
 public IActionResult Index(string? q=null,string? source=null){var all=store.GetAll();var filtered=all;if(!string.IsNullOrWhiteSpace(q))filtered=filtered.Where(v=>(v.Title+" "+v.Company).Contains(q,StringComparison.OrdinalIgnoreCase)).ToList();if(!string.IsNullOrWhiteSpace(source))filtered=filtered.Where(v=>v.Source.Equals(source,StringComparison.OrdinalIgnoreCase)).ToList();ViewBag.AllVacancies=all;ViewBag.Parsers=registry.All;ViewBag.ParserCities=all
  .Where(v=>!string.IsNullOrWhiteSpace(v.Region))
  .GroupBy(v=>v.Source,StringComparer.OrdinalIgnoreCase)
  .ToDictionary(g=>g.Key,g=>g.Select(v=>NormalizeCity(v.Region)).Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x=>x).ToList(),StringComparer.OrdinalIgnoreCase);ViewBag.Query=q??"";ViewBag.Source=source??"";return View(filtered);}
 [HttpPost][ValidateAntiForgeryToken] public async Task<IActionResult> Run(string parserId){var r=await coordinator.RunAsync(parserId,HttpContext.RequestAborted);TempData["Notice"]=$"{r.Site}/{r.Method}: {r.Status}; найдено {r.Found}, добавлено {r.Added}, {r.ElapsedMs} мс. {r.Error}";return RedirectToAction(nameof(Index));}
 [HttpPost][ValidateAntiForgeryToken] public async Task<IActionResult> RunAll(){var r=await coordinator.RunAllAsync(HttpContext.RequestAborted);TempData["Notice"]=string.Join(" | ",r.Select(x=>$"{x.Site}/{x.Method}: {x.Status}, найдено {x.Found}, добавлено {x.Added}, {x.ElapsedMs} мс"));return RedirectToAction(nameof(Index));}
 public IActionResult Analytics(){
  var all=store.GetAll();
  var professions=all.GroupBy(v=>string.IsNullOrWhiteSpace(v.Profession)?"Не определена":v.Profession).OrderByDescending(g=>g.Count()).ToList();
  var companies=all.Where(v=>!string.IsNullOrWhiteSpace(v.Company)).GroupBy(v=>v.Company.Trim()).OrderByDescending(g=>g.Count()).Take(10).ToList();
  var regions=all.Where(v=>!string.IsNullOrWhiteSpace(v.Region)).GroupBy(v=>v.Region.Trim()).OrderByDescending(g=>g.Count()).Take(20).ToList();
  var buckets=new[]{"до 80 тыс. ₽","80–150 тыс. ₽","150–250 тыс. ₽","250–400 тыс. ₽","от 400 тыс. ₽"};
  var salaryCounts=new int[5];
  foreach(var v in all){var salary=v.Salary??"";var m=Regex.Match(salary, @"\d[\d\s\u00a0]*(?:[.,]\d+)?");if(!m.Success)continue;
   var digits=Regex.Replace(m.Value,@"[^\d]","");if(!long.TryParse(digits,out var amount))continue;
   // Convert common monthly salaries in rubles; non-ruble currencies are intentionally excluded.
   if(!(salary.Contains("₽")||salary.Contains("руб",StringComparison.OrdinalIgnoreCase)||salary.Contains("RUB",StringComparison.OrdinalIgnoreCase)))continue;
   var i=amount<80000?0:amount<150000?1:amount<250000?2:amount<400000?3:4;salaryCounts[i]++;
  }
  return View(new AnalyticsViewModel{Total=all.Count,WithCompany=all.Count(v=>!string.IsNullOrWhiteSpace(v.Company)),WithSalary=all.Count(v=>!string.IsNullOrWhiteSpace(v.Salary)),ProfessionLabels=professions.Select(g=>g.Key).ToArray(),ProfessionCounts=professions.Select(g=>g.Count()).ToArray(),CompanyLabels=companies.Select(g=>g.Key).ToArray(),CompanyCounts=companies.Select(g=>g.Count()).ToArray(),SalaryLabels=buckets,SalaryCounts=salaryCounts,RegionLabels=regions.Select(g=>g.Key).ToArray(),RegionCounts=regions.Select(g=>g.Count()).ToArray()});
 }
 public IActionResult Logs()=>View(logs.GetAll());
 public IActionResult Export(){var p=Path.Combine(Directory.GetCurrentDirectory(),"Data","vacancies.csv");return System.IO.File.Exists(p)?PhysicalFile(p,"text/csv; charset=utf-8","vacancies.csv"):NotFound("Сначала запустите парсер.");}
 private static string NormalizeCity(string? region){
  var value=(region??"").Trim();if(value.Length==0)return "";
  var ignored=new[]{"RU","RUS","Россия","Российская Федерация","Russian Federation","Московская область","Московская обл."};
  var parts=value.Split(new[]{',',';','|','·'},StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
  foreach(var part in parts){
   if(ignored.Contains(part,StringComparer.OrdinalIgnoreCase))continue;
   if(part=="{"||part=="}"||part=="["||part=="]"||part.Equals("null",StringComparison.OrdinalIgnoreCase)||part.Equals("undefined",StringComparison.OrdinalIgnoreCase))continue;
   if(part.StartsWith("область",StringComparison.OrdinalIgnoreCase)||part.StartsWith("край",StringComparison.OrdinalIgnoreCase)||part.StartsWith("республика",StringComparison.OrdinalIgnoreCase))continue;
   return part.Trim(' ','"','\'','«','»','{','}','[',']');
  }
  return "";
 }
}