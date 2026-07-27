using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TinkerFlow.API.DTOs;
using TinkerFlow.Domain.Entities;
using TinkerFlow.Domain.Enums;
using TinkerFlow.Infrastructure;
using TinkerFlow.Infrastructure.Services;

namespace TinkerFlow.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PrintLogsController : ControllerBase
{
    private readonly TinkerFlowDbContext _context;
    private readonly IGroupAccessService _accessService;
    private readonly UserManager<User> _userManager;

    public PrintLogsController(
        TinkerFlowDbContext context, 
        IGroupAccessService accessService, 
        UserManager<User> userManager)
    {
        _context = context;
        _accessService = accessService;
        _userManager = userManager;
    }

    private async Task<bool> CanAccessStudentAsync(Student student)
    {
        var currentUserIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(currentUserIdStr, out var currentUserId)) return false;

        var user = await _userManager.FindByIdAsync(currentUserIdStr);
        if (user == null) return false;

        if (user.Role == UserRole.Admin) return true;

        if (student.GroupId.HasValue)
        {
            return await _accessService.CanAccessGroupAsync(currentUserId, student.GroupId.Value);
        }

        if (user.Role == UserRole.Coordinator) return true;

        return false;
    }

    // --- STARA METODA (Zostaje jak była, do głównego widoku kolejki) ---
    [HttpGet("group/{groupId}/queue")]
    public async Task<IActionResult> GetPrintQueue(Guid groupId)
    {
        var currentUserIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(currentUserIdStr, out var currentUserId)) return Unauthorized();
        if (!await _accessService.CanAccessGroupAsync(currentUserId, groupId)) return Forbid();

        var students = await _context.Students
            .Where(s => s.GroupId == groupId)
            .Include(s => s.PrintLogs)
            .ToListAsync();

        var queue = students.Select(s => new
        {
            StudentId = s.Id,
            FullName = $"{s.FirstName} {s.LastName}",
            TotalPrints = s.PrintLogs.Count,
            LastName = s.LastName,
            FirstName = s.FirstName,
            LastPrintDate = s.PrintLogs.OrderByDescending(pl => pl.PrintDate).FirstOrDefault()?.PrintDate
        })
        .OrderBy(x => x.LastPrintDate.HasValue ? 1 : 0)
        .ThenBy(x => x.LastPrintDate)
        .ThenBy(x => x.LastName)
        .ThenBy(x => x.FirstName)
        .Select(x => new
        {
            x.StudentId,
            x.FullName,
            x.TotalPrints,
            x.LastPrintDate
        })
        .ToList();

        return Ok(queue);
    }

    // --- NOWA METODA: POBIERANIE HISTORII WYDRUKÓW (Żeby mieć ID do edycji) ---
    [HttpGet("group/{groupId}/history")]
    public async Task<IActionResult> GetGroupPrintHistory(Guid groupId)
    {
        var currentUserIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(currentUserIdStr, out var currentUserId)) return Unauthorized();
        if (!await _accessService.CanAccessGroupAsync(currentUserId, groupId)) return Forbid();

        // Pobieramy powiedzmy 50 ostatnich wydruków dla danej grupy
        var history = await _context.PrintLogs
            .Include(pl => pl.Student)
            .Where(pl => pl.Student.GroupId == groupId)
            .OrderByDescending(pl => pl.PrintDate)
            .Take(50) 
            .Select(pl => new PrintLogHistoryResponse(
                pl.Id,
                pl.StudentId,
                $"{pl.Student.FirstName} {pl.Student.LastName}",
                pl.PrintDate
            ))
            .ToListAsync();

        return Ok(history);
    }

    [HttpPost]
    public async Task<IActionResult> AddPrintLog([FromBody] CreatePrintLogRequest request)
    {
        var student = await _context.Students.FindAsync(request.StudentId);
        if (student == null) return NotFound(new { message = "Nie znaleziono ucznia." });
        if (!await CanAccessStudentAsync(student)) return Forbid();

        var log = new PrintLog
        {
            Id = Guid.NewGuid(),
            StudentId = request.StudentId,
            PrintDate = request.PrintDate ?? DateTime.UtcNow
        };

        _context.PrintLogs.Add(log);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Wydruk zapisany pomyślnie." });
    }

    // --- NOWA METODA: EDYCJA WYDRUKU ---
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdatePrintLog(Guid id, [FromBody] UpdatePrintLogRequest request)
    {
        var log = await _context.PrintLogs.FindAsync(id);
        if (log == null)
            return NotFound(new { message = "Nie znaleziono takiego wydruku." });

        var currentStudent = await _context.Students.FindAsync(log.StudentId);
        if (currentStudent == null || !await CanAccessStudentAsync(currentStudent)) return Forbid();

        var targetStudent = await _context.Students.FindAsync(request.StudentId);
        if (targetStudent == null || !await CanAccessStudentAsync(targetStudent)) return Forbid();

        // Aktualizujemy dane (możemy zmienić datę LUB przenieść wydruk na inne dziecko)
        log.StudentId = request.StudentId;
        log.PrintDate = request.PrintDate;

        _context.Entry(log).State = EntityState.Modified;
        await _context.SaveChangesAsync();

        return Ok(new { message = "Wydruk zaktualizowany pomyślnie." });
    }

    // --- NOWA METODA: USUWANIE WYDRUKU ---
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeletePrintLog(Guid id)
    {
        var log = await _context.PrintLogs.FindAsync(id);
        if (log == null)
            return NotFound(new { message = "Nie znaleziono takiego wydruku." });

        var student = await _context.Students.FindAsync(log.StudentId);
        if (student == null || !await CanAccessStudentAsync(student)) return Forbid();

        _context.PrintLogs.Remove(log);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Wydruk został pomyślnie usunięty." });
    }
}