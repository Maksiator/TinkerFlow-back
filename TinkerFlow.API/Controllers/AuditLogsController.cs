using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TinkerFlow.API.DTOs;
using TinkerFlow.Infrastructure;

namespace TinkerFlow.API.Controllers;

[ApiController]
[Route("api/audit-logs")]
[Authorize(Roles = "Admin")]
public class AuditLogsController : ControllerBase
{
    private readonly TinkerFlowDbContext _context;

    public AuditLogsController(TinkerFlowDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<AuditLogListResponse>> GetLogs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30,
        [FromQuery] string? category = null,
        [FromQuery] string? actionName = null,
        [FromQuery] Guid? userId = null,
        [FromQuery] string? search = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 30;
        if (pageSize > 100) pageSize = 100;

        var query = _context.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(category) && category != "all")
        {
            query = query.Where(l => l.Category == category);
        }

        if (!string.IsNullOrWhiteSpace(actionName) && actionName != "all")
        {
            query = query.Where(l => l.Action == actionName);
        }

        if (userId.HasValue)
        {
            query = query.Where(l => l.UserId == userId.Value);
        }

        if (fromDate.HasValue)
        {
            var utcFrom = DateTime.SpecifyKind(fromDate.Value.Date, DateTimeKind.Utc);
            query = query.Where(l => l.Timestamp >= utcFrom);
        }

        if (toDate.HasValue)
        {
            var utcTo = DateTime.SpecifyKind(toDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
            query = query.Where(l => l.Timestamp <= utcTo);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.Trim().ToLower();
            query = query.Where(l =>
                (l.UserEmail != null && l.UserEmail.ToLower().Contains(searchLower)) ||
                (l.UserName != null && l.UserName.ToLower().Contains(searchLower)) ||
                (l.EntityName != null && l.EntityName.ToLower().Contains(searchLower)) ||
                (l.Details != null && l.Details.ToLower().Contains(searchLower)) ||
                l.Action.ToLower().Contains(searchLower)
            );
        }

        var totalCount = await query.CountAsync();
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        var items = await query
            .OrderByDescending(l => l.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new AuditLogResponse(
                l.Id,
                l.Timestamp,
                l.UserId,
                l.UserEmail,
                l.UserName,
                l.UserRole,
                l.Action,
                l.Category,
                l.EntityId,
                l.EntityName,
                l.Details,
                l.IpAddress
            ))
            .ToListAsync();

        return Ok(new AuditLogListResponse(items, totalCount, page, pageSize, totalPages));
    }

    [HttpGet("filters")]
    public async Task<ActionResult<AuditLogFiltersResponse>> GetFilters()
    {
        var categories = await _context.AuditLogs
            .Select(l => l.Category)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync();

        var actions = await _context.AuditLogs
            .Select(l => l.Action)
            .Distinct()
            .OrderBy(a => a)
            .ToListAsync();

        return Ok(new AuditLogFiltersResponse(categories, actions));
    }

    [HttpDelete("cleanup")]
    public async Task<IActionResult> CleanupOldLogs([FromQuery] int olderThanDays = 90)
    {
        if (olderThanDays < 14)
        {
            return BadRequest(new { message = "Minimalny okres retencji logów to 14 dni." });
        }

        var cutoffDate = DateTime.UtcNow.AddDays(-olderThanDays);
        var logsToDelete = await _context.AuditLogs
            .Where(l => l.Timestamp < cutoffDate)
            .ExecuteDeleteAsync();

        return Ok(new
        {
            deletedCount = logsToDelete,
            message = $"Pomyślnie usunięto {logsToDelete} archiwalnych wpisów starszych niż {olderThanDays} dni."
        });
    }
}
