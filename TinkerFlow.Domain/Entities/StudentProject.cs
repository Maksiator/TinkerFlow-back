using System.ComponentModel.DataAnnotations.Schema;
using TinkerFlow.Domain.Enums;

namespace TinkerFlow.Domain.Entities;

[Table("StudentProject")]
public class StudentProject
{
    public Guid Id { get; set; }
    
    public Guid StudentId { get; set; }
    public Guid ProjectId { get; set; }
    
    public ProjectState Status { get; set; }
    
    public Student Student { get; set; } = null!;
    public Project Project { get; set; } = null!;
}