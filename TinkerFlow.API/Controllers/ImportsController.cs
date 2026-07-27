using Microsoft.AspNetCore.Authorization;
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
[Authorize(Roles = "Admin,Coordinator,Trainer")]
public class ImportsController : ControllerBase
{
    private readonly TinkerFlowDbContext _context;
    private readonly IGroupAccessService _accessService;

    public ImportsController(TinkerFlowDbContext context, IGroupAccessService accessService)
    {
        _context = context;
        _accessService = accessService;
    }

    [HttpPost("projects-matrix")]
    public async Task<IActionResult> ImportProjectsMatrix([FromBody] ImportMatrixRequest request)
    {
        var currentUserIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(currentUserIdStr, out var currentUserId)) return Unauthorized();

        var hasAccess = await _accessService.CanAccessGroupAsync(currentUserId, request.GroupId);
        if (!hasAccess) return Forbid();

        var group = await _context.Groups
            .AsNoTracking()
            .Include(g => g.Students)
            .FirstOrDefaultAsync(g => g.Id == request.GroupId);

        if (group == null)

            return NotFound(new { message = "Nie znaleziono wybranej grupy w bazie." });

        var missingStudents = new List<string>();
        var matchedStudents = new List<Student>();

        // --------------------------------------------------------
        // FAZA 1A: WALIDACJA UCZNIÓW
        // --------------------------------------------------------
        foreach (var fullName in request.StudentNames)
        {
            var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
            // Imię to zawsze ostatni człon (np. "Jan" z "Kowalski Nowak Jan")
            var firstName = parts.Length > 1 ? parts.Last() : "Nieznane";
            // Nazwisko to wszystko przed imieniem (np. "Kowalski Nowak")
            var lastName = parts.Length > 1 ? string.Join(" ", parts.Take(parts.Length - 1)) : (parts.Length > 0 ? parts[0] : "Nieznane");

            // TWORZYMY KLUCZ DO PORÓWNANIA (Wywalamy śmieci)
            string Normalize(string val) => val.ToLower().Replace(" ", "").Replace("-", "");
        
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
                matchedStudents.Add(student);
            }
        }

        // --------------------------------------------------------
        // FAZA 1B: WALIDACJA PROJEKTÓW
        // --------------------------------------------------------
        var missingProjects = new List<string>();
        var matchedProjects = new Dictionary<int, Project>(); 

        for (int i = 0; i < request.Rows.Count; i++)
        {
            var row = request.Rows[i];
            
            // POPRAWKA: Czyste .Trim(), bez ?. i bez ?? ""
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
        // ZWRÓCENIE BŁĘDÓW (Jeśli czegokolwiek brakuje)
        // --------------------------------------------------------
        if (missingStudents.Any() || missingProjects.Any())
        {
            return BadRequest(new 
            { 
                message = "Zatrzymano import z powodu brakujących danych w systemie. Najpierw dodaj je do bazy lub popraw wklejony tekst.",
                missingStudents,
                missingProjects
            });
        }

        // --------------------------------------------------------
        // FAZA 2: ZAPIS POWIĄZAŃ (Tylko realne statusy!)
        // --------------------------------------------------------
        var newStudentProjects = new List<StudentProject>();
        
        var existingStudentProjects = await _context.StudentProjects
            .Where(sp => matchedStudents.Select(s => s.Id).Contains(sp.StudentId))
            .ToListAsync();

        for (int i = 0; i < request.Rows.Count; i++)
        {
            var row = request.Rows[i];
            var project = matchedProjects[i];

            for (int j = 0; j < matchedStudents.Count; j++)
            {
                if (j >= row.Statuses.Count) break;

                var student = matchedStudents[j];
                var rawStatus = row.Statuses[j].Trim().ToLower();
                var state = MapExcelStatus(rawStatus);

                var existingSp = existingStudentProjects
                    .FirstOrDefault(sp => sp.StudentId == student.Id && sp.ProjectId == project.Id);

                // --- ŻELAZNA ZASADA: 0 (NotStarted) NIE MA PRAWA BYĆ W BAZIE ---
                if (state == ProjectState.NotStarted)
                {
                    // Jeśli w bazie był stary rekord, a teraz w Excelu jest "brak" -> USUŃ GO
                    if (existingSp != null)
                    {
                        _context.StudentProjects.Remove(existingSp);
                    }
                    continue; // Przerywamy obieg dla tego ucznia, 0 nie leci dalej
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

        return Ok(new { message = $"Migracja zakończona! Przetworzono {request.Rows.Count} projektów z zachowaniem czystości bazy." });
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