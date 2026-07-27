namespace TinkerFlow.Domain.Entities;

public class TrainerGroupListItem
{
    public Guid TrainerGroupListId { get; set; }
    public TrainerGroupList TrainerGroupList { get; set; } = null!;

    public Guid GroupId { get; set; }
    public Group Group { get; set; } = null!;

    public int OrderIndex { get; set; } 
}