using Microsoft.Extensions.Logging;
using TinkerFlow.Domain.Entities;

namespace TinkerFlow.Infrastructure.Services;

public class AuditLogService : IAuditLogService
{
    private readonly TinkerFlowDbContext _context;
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(TinkerFlowDbContext context, ILogger<AuditLogService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task LogAsync(
        string category,
        string action,
        string? details = null,
        Guid? entityId = null,
        string? entityName = null,
        Guid? userId = null,
        string? userEmail = null,
        string? userName = null,
        string? userRole = null,
        string? ipAddress = null)
    {
        try
        {
            var log = new AuditLog
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTime.UtcNow,
                Category = category,
                Action = action,
                Details = details,
                EntityId = entityId,
                EntityName = entityName,
                UserId = userId,
                UserEmail = userEmail,
                UserName = userName,
                UserRole = userRole,
                IpAddress = ipAddress
            };

            _context.AuditLogs.Add(log);
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Błąd zapisu audytu nie może przerwać głównej operacji użytkownika
            _logger.LogError(ex, "Błąd podczas zapisu AuditLog dla akcji {Action} w kategorii {Category}", action, category);
        }
    }
}
