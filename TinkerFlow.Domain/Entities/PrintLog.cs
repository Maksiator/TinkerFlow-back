namespace TinkerFlow.Domain.Entities;

public class PrintLog
{
    public Guid Id { get; set; }
    public Guid StudentId { get; set; }
    public DateTime PrintDate { get; set; }
    
    public Student Student { get; set; } = null!;
}