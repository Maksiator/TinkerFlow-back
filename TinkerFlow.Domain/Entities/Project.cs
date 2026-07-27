namespace TinkerFlow.Domain.Entities;

public class Project
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    
    public int SequenceOrder { get; set; }
    
    public bool IsPractice { get; set; }
    public bool IsYearBoundary { get; set; }
}