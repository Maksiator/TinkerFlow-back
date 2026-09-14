using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TinkerFlow.API.DTOs;
using TinkerFlow.API.Extensions;
using TinkerFlow.Domain.Entities;
using TinkerFlow.Domain.Enums;
using TinkerFlow.Infrastructure;
using TinkerFlow.Infrastructure.Services;

namespace TinkerFlow.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Coordinator,Trainer")]
public class ImportsController : ControllerBase
{
    private readonly TinkerFlowDbContext _context;
    private readonly IGroupAccessService _accessService;
    private readonly IAuditLogService _auditLogService;

    public ImportsController(TinkerFlowDbContext context, IGroupAccessService accessService, IAuditLogService auditLogService)
    {
        _context = context;
        _accessService = accessService;
        _auditLogService = auditLogService;
    }

    [HttpPost("match-students")]
    public async Task<IActionResult> MatchStudents([FromBody] MatchStudentsRequest request)
    {
        if (request.StudentNames == null || !request.StudentNames.Any())
            return Ok(new List<MatchedStudentDto>());

        string Normalize(string val) => val.ToLower().Replace(" ", "").Replace("-", "");

        var allStudents = await _context.Students
            .AsNoTracking()
            .Include(s => s.Group)
                .ThenInclude(g => g!.Branch)
            .Include(s => s.Branch)
            .ToListAsync();

        var result = new List<MatchedStudentDto>();

        foreach (var fullName in request.StudentNames)
        {
            var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var firstName = parts.Length > 1 ? parts.Last() : "Nieznane";
            var lastName = parts.Length > 1 ? string.Join(" ", parts.Take(parts.Length - 1)) : (parts.Length > 0 ? parts[0] : "Nieznane");

            var targetFirst = Normalize(firstName);
            var targetLast = Normalize(lastName);

            var matched = allStudents.FirstOrDefault(s =>
                Normalize(s.FirstName) == targetFirst &&
                Normalize(s.LastName) == targetLast);

            result.Add(new MatchedStudentDto(
                NameInExcel: fullName,
                StudentId: matched?.Id,
                StudentName: matched != null ? $"{matched.FirstName} {matched.LastName}" : null,
                GroupId: matched?.GroupId,
                GroupName: matched?.Group?.Name,
                BranchId: matched?.Group?.BranchId ?? matched?.BranchId,
                BranchName: matched?.Group?.Branch?.Name ?? matched?.Branch?.Name,
                IsMatched: matched != null
            ));
        }

        return Ok(result);
    }

    [HttpPost("projects-matrix")]
    public async Task<IActionResult> ImportProjectsMatrix([FromBody] ImportMatrixRequest request)
    {
        var currentUserIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(currentUserIdStr, out var currentUserId)) return Unauthorized();

        string Normalize(string val) => val.ToLower().Replace(" ", "").Replace("-", "");

        HashSet<string>? selectedNamesSet = null;
        if (request.SelectedStudentNames != null && request.SelectedStudentNames.Any())
        {
            selectedNamesSet = request.SelectedStudentNames.Select(Normalize).ToHashSet();
        }

        // Mapowanie indeksu kolumny j -> Uczeń
        var columnToStudent = new Dictionary<int, Student>();
        var missingStudents = new List<string>();

        if (request.GroupId.HasValue && request.GroupId.Value != Guid.Empty)
        {
            var hasAccess = await _accessService.CanAccessGroupAsync(currentUserId, request.GroupId.Value);
            if (!hasAccess) return Forbid();

            var group = await _context.Groups
                .AsNoTracking()
                .Include(g => g.Students)
                .FirstOrDefaultAsync(g => g.Id == request.GroupId.Value);

            if (group == null)
                return NotFound(new { message = "Nie znaleziono wybranej grupy w bazie." });

            for (int j = 0; j < request.StudentNames.Count; j++)
            {
                var fullName = request.StudentNames[j];
                var normalized = Normalize(fullName);

                bool isSelected = selectedNamesSet == null || selectedNamesSet.Contains(normalized);
                if (!isSelected) continue;

                var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var firstName = parts.Length > 1 ? parts.Last() : "Nieznane";
                var lastName = parts.Length > 1 ? string.Join(" ", parts.Take(parts.Length - 1)) : (parts.Length > 0 ? parts[0] : "Nieznane");

                var targetFirst = Normalize(firstName);
                var targetLast = Normalize(lastName);

                var student = group.Students.FirstOrDefault(s =>
                    Normalize(s.FirstName) == targetFirst &&
                    Normalize(s.LastName) == targetLast);

                if (student == null)
                {
                    missingStudents.Add(fullName);
                }
                else
                {
                    columnToStudent[j] = student;
                }
            }
        }
        else
        {
            // Tryb: Bezpośrednio do uczniów (wyszukiwanie globalne)
            var allStudents = await _context.Students
                .AsNoTracking()
                .Include(s => s.Group)
                .ToListAsync();

            for (int j = 0; j < request.StudentNames.Count; j++)
            {
                var fullName = request.StudentNames[j];
                var normalized = Normalize(fullName);

                bool isSelected = selectedNamesSet != null && selectedNamesSet.Contains(normalized);
                if (!isSelected) continue;

                var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var firstName = parts.Length > 1 ? parts.Last() : "Nieznane";
                var lastName = parts.Length > 1 ? string.Join(" ", parts.Take(parts.Length - 1)) : (parts.Length > 0 ? parts[0] : "Nieznane");

                var targetFirst = Normalize(firstName);
                var targetLast = Normalize(lastName);

                var student = allStudents.FirstOrDefault(s =>
                    Normalize(s.FirstName) == targetFirst &&
                    Normalize(s.LastName) == targetLast);

                if (student == null)
                {
                    missingStudents.Add(fullName);
                }
                else
                {
                    columnToStudent[j] = student;
                }
            }
        }

