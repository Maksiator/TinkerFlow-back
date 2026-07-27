namespace TinkerFlow.Domain.Entities;
    
public class StudentGroupHistory
{
    public Guid Id { get; set; }
        
    public Guid StudentId { get; set; }
    public Student Student { get; set; } = null!;
        
    public Guid GroupId { get; set; }
    public Group Group { get; set; } = null!;
        
    public string AcademicYear { get; set; } = string.Empty; // np. "2025/2026"
        
    public DateTime ArchivedAt { get; set; } = DateTime.UtcNow;
    
    public bool IsMidYear { get; set; } = false;
}