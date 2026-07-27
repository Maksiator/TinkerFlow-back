namespace TinkerFlow.Domain.Entities;

public class Branch
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty; // np. "Kraków - Centrum"

    public ICollection<Group> Groups { get; set; } = new List<Group>();
    public ICollection<UserBranch> UserBranches { get; set; } = new List<UserBranch>();
}