        if (!columnToStudent.Any() && !missingStudents.Any())
        {
            return BadRequest(new { message = "Nie wybrano żadnego ucznia do migracji." });
        }

        // --------------------------------------------------------
        // WALIDACJA PROJEKTÓW
        // --------------------------------------------------------
        var missingProjects = new List<string>();
        var matchedProjects = new Dictionary<int, Project>();

        for (int i = 0; i < request.Rows.Count; i++)
        {
            var row = request.Rows[i];
            var code = row.ProjectCode.Trim();
            var name = row.ProjectName.Trim();

            var project = await _context.Projects.FirstOrDefaultAsync(p =>
                (!string.IsNullOrEmpty(code) && p.Code == code) ||
                (string.IsNullOrEmpty(code) && p.Name == name));

            if (project == null)
            {
                var identifier = string.IsNullOrEmpty(code) ? name : $"{code} - {name}";
                missingProjects.Add(identifier);
            }
            else
            {
                matchedProjects[i] = project;
            }
        }

        // --------------------------------------------------------
        // ZWRÓCENIE BŁĘDÓW (Tylko dla wybranych uczniów i projektów)
        // --------------------------------------------------------
        if (missingStudents.Any() || missingProjects.Any())
        {
            return BadRequest(new
            {
                message = "Zatrzymano import z powodu brakujących danych w systemie. Najpierw dodaj je do bazy lub odznacz brakujące osoby.",
                missingStudents,
                missingProjects
            });
        }

        // --------------------------------------------------------
        // ZAPIS STATUSÓW DLA WYBRANYCH UCZNIÓW
        // --------------------------------------------------------
        var matchedStudentIds = columnToStudent.Values.Select(s => s.Id).Distinct().ToList();
        var existingStudentProjects = await _context.StudentProjects
            .Where(sp => matchedStudentIds.Contains(sp.StudentId))
            .ToListAsync();

        var newStudentProjects = new List<StudentProject>();

        for (int i = 0; i < request.Rows.Count; i++)
        {
            var row = request.Rows[i];
            var project = matchedProjects[i];

            foreach (var kvp in columnToStudent)
            {
                int colIndex = kvp.Key;
                var student = kvp.Value;

                if (colIndex >= row.Statuses.Count) continue;

                var rawStatus = row.Statuses[colIndex].Trim().ToLower();
                var state = MapExcelStatus(rawStatus);

                var existingSp = existingStudentProjects
                    .FirstOrDefault(sp => sp.StudentId == student.Id && sp.ProjectId == project.Id);

                // --- ŻELAZNA ZASADA: 0 (NotStarted) NIE MA PRAWA BYĆ W BAZIE ---
                if (state == ProjectState.NotStarted)
                {
                    if (existingSp != null)
                    {
                        _context.StudentProjects.Remove(existingSp);
                    }
                    continue;
                }

                // --- Jeśli jest > 0 (Zaplanowane, W Trakcie, Zrealizowane) ---
                if (existingSp == null)
                {
                    newStudentProjects.Add(new StudentProject
                    {
                        Id = Guid.NewGuid(),
                        StudentId = student.Id,
                        ProjectId = project.Id,
                        Status = state
                    });
                }
                else
                {
                    existingSp.Status = state;
                    _context.Entry(existingSp).State = EntityState.Modified;
                }
            }
        }

        if (newStudentProjects.Any())
        {
            _context.StudentProjects.AddRange(newStudentProjects);
        }

        await _context.SaveChangesAsync();

        await _auditLogService.LogAsync(
            "Imports",
            "ImportProjectsMatrix",
            $"Zmigrowano statusy projektów dla {columnToStudent.Count} uczniów i {request.Rows.Count} projektów",
            ipAddress: HttpContext.GetClientIpAddress());

        return Ok(new { message = $"Migracja zakończona! Pomyślnie zaktualizowano statusy dla {columnToStudent.Count} uczniów i {request.Rows.Count} projektów." });
    }

    private ProjectState MapExcelStatus(string status)
    {
        return status switch
        {
            "zaplanowane" => ProjectState.Scheduled,
            "w trakcie" => ProjectState.InProgress,
            "do druku" => ProjectState.ReadytoPrint,
            "zrealizowane" => ProjectState.Completed,
            _ => ProjectState.NotStarted
        };
    }
}