namespace TinkerFlow.Infrastructure.Services;

public interface IAuditLogService
{
    Task LogAsync(
        string category,
        string action,
        string? details = null,
        Guid? entityId = null,
        string? entityName = null,
        Guid? userId = null,
        string? userEmail = null,
        string? userName = null,
        string? userRole = null,
        string? ipAddress = null);
}
