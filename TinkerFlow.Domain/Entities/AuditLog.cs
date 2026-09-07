namespace TinkerFlow.Domain.Entities;

public class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public Guid? UserId { get; set; }
    public string? UserEmail { get; set; }
    public string? UserName { get; set; }
    public string? UserRole { get; set; }

    public string Action { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;

    public Guid? EntityId { get; set; }
    public string? EntityName { get; set; }
    public string? Details { get; set; }
    public string? IpAddress { get; set; }
}
