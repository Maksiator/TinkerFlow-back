using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TinkerFlow.API.DTOs;
using TinkerFlow.API.Extensions;
using TinkerFlow.Infrastructure;
using TinkerFlow.Infrastructure.Services;

namespace TinkerFlow.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Route("api/admin/orphans")]
[Authorize(Roles = "Admin")]
public class MaintenanceController : ControllerBase
{
    private readonly TinkerFlowDbContext _context;
    private readonly IAuditLogService _auditLogService;

    public MaintenanceController(TinkerFlowDbContext context, IAuditLogService auditLogService)
    {
        _context = context;
        _auditLogService = auditLogService;
    }

    /// <summary>
    /// Pobiera raport o rekordach osieroconych (brak powiązania w bazie, martwe klucze obce itp.)
    /// </summary>
    [HttpGet]
    [HttpGet("orphans")]
    public async Task<ActionResult<OrphanReportResponse>> GetOrphans()
    {
        var categories = new List<OrphanCategoryDto>();

        // 1. StudentProjects (brak powiązanego ucznia lub brak powiązanego projektu)
        var orphanedStudentProjects = await _context.StudentProjects
            .AsNoTracking()
            .Where(sp => !_context.Students.Any(s => s.Id == sp.StudentId) ||
                         !_context.Projects.Any(p => p.Id == sp.ProjectId))
            .Select(sp => new OrphanItemDto(
                sp.Id,
                "StudentProject",
                !_context.Students.Any(s => s.Id == sp.StudentId) 
                    ? $"Uczeń o Id '{sp.StudentId}' nie istnieje." 
                    : $"Szablon projektu o Id '{sp.ProjectId}' nie istnieje.",
                $"Status: {sp.Status}"))
            .ToListAsync();

        categories.Add(new OrphanCategoryDto(
            "StudentProjects",
            "Projekty uczniów",
            "Wpisy projektów przypisane do nieistniejących uczniów lub szablonów projektów.",
            orphanedStudentProjects.Count,
            orphanedStudentProjects));

        // 2. PrintJobs (brak paczki, brak ucznia lub martwy StudentProjectId)
        var orphanedPrintJobs = await _context.PrintJobs
            .AsNoTracking()
            .Where(pj => !_context.PrintBatches.Any(pb => pb.Id == pj.PrintBatchId) ||
                         !_context.Students.Any(s => s.Id == pj.StudentId) ||
                         (pj.StudentProjectId != null && !_context.StudentProjects.Any(sp => sp.Id == pj.StudentProjectId)))
            .Select(pj => new OrphanItemDto(
                pj.Id,
                "PrintJob",
                !_context.PrintBatches.Any(pb => pb.Id == pj.PrintBatchId)
                    ? $"Paczka wydruków o Id '{pj.PrintBatchId}' nie istnieje."
                    : (!_context.Students.Any(s => s.Id == pj.StudentId)
                        ? $"Uczeń o Id '{pj.StudentId}' nie istnieje."
                        : $"Projekt ucznia o Id '{pj.StudentProjectId}' nie istnieje."),
                $"Model: {pj.CustomName ?? "Z matrycy"}, Utworzono: {pj.CreatedAt:yyyy-MM-dd}"))
            .ToListAsync();

        categories.Add(new OrphanCategoryDto(
            "PrintJobs",
            "Zadania wydruku",
            "Wydruki przypisane do usuniętych paczek, uczniów lub projektów.",
            orphanedPrintJobs.Count,
            orphanedPrintJobs));

        // 3. Students (uczeń ma wpisane nieistniejące GroupId lub nieistniejące BranchId)
        var orphanedStudents = await _context.Students
            .AsNoTracking()
            .Where(s => (s.GroupId != null && !_context.Groups.Any(g => g.Id == s.GroupId)) ||
                         (s.BranchId != null && !_context.Branches.Any(b => b.Id == s.BranchId)))
            .Select(s => new OrphanItemDto(
                s.Id,
                "Student",
                s.GroupId != null && !_context.Groups.Any(g => g.Id == s.GroupId)
                    ? $"Przypisana grupa o Id '{s.GroupId}' nie istnieje w bazie."
                    : $"Przypisany oddział o Id '{s.BranchId}' nie istnieje w bazie.",
                $"Uczeń: {s.FirstName} {s.LastName}"))
            .ToListAsync();

        categories.Add(new OrphanCategoryDto(
            "Students",
            "Uczniowie z martwymi referencjami",
            "Uczniowie wskazujący na grupy lub oddziały, które zostały trwale usunięte.",
            orphanedStudents.Count,
            orphanedStudents));

        // 4. Groups (grupy z martwym trenerem głównym lub drukarzem)
        var orphanedGroups = await _context.Groups
            .AsNoTracking()
            .Where(g => (g.PrimaryTrainerId != null && !_context.Users.Any(u => u.Id == g.PrimaryTrainerId)) ||
                         (g.AssignedPrinterId != null && !_context.Users.Any(u => u.Id == g.AssignedPrinterId)) ||
                         !_context.Branches.Any(b => b.Id == g.BranchId))
            .Select(g => new OrphanItemDto(
                g.Id,
                "Group",
                !_context.Branches.Any(b => b.Id == g.BranchId)
                    ? $"Oddział o Id '{g.BranchId}' nie istnieje."
                    : (g.PrimaryTrainerId != null && !_context.Users.Any(u => u.Id == g.PrimaryTrainerId)
                        ? $"Główny trener o Id '{g.PrimaryTrainerId}' nie istnieje."
                        : $"Przypisany drukarz o Id '{g.AssignedPrinterId}' nie istnieje."),
                $"Grupa: {g.Name}"))
            .ToListAsync();

        categories.Add(new OrphanCategoryDto(
            "Groups",
            "Grupy z martwymi referencjami",
            "Grupy posiadające przypisanie do usuniętego trenera, drukarza lub oddziału.",
            orphanedGroups.Count,
            orphanedGroups));

        // 5. StudentGroupHistories (historia grupy ucznia)
        var orphanedHistories = await _context.StudentGroupHistories
            .AsNoTracking()
            .Where(h => !_context.Students.Any(s => s.Id == h.StudentId) ||
                         !_context.Groups.Any(g => g.Id == h.GroupId))
            .Select(h => new OrphanItemDto(
                h.Id,
                "StudentGroupHistory",
                !_context.Students.Any(s => s.Id == h.StudentId)
                    ? $"Uczeń o Id '{h.StudentId}' nie istnieje."
                    : $"Grupa o Id '{h.GroupId}' nie istnieje.",
                $"Rok: {h.AcademicYear}, Archiwizowano: {h.ArchivedAt:yyyy-MM-dd}"))
            .ToListAsync();

        categories.Add(new OrphanCategoryDto(
            "StudentGroupHistories",
            "Historia grup uczniów",
            "Archiwalne wpisy przynależności wskazujące na usuniętego ucznia lub usuniętą grupę.",
            orphanedHistories.Count,
            orphanedHistories));

        // 6. TrainerGroupListItems (elementy ulubionych list trenerów)
        var orphanedListItems = await _context.TrainerGroupListItems
            .AsNoTracking()
            .Where(item => !_context.TrainerGroupLists.Any(l => l.Id == item.TrainerGroupListId) ||
                           !_context.Groups.Any(g => g.Id == item.GroupId))
            .Select(item => new OrphanItemDto(
                item.TrainerGroupListId,
                "TrainerGroupListItem",
                !_context.TrainerGroupLists.Any(l => l.Id == item.TrainerGroupListId)
                    ? $"Lista o Id '{item.TrainerGroupListId}' nie istnieje."
                    : $"Grupa o Id '{item.GroupId}' nie istnieje.",
                $"Para kluczy: ListId={item.TrainerGroupListId}, GroupId={item.GroupId}"))
            .ToListAsync();

        categories.Add(new OrphanCategoryDto(
            "TrainerGroupListItems",
            "Elementy list trenerów",
            "Elementy ulubionych list trenerów odwołujące się do usuniętych grup lub list.",
            orphanedListItems.Count,
            orphanedListItems));

        // 7. GroupSubstitutes (zastępstwa)
        var orphanedSubstitutes = await _context.GroupSubstitutes
            .AsNoTracking()
            .Where(gs => !_context.Groups.Any(g => g.Id == gs.GroupId) ||
                         !_context.Users.Any(u => u.Id == gs.SubstituteTrainerId))
            .Select(gs => new OrphanItemDto(
                gs.Id,
                "GroupSubstitute",
                !_context.Groups.Any(g => g.Id == gs.GroupId)
                    ? $"Grupa o Id '{gs.GroupId}' nie istnieje."
                    : $"Trener zastępujący o Id '{gs.SubstituteTrainerId}' nie istnieje.",
                $"Data zajęć: {gs.LessonDate:yyyy-MM-dd}"))
            .ToListAsync();

        categories.Add(new OrphanCategoryDto(
            "GroupSubstitutes",
            "Zastępstwa",
            "Wpisy zastępstw odwołujące się do usuniętych grup lub trenerów.",
            orphanedSubstitutes.Count,
            orphanedSubstitutes));

        // 8. ClassSessions (odbyte zajęcia)
        var orphanedSessions = await _context.ClassSessions
            .AsNoTracking()
            .Where(cs => !_context.Groups.Any(g => g.Id == cs.GroupId) ||
                         !_context.Users.Any(u => u.Id == cs.TrainerId))
            .Select(cs => new OrphanItemDto(
                cs.Id,
                "ClassSession",
                !_context.Groups.Any(g => g.Id == cs.GroupId)
                    ? $"Grupa o Id '{cs.GroupId}' nie istnieje."
                    : $"Trener o Id '{cs.TrainerId}' nie istnieje.",
                $"Data zajęć: {cs.Date:yyyy-MM-dd}"))
            .ToListAsync();

        categories.Add(new OrphanCategoryDto(
            "ClassSessions",
            "Sesje zajęć",
            "Sesje zajęć wskazujące na nieistniejącą grupę lub nieistniejącego trenera.",
            orphanedSessions.Count,
            orphanedSessions));

        // 9. UserBranches (przypisania pracowników do oddziałów)
        var orphanedUserBranches = await _context.UserBranches
            .AsNoTracking()
            .Where(ub => !_context.Users.Any(u => u.Id == ub.UserId) ||
                         !_context.Branches.Any(b => b.Id == ub.BranchId))
            .Select(ub => new OrphanItemDto(
                ub.UserId,
                "UserBranch",
                !_context.Users.Any(u => u.Id == ub.UserId)
                    ? $"Użytkownik o Id '{ub.UserId}' nie istnieje."
                    : $"Oddział o Id '{ub.BranchId}' nie istnieje.",
                $"Para: UserId={ub.UserId}, BranchId={ub.BranchId}"))
            .ToListAsync();

        categories.Add(new OrphanCategoryDto(
            "UserBranches",
            "Powiązania pracownik-oddział",
            "Powiązania w tabeli łącznikowej wskazujące na usuniętego pracownika lub oddział.",
            orphanedUserBranches.Count,
            orphanedUserBranches));

        var total = categories.Sum(c => c.Count);
        return Ok(new OrphanReportResponse(total, DateTime.UtcNow, categories));
    }

