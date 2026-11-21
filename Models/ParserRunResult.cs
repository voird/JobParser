namespace JobParser.Models;
public sealed class ParserRunResult {
 public string ParserId{get;set;}=""; public string Site{get;set;}=""; public string Method{get;set;}="";
 public int Found{get;set;} public int Added{get;set;} public long ElapsedMs{get;set;}
 public string Status{get;set;}="OK"; public string? Error{get;set;} public DateTime StartedAt{get;set;}
}
