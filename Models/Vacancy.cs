namespace JobParser.Models;

public sealed class Vacancy
{
    public string Id { get; set; } = "";
    public string Source { get; set; } = "";
    public string Profession { get; set; } = "";
    public string Title { get; set; } = "";
    public string Company { get; set; } = "";
    public string Region { get; set; } = "";
    public string Salary { get; set; } = "";
    public string Requirements { get; set; } = "";
    public string Url { get; set; } = "";
    public DateTime ParsedAt { get; set; } = DateTime.UtcNow;
}
