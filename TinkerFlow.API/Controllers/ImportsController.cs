using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<ImportsController> _logger;

    public ImportsController(
        TinkerFlowDbContext context,
        IGroupAccessService accessService,
        IAuditLogService auditLogService,
        ILogger<ImportsController> logger)
    {
        _context = context;
        _accessService = accessService;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    private static string Normalize(string val) =>
        string.IsNullOrWhiteSpace(val) ? "" : val.ToLowerInvariant().Replace(" ", "").Replace("-", "");

    private static bool StudentMatchesName(Student s, string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return false;

        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var targetFirst = parts.Length > 1 ? parts.Last() : fullName;
        var targetLast = parts.Length > 1 ? string.Join(" ", parts.Take(parts.Length - 1)) : fullName;

        var sFirst = Normalize(s.FirstName);
        var sLast = Normalize(s.LastName);
        var tFirst = Normalize(targetFirst);
        var tLast = Normalize(targetLast);

        // Kierunek 1: "Nazwisko Imię" (np. parts.Last() to Imię)
        if (sFirst == tFirst && sLast == tLast) return true;

        // Kierunek 2: "Imię Nazwisko" (np. parts.First() to Imię)
        if (sFirst == tLast && sLast == tFirst) return true;

        // Kierunek 3: Całość złączona
        var sFull = Normalize(s.FirstName + s.LastName);
        var sFullReversed = Normalize(s.LastName + s.FirstName);
        var targetFull = Normalize(fullName);
        if (sFull == targetFull || sFullReversed == targetFull) return true;

        return false;
    }

    [HttpPost("match-students")]
    public async Task<IActionResult> MatchStudents([FromBody] MatchStudentsRequest request)
    {
        if (request.StudentNames == null || !request.StudentNames.Any())
            return Ok(new List<MatchedStudentDto>());

        try
        {
            var allStudents = await _context.Students
                .AsNoTracking()
                .Include(s => s.Group)
                    .ThenInclude(g => g!.Branch)
                .Include(s => s.Branch)
                .ToListAsync();

            var result = new List<MatchedStudentDto>();

            foreach (var fullName in request.StudentNames)
            {
                var matched = allStudents.FirstOrDefault(s => StudentMatchesName(s, fullName));

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd podczas dopasowywania uczniów do matrycy");
            return StatusCode(500, new { message = "Błąd serwera podczas dopasowywania uczniów.", detail = ex.Message });
        }
    }

    [HttpPost("projects-matrix")]
    public async Task<IActionResult> ImportProjectsMatrix([FromBody] ImportMatrixRequest request)
    {
        try
        {
            var currentUserIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(currentUserIdStr, out var currentUserId)) return Unauthorized();

            if (request.StudentNames == null || !request.StudentNames.Any())
                return BadRequest(new { message = "Brak listy uczniów do importu." });

            if (request.Rows == null || !request.Rows.Any())
                return BadRequest(new { message = "Brak wierszy projektów do zaimportowania." });

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

                    var student = group.Students.FirstOrDefault(s => StudentMatchesName(s, fullName));

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

                    bool isSelected = selectedNamesSet == null || selectedNamesSet.Contains(normalized);
                    if (!isSelected) continue;

                    var student = allStudents.FirstOrDefault(s => StudentMatchesName(s, fullName));

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
                var code = row.ProjectCode?.Trim() ?? "";
                var name = row.ProjectName?.Trim() ?? "";

                if (string.IsNullOrEmpty(code) && string.IsNullOrEmpty(name))
                    continue;

                var project = await _context.Projects.FirstOrDefaultAsync(p =>
                    (!string.IsNullOrEmpty(code) && p.Code == code) ||
                    (string.IsNullOrEmpty(code) && p.Name == name));

                if (project == null)
                {
                    var identifier = string.IsNullOrEmpty(code) ? name : $"{code} - {name}";
                    if (!missingProjects.Contains(identifier))
                    {
                        missingProjects.Add(identifier);
                    }
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

            // Słownik istniejących powiązań w bazie: (StudentId, ProjectId) -> StudentProject
            var existingSpMap = existingStudentProjects
                .GroupBy(sp => (sp.StudentId, sp.ProjectId))
                .ToDictionary(g => g.Key, g => g.First());

            // Śledzenie nowych rekordów, by zapobiec duplikatom w tej samej paczce
            var newSpMap = new Dictionary<(Guid StudentId, Guid ProjectId), StudentProject>();

            // ID rekordów do bezpiecznego usunięcia
            var spToRemove = new List<StudentProject>();

            for (int i = 0; i < request.Rows.Count; i++)
            {
                if (!matchedProjects.TryGetValue(i, out var project))
                    continue;

                var row = request.Rows[i];
                if (row.Statuses == null) continue;

                foreach (var kvp in columnToStudent)
                {
                    int colIndex = kvp.Key;
                    var student = kvp.Value;

                    if (colIndex >= row.Statuses.Count) continue;

                    var rawStatus = row.Statuses[colIndex]?.Trim()?.ToLower() ?? "";
                    var state = MapExcelStatus(rawStatus);
                    var key = (student.Id, project.Id);

                    // --- ŻELAZNA ZASADA: 0 (NotStarted) NIE MA PRAWA BYĆ W BAZIE ---
                    if (state == ProjectState.NotStarted)
                    {
                        if (newSpMap.ContainsKey(key))
                        {
                            newSpMap.Remove(key);
                        }
                        else if (existingSpMap.TryGetValue(key, out var spToDelete))
                        {
                            // Sprawdzamy czy powiązany z PrintJob, żeby uniknąć błędu FK Restrict!
                            bool hasPrintJob = await _context.PrintJobs.AnyAsync(pj => pj.StudentProjectId == spToDelete.Id);
                            if (!hasPrintJob && !spToRemove.Contains(spToDelete))
                            {
                                spToRemove.Add(spToDelete);
                                existingSpMap.Remove(key);
                            }
                        }
                        continue;
                    }

                    // --- Jeśli jest > 0 (Zaplanowane, W Trakcie, Zrealizowane) ---
                    if (existingSpMap.TryGetValue(key, out var spExisting))
                    {
                        spToRemove.Remove(spExisting);
                        spExisting.Status = state;
                        _context.Entry(spExisting).State = EntityState.Modified;
                    }
                    else if (newSpMap.TryGetValue(key, out var spNew))
                    {
                        // Jeśli ten sam projekt występuje wielokrotnie w Excelu, aktualizujemy status w paczce
                        spNew.Status = state;
                    }
                    else
                    {
                        var record = new StudentProject
                        {
                            Id = Guid.NewGuid(),
                            StudentId = student.Id,
                            ProjectId = project.Id,
                            Status = state
                        };
                        newSpMap[key] = record;
                    }
                }
            }

            using var transaction = await _context.Database.BeginTransactionAsync();

            if (spToRemove.Any())
            {
                _context.StudentProjects.RemoveRange(spToRemove);
            }

            if (newSpMap.Any())
            {
                _context.StudentProjects.AddRange(newSpMap.Values);
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            await _auditLogService.LogAsync(
                "Imports",
                "ImportProjectsMatrix",
                $"Zmigrowano statusy projektów dla {columnToStudent.Count} uczniów i {matchedProjects.Count} projektów",
                ipAddress: HttpContext.GetClientIpAddress());

            return Ok(new
            {
                message = $"Migracja zakończona sukcesem! Pomyślnie zaktualizowano statusy dla {columnToStudent.Count} uczniów i {matchedProjects.Count} projektów."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Krytyczny błąd podczas importu matrycy projektów");
            return StatusCode(500, new
            {
                message = "Wystąpił błąd podczas przetwarzania matrycy projektów na serwerze.",
                detail = ex.Message
            });
        }
    }

    private static ProjectState MapExcelStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status)) return ProjectState.NotStarted;

        var s = status.Trim().ToLowerInvariant();
        return s switch
        {
            "zaplanowane" => ProjectState.Scheduled,
            "w trakcie" => ProjectState.InProgress,
            "do druku" => ProjectState.ReadytoPrint,
            "zrealizowane" or "ukończone" or "wydrukowane" or "gotowe" or "+" or "tak" => ProjectState.Completed,
            _ => ProjectState.NotStarted
        };
    }
}