    /// <summary>
    /// Czyści wybrane lub wszystkie kategorie osieroconych danych.
    /// W przypadku tabel potomnych (np. StudentProjects, PrintJobs) usuwa rekordy.
    /// W przypadku encji nadrzędnych (np. Uczeń z usuniętą grupą) zeruje martwy identyfikator (GroupId = null).
    /// </summary>
    [HttpPost("cleanup")]
    public async Task<ActionResult<CleanupOrphansResponse>> CleanupOrphans([FromBody] CleanupOrphansRequest? request)
    {
        var selectedCategories = request?.Categories != null && request.Categories.Any()
            ? new HashSet<string>(request.Categories, StringComparer.OrdinalIgnoreCase)
            : null; // null oznacza wyczyść wszystko

        var shouldClean = new Func<string, bool>(cat => selectedCategories == null || selectedCategories.Contains(cat));
        var cleanedSummary = new Dictionary<string, int>();
        int totalCleaned = 0;

        // 1. StudentProjects
        if (shouldClean("StudentProjects"))
        {
            var orphans = await _context.StudentProjects
                .Where(sp => !_context.Students.Any(s => s.Id == sp.StudentId) ||
                             !_context.Projects.Any(p => p.Id == sp.ProjectId))
                .ToListAsync();

            if (orphans.Any())
            {
                _context.StudentProjects.RemoveRange(orphans);
                cleanedSummary["StudentProjects"] = orphans.Count;
                totalCleaned += orphans.Count;
            }
        }

        // 2. PrintJobs
        if (shouldClean("PrintJobs"))
        {
            var orphans = await _context.PrintJobs
                .Where(pj => !_context.PrintBatches.Any(pb => pb.Id == pj.PrintBatchId) ||
                             !_context.Students.Any(s => s.Id == pj.StudentId) ||
                             (pj.StudentProjectId != null && !_context.StudentProjects.Any(sp => sp.Id == pj.StudentProjectId)))
                .ToListAsync();

            if (orphans.Any())
            {
                _context.PrintJobs.RemoveRange(orphans);
                cleanedSummary["PrintJobs"] = orphans.Count;
                totalCleaned += orphans.Count;
            }
        }

        // 3. Students (zerujemy martwe referencje, nie usuwamy samego ucznia)
        if (shouldClean("Students"))
        {
            var studentsWithDeadGroup = await _context.Students
                .Where(s => s.GroupId != null && !_context.Groups.Any(g => g.Id == s.GroupId))
                .ToListAsync();

            foreach (var s in studentsWithDeadGroup)
            {
                s.GroupId = null;
            }

            var studentsWithDeadBranch = await _context.Students
                .Where(s => s.BranchId != null && !_context.Branches.Any(b => b.Id == s.BranchId))
                .ToListAsync();

            foreach (var s in studentsWithDeadBranch)
            {
                s.BranchId = null;
            }

            var fixedStudentsCount = studentsWithDeadGroup.Count + studentsWithDeadBranch.Count;
            if (fixedStudentsCount > 0)
            {
                cleanedSummary["Students"] = fixedStudentsCount;
                totalCleaned += fixedStudentsCount;
            }
        }

        // 4. Groups (zerujemy martwych trenerów / drukarzy)
        if (shouldClean("Groups"))
        {
            var groupsWithDeadTrainer = await _context.Groups
                .Where(g => g.PrimaryTrainerId != null && !_context.Users.Any(u => u.Id == g.PrimaryTrainerId))
                .ToListAsync();

            foreach (var g in groupsWithDeadTrainer)
            {
                g.PrimaryTrainerId = null;
            }

            var groupsWithDeadPrinter = await _context.Groups
                .Where(g => g.AssignedPrinterId != null && !_context.Users.Any(u => u.Id == g.AssignedPrinterId))
                .ToListAsync();

            foreach (var g in groupsWithDeadPrinter)
            {
                g.AssignedPrinterId = null;
            }

            var fixedGroupsCount = groupsWithDeadTrainer.Count + groupsWithDeadPrinter.Count;
            if (fixedGroupsCount > 0)
            {
                cleanedSummary["Groups"] = fixedGroupsCount;
                totalCleaned += fixedGroupsCount;
            }
        }

        // 5. StudentGroupHistories
        if (shouldClean("StudentGroupHistories"))
        {
            var orphans = await _context.StudentGroupHistories
                .Where(h => !_context.Students.Any(s => s.Id == h.StudentId) ||
                             !_context.Groups.Any(g => g.Id == h.GroupId))
                .ToListAsync();

            if (orphans.Any())
            {
                _context.StudentGroupHistories.RemoveRange(orphans);
                cleanedSummary["StudentGroupHistories"] = orphans.Count;
                totalCleaned += orphans.Count;
            }
        }

        // 6. TrainerGroupListItems
        if (shouldClean("TrainerGroupListItems"))
        {
            var orphans = await _context.TrainerGroupListItems
                .Where(item => !_context.TrainerGroupLists.Any(l => l.Id == item.TrainerGroupListId) ||
                               !_context.Groups.Any(g => g.Id == item.GroupId))
                .ToListAsync();

            if (orphans.Any())
            {
                _context.TrainerGroupListItems.RemoveRange(orphans);
                cleanedSummary["TrainerGroupListItems"] = orphans.Count;
                totalCleaned += orphans.Count;
            }
        }

        // 7. GroupSubstitutes
        if (shouldClean("GroupSubstitutes"))
        {
            var orphans = await _context.GroupSubstitutes
                .Where(gs => !_context.Groups.Any(g => g.Id == gs.GroupId) ||
                             !_context.Users.Any(u => u.Id == gs.SubstituteTrainerId))
                .ToListAsync();

            if (orphans.Any())
            {
                _context.GroupSubstitutes.RemoveRange(orphans);
                cleanedSummary["GroupSubstitutes"] = orphans.Count;
                totalCleaned += orphans.Count;
            }
        }

        // 8. ClassSessions
        if (shouldClean("ClassSessions"))
        {
            var orphans = await _context.ClassSessions
                .Where(cs => !_context.Groups.Any(g => g.Id == cs.GroupId) ||
                             !_context.Users.Any(u => u.Id == cs.TrainerId))
                .ToListAsync();

            if (orphans.Any())
            {
                _context.ClassSessions.RemoveRange(orphans);
                cleanedSummary["ClassSessions"] = orphans.Count;
                totalCleaned += orphans.Count;
            }
        }

        // 9. UserBranches
        if (shouldClean("UserBranches"))
        {
            var orphans = await _context.UserBranches
                .Where(ub => !_context.Users.Any(u => u.Id == ub.UserId) ||
                             !_context.Branches.Any(b => b.Id == ub.BranchId))
                .ToListAsync();

            if (orphans.Any())
            {
                _context.UserBranches.RemoveRange(orphans);
                cleanedSummary["UserBranches"] = orphans.Count;
                totalCleaned += orphans.Count;
            }
        }

        if (totalCleaned > 0)
        {
            await _context.SaveChangesAsync();

            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var userRoleStr = User.FindFirstValue(ClaimTypes.Role);

            await _auditLogService.LogAsync(
                "Maintenance",
                "CleanupOrphans",
                $"Wyczyszczono/naprawiono {totalCleaned} osieroconych rekordów w bazie danych. Szczegóły: {string.Join(", ", cleanedSummary.Select(kv => $"{kv.Key}: {kv.Value}"))}",
                userId: Guid.TryParse(currentUserId, out var uid) ? uid : null,
                userRole: userRoleStr,
                ipAddress: HttpContext.GetClientIpAddress());
        }

        return Ok(new CleanupOrphansResponse(
            Success: true,
            TotalCleaned: totalCleaned,
            CleanedByCategory: cleanedSummary,
            Message: totalCleaned > 0
                ? $"Pomyślnie wyczyszczono {totalCleaned} osieroconych powiązań/rekordów w bazie danych."
                : "Brak osieroconych rekordów do wyczyszczenia w wybranych kategoriach."));
    }
}
