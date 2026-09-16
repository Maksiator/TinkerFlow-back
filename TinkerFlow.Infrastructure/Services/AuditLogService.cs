using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TinkerFlow.Domain.Entities;

namespace TinkerFlow.Infrastructure.Services;

public class AuditLogService : IAuditLogService
{
    private readonly TinkerFlowDbContext _context;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(
        TinkerFlowDbContext context,
        IHttpContextAccessor httpContextAccessor,
        ILogger<AuditLogService> logger)
    {
        _context = context;
        _httpContextAccessor = httpContextAccessor;
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
            var httpContext = _httpContextAccessor.HttpContext;

            // 1. Jeśli IP nie podano, pobierz z bieżącego żądania HTTP
            if (string.IsNullOrWhiteSpace(ipAddress) && httpContext != null)
            {
                if (httpContext.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor) && !string.IsNullOrWhiteSpace(forwardedFor))
                {
                    ipAddress = forwardedFor.ToString().Split(',')[0].Trim();
                }
                else
                {
                    ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
                }
            }

            // 2. Jeśli userId nie podano, spróbuj wyciągnąć z tokena JWT zalogowanego użytkownika
            if (!userId.HasValue && httpContext?.User != null)
            {
                var idClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                              ?? httpContext.User.FindFirst("sub")?.Value;
                if (Guid.TryParse(idClaim, out var parsedId))
                {
                    userId = parsedId;
                }
            }

            // 3. Jeśli mamy userId, a brakuje userName, userEmail lub userRole - dociągnij z bazy
            if (userId.HasValue && (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(userEmail) || string.IsNullOrWhiteSpace(userRole)))
            {
                var user = await _context.Users
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == userId.Value);

                if (user != null)
                {
                    userEmail ??= user.Email;
                    userName ??= $"{user.FirstName} {user.LastName}".Trim();
                    userRole ??= user.Role.ToString();
                }
            }

            // 4. Jeśli nadal nie mamy roli, sprawdź w claims
            if (string.IsNullOrWhiteSpace(userRole) && httpContext?.User != null)
            {
                userRole = httpContext.User.FindFirst(ClaimTypes.Role)?.Value;
            }

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
