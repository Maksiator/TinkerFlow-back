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
public class StudentProjectsController : ControllerBase
{
    private readonly TinkerFlowDbContext _context;
    private readonly IGroupAccessService _accessService;
    private readonly UserManager<User> _userManager;

    public StudentProjectsController(
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

        // Jeśli uczeń nie ma grupy (duch), koordynatorzy mają do niego dostęp (zgodnie z logiką StudentsController)
        if (user.Role == UserRole.Coordinator) return true;

        return false;
    }

    [HttpGet("student/{studentId}")]
    public async Task<IActionResult> GetStudentMatrix(Guid studentId)
    {
        var student = await _context.Students.FindAsync(studentId);
        if (student == null) return NotFound(new { message = "Nie znaleziono ucznia." });
        if (!await CanAccessStudentAsync(student)) return Forbid();

        var matrix = await _context.StudentProjects
            .Where(sp => sp.StudentId == studentId)
            .Select(sp => new
            {
                sp.ProjectId,
                ProjectName = sp.Project != null ? sp.Project.Name : "Nieznany projekt",
                sp.Status
            })
            .ToListAsync();
        
        return Ok(matrix);
    }

    [HttpPost("upsert")]
    public async Task<IActionResult> UpsertProjectStatus([FromBody] UpsertProjectStatusRequest request)
    {
        var student = await _context.Students.FindAsync(request.StudentId);
        if (student == null) return NotFound(new { message = "Nie znaleziono ucznia." });
        if (!await CanAccessStudentAsync(student)) return Forbid();

        var existingRecord = await _context.StudentProjects
            .FirstOrDefaultAsync(sp => sp.StudentId == request.StudentId && sp.ProjectId == request.ProjectId);
        
        // Jezeli status projektu to "brak" usuwamy z bazy
        if (!request.Status.HasValue)
        {
            if (existingRecord != null)
            {
                _context.StudentProjects.Remove(existingRecord);
                await _context.SaveChangesAsync();
            }
            return Ok(new { message = "Usunięto przypisanie (zmieniono na brak)" });
        }
        
        var newStatus = request.Status.Value; 
        
        // Rekord istnieje a my zmieniamy status na inny
        if (existingRecord != null)
        {
            existingRecord.Status = newStatus;
            await _context.SaveChangesAsync();
            return Ok(new { message = "Zaktualizowano istniejacy status projektu." });
        }
        
        // rekord nie istnieje patrz jest brak a my ustalamy pierwszy raz status
        var newRecord = new StudentProject
        {
            Id = Guid.NewGuid(),
            StudentId = request.StudentId,
            ProjectId = request.ProjectId,
            Status = request.Status.Value
        };
        _context.StudentProjects.Add(newRecord);
        await _context.SaveChangesAsync();
        
        return Ok(new { message = "Utworzono nowe przypisanie projektu" });
    }

    [HttpPost("bulk-upsert")]
    public async Task<IActionResult> BulkUpsert([FromBody] List<UpsertProjectStatusRequest> requests)
    {
        if (!requests.Any())
        {
            return BadRequest(new { message = "Lista operacji jest pusta." });
        }
        
        var studentIds = requests.Select(r => r.StudentId).Distinct().ToList();
        
        foreach (var studentId in studentIds)
        {
            var student = await _context.Students.FindAsync(studentId);
            if (student == null) return NotFound(new { message = $"Nie znaleziono ucznia o ID {studentId}." });
            if (!await CanAccessStudentAsync(student)) return Forbid();
        }

        var projectIds = requests.Select(r => r.ProjectId).Distinct().ToList();
        
        var existingRecords = await _context.StudentProjects
            .Where(sp => studentIds.Contains(sp.StudentId) && projectIds.Contains(sp.ProjectId))
            .ToListAsync();

        foreach (var req in requests)
        {
            var existing = existingRecords.FirstOrDefault(sp => sp.StudentId == req.StudentId && sp.ProjectId == req.ProjectId);

            if (!req.Status.HasValue)
            {
                if (existing != null)
                {
                    _context.StudentProjects.Remove(existing);
                    existingRecords.Remove(existing);
                }
            }
            else
            {
                var newStatus = req.Status.Value;
                if (existing != null)
                {
                    existing.Status = newStatus;
                }
                else
                {
                    var newRecord = new StudentProject
                    {
                        Id = Guid.NewGuid(),
                        StudentId = req.StudentId,
                        ProjectId = req.ProjectId,
                        Status = newStatus
                    };
                    _context.StudentProjects.Add(newRecord);
                    existingRecords.Add(newRecord);
                }
            }
        }

        await _context.SaveChangesAsync();
        return Ok(new { message = $"Przetworzono {requests.Count} operacji" });
    }
    
    // POBIERZ WYDRUKI DLA GRUPY
    [HttpGet("group/{groupId}/ready-to-print")]
    public async Task<IActionResult> GetReadyToPrintForGroup(Guid groupId)
    {
        var currentUserIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(currentUserIdStr, out var currentUserId)) return Unauthorized();

        var hasAccess = await _accessService.CanAccessGroupAsync(currentUserId, groupId);
        if (!hasAccess) return Forbid();

        // Bezpieczne zapytanie chroniące przed sierotami (NullReferenceException)
        var pendingPrints = await _context.StudentProjects
            .Include(sp => sp.Student)
            .Include(sp => sp.Project)
            .Where(sp => sp.Student != null && sp.Student.GroupId == groupId && sp.Status == ProjectState.ReadytoPrint)
            .Select(sp => new 
            {
                sp.Id,
                StudentName = sp.Student != null ? sp.Student.FirstName + " " + sp.Student.LastName : "Nieznany uczeń",
                ProjectName = sp.Project != null ? sp.Project.Name : "Nieznany projekt"
            })
            .ToListAsync();

        return Ok(pendingPrints);
    }

    // ZMIEŃ MASOWO NA ZREALIZOWANE
    [HttpPatch("bulk-complete")]
    public async Task<IActionResult> MarkAsCompleted([FromBody] List<Guid> studentProjectIds)
    {
        if (!studentProjectIds.Any())
        {
            return BadRequest(new { message = "Brak zaznaczonych projektów do aktualizacji." });
        }

        // Zoptymalizowane pobranie i aktualizacja
        var recordsToUpdate = await _context.StudentProjects
            .Where(sp => studentProjectIds.Contains(sp.Id))
            .ToListAsync();

        foreach (var record in recordsToUpdate)
        {
            var student = await _context.Students.FindAsync(record.StudentId);
            if (student == null) return NotFound(new { message = $"Nie znaleziono ucznia dla rekordu {record.Id}." });
            if (!await CanAccessStudentAsync(student)) return Forbid();

            record.Status = ProjectState.Completed; // Zmieniamy z 3 na 4
        }

        await _context.SaveChangesAsync();
        return Ok(new { message = $"Zaktualizowano {recordsToUpdate.Count} wydruków na status Zrealizowane." });
    }
}