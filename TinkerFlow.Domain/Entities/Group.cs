namespace TinkerFlow.Domain.Entities;

public class Group
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    
    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    
    public Guid? PrimaryTrainerId { get; set; }
    public User? PrimaryTrainer { get; set; }
    
    public Guid? AssignedPrinterId { get; set; }
    public User? AssignedPrinter { get; set; }
    
    
    public ICollection<Student> Students { get; set; } = new List<Student>();
    public ICollection<GroupSubstitute> Substitutes { get; set; } = new List<GroupSubstitute>(); // Zastępstwa dla tej grupy
    
    // ustawienie dnia tygodnia odbywania się zajęć
    public DayOfWeek? ClassDayOfWeek { get; set; }
    
    // Archiwizacja grupy
    public bool IsArchived { get; set; } = false;
    public string? ArchivedAcademicYear { get; set; }
}