namespace TinkerFlow.Domain.Entities;

public class ClassSession
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid TrainerId { get; set; }
    public DateTime Date { get; set; }
}