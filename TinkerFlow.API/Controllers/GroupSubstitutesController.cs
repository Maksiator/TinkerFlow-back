using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims; // DODANE: Wymagane do pobrania ID zalogowanego usera z tokena
using TinkerFlow.API.DTOs;
using TinkerFlow.Domain.Entities;
using TinkerFlow.Infrastructure;
using TinkerFlow.Infrastructure.Services;

namespace TinkerFlow.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize] // ZMIANA 1: Otwieramy kontroler dla każdego zalogowanego (w tym Trenerów)
public class GroupSubstitutesController : ControllerBase
{
    private readonly TinkerFlowDbContext _context;
    private readonly IGroupAccessService _accessService;

    public GroupSubstitutesController(TinkerFlowDbContext context, IGroupAccessService accessService)
    {
        _context = context;
        _accessService = accessService;
    }

    [HttpGet]
    [Authorize(Roles = "Admin,Coordinator")] // ZMIANA 2: Blokujemy ten endpoint tylko dla "szefostwa"
    public async Task<IActionResult> GetSubstitutes()
    {
        var currentUserIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(currentUserIdStr, out var currentUserId)) return Unauthorized();

        var query = _context.GroupSubstitutes.AsQueryable();

        if (User.IsInRole("Coordinator"))
        {
            var coordinatorBranchIds = await _context.UserBranches
                .Where(ub => ub.UserId == currentUserId)
                .Select(ub => ub.BranchId)
                .ToListAsync();

            query = query.Where(s => coordinatorBranchIds.Contains(s.Group.BranchId));
        }

        var subs = await query
            .OrderByDescending(s => s.LessonDate)
            .Select(s => new SubstituteResponse(
                s.Id,
                s.GroupId,
                s.Group.Name,
                s.SubstituteTrainerId,
                s.SubstituteTrainer.FirstName + " " + s.SubstituteTrainer.LastName,
                s.LessonDate,
                s.ValidFrom,
                s.ValidUntil
            ))
            .ToListAsync();

        return Ok(subs);
    }

    // ZMIANA 3: NOWY ENDPOINT DLA TRENERA (bez blokady ról - wpuszcza każdego zalogowanego)
    [HttpGet("my")]
    public async Task<IActionResult> GetMySubstitutes()
    {
        // Wyciągamy ID użytkownika z tokena JWT
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out Guid userId))
            return Unauthorized();

        var today = DateTime.UtcNow.Date;

        var subs = await _context.GroupSubstitutes
            .Where(s => s.SubstituteTrainerId == userId && s.ValidUntil >= today) // Tylko dla tego trenera i nieprzedawnione
            .OrderBy(s => s.LessonDate)
            .Select(s => new SubstituteResponse(
                s.Id,
                s.GroupId,
                s.Group.Name,
                s.SubstituteTrainerId,
                s.SubstituteTrainer.FirstName + " " + s.SubstituteTrainer.LastName,
                s.LessonDate,
                s.ValidFrom,
                s.ValidUntil
            ))
            .ToListAsync();

        return Ok(subs);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Coordinator")]
    public async Task<IActionResult> CreateSubstitute([FromBody] CreateSubstituteRequest request)
    {
        var currentUserIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(currentUserIdStr, out var currentUserId)) return Unauthorized();

        if (!User.IsInRole("Admin"))
        {
            var hasAccess = await _accessService.CanAccessGroupAsync(currentUserId, request.GroupId);
            if (!hasAccess) return Forbid();
        }

        var group = await _context.Groups.FindAsync(request.GroupId);
        if (group == null)
        {
            return NotFound(new { message = "Nie znaleziono wybranej grupy." });
        }

        if (group.PrimaryTrainerId == request.TrainerId)
        {
            return BadRequest(new { message = "Nie możesz przypisać zastępstwa głównemu prowadzącemu tej grupy." });
        }

        var settings = await _context.SystemSettings.FirstOrDefaultAsync() 
                       ?? new SystemSetting { SubstituteDaysBefore = 2, SubstituteDaysAfter = 2 };

        // LOGIKA: Jeśli frontend przysłał customowe daty (bo koordynator je nadpisał), to je szanujemy. 
        // Jeśli nie (przyszły nulle), to wyliczamy z domyślnych ustawień Admina.
        // Npgsql wymaga, aby zapisywane daty do kolumn 'timestamp with time zone' miały Kind = DateTimeKind.Utc
        var lessonDateUtc = DateTime.SpecifyKind(request.LessonDate, DateTimeKind.Utc);
        
        var validFromUtc = request.ValidFrom.HasValue 
            ? DateTime.SpecifyKind(request.ValidFrom.Value, DateTimeKind.Utc)
            : DateTime.SpecifyKind(request.LessonDate.Date.AddDays(-settings.SubstituteDaysBefore), DateTimeKind.Utc);
            
        var validUntilUtc = request.ValidUntil.HasValue 
            ? DateTime.SpecifyKind(request.ValidUntil.Value, DateTimeKind.Utc)
            : DateTime.SpecifyKind(request.LessonDate.Date.AddDays(settings.SubstituteDaysAfter).AddHours(23).AddMinutes(59), DateTimeKind.Utc);

        var substitute = new GroupSubstitute
        {
            Id = Guid.NewGuid(),
            GroupId = request.GroupId,
            SubstituteTrainerId = request.TrainerId,
            LessonDate = lessonDateUtc,
            ValidFrom = validFromUtc,
            ValidUntil = validUntilUtc
        };

        _context.GroupSubstitutes.Add(substitute);
        await _context.SaveChangesAsync();

        var response = await _context.GroupSubstitutes
            .Where(s => s.Id == substitute.Id)
            .Select(s => new SubstituteResponse(
                s.Id, s.GroupId, s.Group.Name, s.SubstituteTrainerId,
                s.SubstituteTrainer.FirstName + " " + s.SubstituteTrainer.LastName,
                s.LessonDate, s.ValidFrom, s.ValidUntil))
            .FirstAsync();

        return Ok(response);
    }
    

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin,Coordinator")] // ZMIANA 5: Blokada dla "szefostwa"
    public async Task<IActionResult> DeleteSubstitute(Guid id)
    {
        var sub = await _context.GroupSubstitutes.FindAsync(id);
        if (sub == null) return NotFound();

        var currentUserIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(currentUserIdStr, out var currentUserId)) return Unauthorized();

        if (!User.IsInRole("Admin"))
        {
            var hasAccess = await _accessService.CanAccessGroupAsync(currentUserId, sub.GroupId);
            if (!hasAccess) return Forbid();
        }

        _context.GroupSubstitutes.Remove(sub);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}