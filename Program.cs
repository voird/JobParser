using JobParser.Data; using JobParser.Parsers; using JobParser.Services;
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient("scraper", c => { c.Timeout=TimeSpan.FromSeconds(25); c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120 Safari/537.36"); });
builder.Services.AddSingleton<CsvVacancyStore>(); builder.Services.AddSingleton<ParserRunLogStore>(); builder.Services.AddSingleton<ParserRegistry>(); builder.Services.AddSingleton<ParsingCoordinator>();
var app=builder.Build(); if(!app.Environment.IsDevelopment()){app.UseExceptionHandler("/Home/Error");app.UseHsts();}
app.UseHttpsRedirection(); app.UseStaticFiles(); app.UseRouting(); app.MapControllerRoute(name:"default",pattern:"{controller=Home}/{action=Index}/{id?}"); app.Run();