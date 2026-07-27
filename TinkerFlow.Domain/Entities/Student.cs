using TinkerFlow.Domain.Enums;

namespace TinkerFlow.Domain.Entities;

public class Student
{
    public Guid Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public DateOnly DateOfBirth { get; set; }
    
    public SkillLevel Level { get; set; }
    public bool IsIndependent { get; set; }
    public bool NeedsAttention { get; set; }
    
    public Guid? GroupId { get; set; }
    public Group? Group { get; set; }
    
    public ICollection<PrintLog> PrintLogs { get; set; } = new List<PrintLog>();
}