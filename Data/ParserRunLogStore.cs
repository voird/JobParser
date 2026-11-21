using System.Text.Json;
using JobParser.Models;
namespace JobParser.Data;
public sealed class ParserRunLogStore(IWebHostEnvironment env) {
 private readonly object gate=new();
 private string FilePath=>Path.Combine(env.ContentRootPath,"Data","parser-runs.json");
 public void Add(ParserRunResult item) { lock(gate) { Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!); var all=GetAll().ToList(); all.Insert(0,item); File.WriteAllText(FilePath,JsonSerializer.Serialize(all.Take(500),new JsonSerializerOptions{WriteIndented=true})); } }
 public IReadOnlyList<ParserRunResult> GetAll() { lock(gate) { if(!File.Exists(FilePath)) return []; try{return JsonSerializer.Deserialize<List<ParserRunResult>>(File.ReadAllText(FilePath))??[];}catch{return [];} } }
}
