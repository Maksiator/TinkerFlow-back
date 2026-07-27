namespace TinkerFlow.Domain.Entities;

public class GroupSubstitute
{
    public Guid Id { get; set; }
    
    public Guid GroupId { get; set; }
    public Group Group { get; set; } = null!;

    public Guid SubstituteTrainerId { get; set; }
    public User SubstituteTrainer { get; set; } = null!;

    public DateTime LessonDate { get; set; } // Kiedy faktycznie są zajęcia
    public DateTime ValidFrom { get; set; }  // Od kiedy widać matrycę
    public DateTime ValidUntil { get; set; } // Do kiedy widać matrycę
}