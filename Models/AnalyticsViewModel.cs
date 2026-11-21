namespace JobParser.Models;

public sealed class AnalyticsViewModel
{
    public int Total { get; set; }
    public int WithCompany { get; set; }
    public int WithSalary { get; set; }
    public string[] RegionLabels { get; set; } = [];
    public int[] RegionCounts { get; set; } = [];
    public string[] ProfessionLabels { get; set; } = [];
    public int[] ProfessionCounts { get; set; } = [];
    public string[] CompanyLabels { get; set; } = [];
    public int[] CompanyCounts { get; set; } = [];
    public string[] SalaryLabels { get; set; } = [];
    public int[] SalaryCounts { get; set; } = [];
}
