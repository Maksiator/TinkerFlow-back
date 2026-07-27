namespace TinkerFlow.Domain.Entities;

public class TrainerGroupList
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    
    public DayOfWeek? TargetDay { get; set; }
    
    public Guid TrainerId { get; set; } 
    public User Trainer { get; set; } = null!; // Informuje EF, że lista musi mieć właściciela
    
    public ICollection<TrainerGroupListItem> ListItems { get; set; } = new List<TrainerGroupListItem>();
    
}