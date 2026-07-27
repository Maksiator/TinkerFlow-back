using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TinkerFlow.Domain.Entities;
using TinkerFlow.Domain.Enums;
using TinkerFlow.Infrastructure;

namespace TinkerFlow.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")] // Tylko GŁÓWNY administrator może to zrobić
public class EndOfYearController : ControllerBase
{
    private readonly TinkerFlowDbContext _context;

    public EndOfYearController(TinkerFlowDbContext context)
    {
        _context = context;
    }

    public class EndOfYearRequest 
    {
        public string AcademicYear { get; set; } = string.Empty; // np. "2025/2026"
    }

    [HttpPost("close-year")]
    public async Task<IActionResult> CloseAcademicYear([FromBody] EndOfYearRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.AcademicYear))
            return BadRequest(new { message = "Musisz podać rok szkolny, np. 2025/2026." });

        // Używamy transakcji - jeśli cokolwiek się wysypie w trakcie, baza cofnie wszystkie zmiany! (Security-by-design)
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // 1. Walidacja: Czy są jakieś projekty "Gotowe do druku" lub "Wysłane do druku"?
            var blockingProjects = await _context.Set<StudentProject>()
                .AsNoTracking()
                .Include(sp => sp.Student)
                    .ThenInclude(s => s!.Group)
                .Include(sp => sp.Project)
                .Where(sp => sp.Status == ProjectState.ReadytoPrint || sp.Status == ProjectState.SentToPrint)
                .Select(sp => new
                {
                    GroupName = sp.Student != null && sp.Student.Group != null ? sp.Student.Group.Name : "Brak przypisanej grupy",
                    StudentName = sp.Student != null ? sp.Student.FirstName + " " + sp.Student.LastName : "Nieznany uczeń",
                    ProjectName = sp.Project != null ? sp.Project.Name : "Nieznany projekt",
                    Status = sp.Status == ProjectState.ReadytoPrint ? "Gotowe do druku" : "Wysłane do druku (w paczce)"
                })
                .ToListAsync();

            if (blockingProjects.Any())
            {
                return BadRequest(new 
                { 
                    message = "Zakończenie roku zablokowane. Zanim zamkniesz rok, poniższe projekty muszą zostać zrealizowane.", 
                    blockingProjects 
                });
            }

            // 2. Pobieramy wszystkich uczniów przypisanych aktualnie do jakiejkolwiek grupy
            var studentsInGroups = await _context.Students
                .Where(s => s.GroupId != null)
                .ToListAsync();

            // 3. Kopiujemy ich do Archiwum Historii
            var historyRecords = studentsInGroups.Select(s => new StudentGroupHistory
            {
                Id = Guid.NewGuid(),
                StudentId = s.Id,
                GroupId = s.GroupId!.Value,
                AcademicYear = request.AcademicYear,
                ArchivedAt = DateTime.UtcNow
            }).ToList();

            await _context.StudentGroupHistories.AddRangeAsync(historyRecords);

            // 4. Wypisujemy ich z obecnych grup
            foreach (var student in studentsInGroups)
            {
                student.GroupId = null;
            }

            // 5. Archiwizujemy aktywne grupy
            var activeGroups = await _context.Groups
                .Where(g => !g.IsArchived)
                .ToListAsync();

            foreach (var group in activeGroups)
            {
                group.IsArchived = true;
                group.ArchivedAcademicYear = request.AcademicYear;
            }

            // 5.5. Czyścimy powiązania grup z listami trenerów (zostają puste szablony list)
            var allTrainerListItems = await _context.TrainerGroupListItems.ToListAsync();
            _context.TrainerGroupListItems.RemoveRange(allTrainerListItems);

            // 6. Całkowicie usuwamy projekty "Zaplanowane" (1) oraz "W trakcie" (2)
            var projectsToRemove = await _context.Set<StudentProject>() 
                .Where(sp => sp.Status == ProjectState.Scheduled || sp.Status == ProjectState.InProgress)
                .ToListAsync();

            _context.Set<StudentProject>().RemoveRange(projectsToRemove);

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return Ok(new { message = $"Pomyślnie zarchiwizowano rok {request.AcademicYear}, zresetowano grupy oraz usunięto niezakończone projekty." });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return StatusCode(500, new { message = "Wystąpił krytyczny błąd.", error = ex.Message });
        }
    }
    [HttpPost("undo-close-year")]
    public async Task<IActionResult> UndoCloseAcademicYear([FromBody] EndOfYearRequest request)
    {
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // 1. Odtwarzanie grup z historii
            var historyRecords = await _context.StudentGroupHistories
                .Where(h => h.AcademicYear == request.AcademicYear && !h.IsMidYear)
                .ToListAsync();

            foreach (var history in historyRecords)
            {
                // Znajdujemy ucznia i przypisujemy mu z powrotem dawną grupę
                var student = await _context.Students.FindAsync(history.StudentId);
                if (student != null)
                {
                    student.GroupId = history.GroupId;
                }
            }

            // 1.5 Odtwarzanie statusu grup (przywracamy grupy zarchiwizowane w tym roku)
            var archivedGroups = await _context.Groups
                .Where(g => g.IsArchived && g.ArchivedAcademicYear == request.AcademicYear)
                .ToListAsync();
            
            foreach(var group in archivedGroups)
            {
                group.IsArchived = false;
                group.ArchivedAcademicYear = null;
            }

            // (Projekty nie są już archiwizowane, więc cofnięcie roku przywraca tylko grupy, a nie usunięte projekty w trakcie)

            // 2. Czyszczenie Archiwum po pomyślnym odtworzeniu
            _context.StudentGroupHistories.RemoveRange(historyRecords);

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return Ok(new { message = $"Cofnięto zamknięcie roku {request.AcademicYear}." });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return StatusCode(500, new { message = "Błąd przy cofaniu.", error = ex.Message });
        }
    }
}