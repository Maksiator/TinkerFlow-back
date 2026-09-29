using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TinkerFlow.Domain.Entities;
using TinkerFlow.Infrastructure;
using TinkerFlow.API.DTOs;
using TinkerFlow.API.Extensions;
using Microsoft.AspNetCore.Authorization;
using TinkerFlow.Domain.Enums;
using TinkerFlow.Infrastructure.Services;
using System.Security.Claims;

namespace TinkerFlow.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class GroupsController : ControllerBase
{
    private readonly TinkerFlowDbContext _context;
    private readonly IGroupAccessService _accessService; 
    private readonly IAuditLogService _auditLogService;

    public GroupsController(
        TinkerFlowDbContext context,
        IGroupAccessService accessService,
        IAuditLogService auditLogService)
    {
        _context = context;
        _accessService = accessService;
        _auditLogService = auditLogService;
    }
    
    private Guid GetCurrentUserId()
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(userIdString, out var parsedId))
        {
            return parsedId;
        }
        throw new UnauthorizedAccessException("Nieprawidłowy token użytkownika.");
    }
    
    [HttpPost]
    [Authorize(Roles = "Admin,Coordinator")]
    public async Task<IActionResult> CreateGroup([FromBody] CreateGroupRequest request)
    {
        var currentUserId = GetCurrentUserId();
        var userRoleStr = User.FindFirstValue(ClaimTypes.Role);

        // ZABEZPIECZENIE: Koordynator może tworzyć grupy TYLKO w przypisanych do siebie oddziałach
        if (userRoleStr == "Coordinator")
        {
            var hasBranchAccess = await _context.UserBranches
                .AnyAsync(ub => ub.UserId == currentUserId && ub.BranchId == request.BranchId);
            
            if (!hasBranchAccess) 
                return Forbid(); // Rzuca 403, ucinając request
        }

        // Sprawdzamy czy oddział w ogóle istnieje
        var branchExists = await _context.Branches.AnyAsync(b => b.Id == request.BranchId);
        if (!branchExists) return BadRequest(new { message = "Podany oddział nie istnieje." });

        var newGroup = new Group
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            BranchId = request.BranchId,
            PrimaryTrainerId = request.PrimaryTrainerId,
            AssignedPrinterId = request.AssignedPrinterId,
            ClassDayOfWeek = request.ClassDayOfWeek,
            Type = request.Type
        };
        
        _context.Groups.Add(newGroup);
        await _context.SaveChangesAsync();

        await _auditLogService.LogAsync(
            "Groups",
            "CreateGroup",
            $"Utworzono grupę '{newGroup.Name}' (Typ: {newGroup.Type})",
            entityId: newGroup.Id,
            entityName: newGroup.Name,
            userId: currentUserId,
            userRole: userRoleStr,
            ipAddress: HttpContext.GetClientIpAddress());
        
        return CreatedAtAction(nameof(GetGroup), new { id = newGroup.Id }, new { id = newGroup.Id });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,Coordinator")]
    public async Task<IActionResult> UpdateGroup(Guid id, [FromBody] UpdateGroupRequest request)
    {
        var currentUserId = GetCurrentUserId();
        var userRoleStr = User.FindFirstValue(ClaimTypes.Role);
        
        var group = await _context.Groups.FindAsync(id);
        
        if (group == null)
        {
            return NotFound(new { message = "Nie odnaleziono grupy o podanym ID." });
        }

        // ZABEZPIECZENIE DLA KOORDYNATORA PRZY EDYCJI
        if (userRoleStr == "Coordinator")
        {
            // 1. Sprawdzamy czy ma dostęp do obecnego oddziału grupy (żeby nie edytował cudzych grup)
            if (!await _accessService.CanAccessGroupAsync(currentUserId, id)) return Forbid();
            
            // 2. Jeśli zmienia oddział w formularzu, sprawdzamy czy ma dostęp do TEGO NOWEGO oddziału
            var hasAccessToNewBranch = await _context.UserBranches
                .AnyAsync(ub => ub.UserId == currentUserId && ub.BranchId == request.BranchId);
            if (!hasAccessToNewBranch) return Forbid();
        }
        
        group.Name = request.Name;
        group.BranchId = request.BranchId;
        group.PrimaryTrainerId = request.PrimaryTrainerId;
        group.AssignedPrinterId = request.AssignedPrinterId;
        group.ClassDayOfWeek = request.ClassDayOfWeek;
        group.Type = request.Type;

        await _context.SaveChangesAsync();

        await _auditLogService.LogAsync(
            "Groups",
            "UpdateGroup",
            $"Zaktualizowano dane grupy '{group.Name}' (Typ: {group.Type})",
            entityId: group.Id,
            entityName: group.Name,
            userId: currentUserId,
            userRole: userRoleStr,
            ipAddress: HttpContext.GetClientIpAddress());
        
        return NoContent();
    }
    
    // --- TUTAJ ZACZYNAJĄ SIĘ ODCHUDZONE METODY GET ---

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetGroup(Guid id)
    {
        // Wykorzystujemy ujednolicony serwis do odfiltrowania bazy
        var accessibleGroups = await _accessService.GetAccessibleGroupsQueryAsync(GetCurrentUserId());
        
        var groupResponse = await accessibleGroups
            .AsNoTracking()
            .Where(g => g.Id == id)
            .Select(g => new GroupResponse(
                g.Id, 
                g.Name, 
                g.BranchId,
                g.Branch.Name,
                g.PrimaryTrainerId,
                g.PrimaryTrainer != null ? g.PrimaryTrainer.FirstName + " " + g.PrimaryTrainer.LastName : null,
                g.IsArchived ? _context.StudentGroupHistories.Count(h => h.GroupId == g.Id && !h.IsMidYear) : g.Students.Count(),
                g.ClassDayOfWeek,
                g.IsArchived,
                g.ArchivedAcademicYear,
                g.AssignedPrinterId,
                g.AssignedPrinter != null ? g.AssignedPrinter.FirstName + " " + g.AssignedPrinter.LastName : null,
                g.Type
            ))
            .FirstOrDefaultAsync();
        
        // Jeśli groupResponse jest nullem, to znaczy że grupy nie ma LUB użytkownik nie ma do niej dostępu
        if (groupResponse == null) return NotFound(new { message = "Nie znaleziono grupy lub brak dostępu." });
    
        return Ok(groupResponse);
    }
    
    [HttpGet]
    public async Task<IActionResult> GetGroups([FromQuery] bool includeArchived = false)
    {
        // 100% logiki uprawnień przenieśliśmy do serwisu. Pobieramy tylko to, co nam wolno.
        var accessibleGroups = await _accessService.GetAccessibleGroupsQueryAsync(GetCurrentUserId());

        if (!includeArchived)
        {
            accessibleGroups = accessibleGroups.Where(g => !g.IsArchived);
        }

        var result = await accessibleGroups
            .AsNoTracking()
            .Select(g => new GroupResponse(
                g.Id, g.Name, g.BranchId, g.Branch.Name, 
                g.PrimaryTrainerId,
                g.PrimaryTrainer != null ? g.PrimaryTrainer.FirstName + " " + g.PrimaryTrainer.LastName : null,
                g.IsArchived ? _context.StudentGroupHistories.Count(h => h.GroupId == g.Id && !h.IsMidYear) : g.Students.Count(),
                g.ClassDayOfWeek,
                g.IsArchived,
                g.ArchivedAcademicYear,
                g.AssignedPrinterId,
                g.AssignedPrinter != null ? g.AssignedPrinter.FirstName + " " + g.AssignedPrinter.LastName : null,
                g.Type))
            .ToListAsync();

        return Ok(result);
    }

    [HttpGet("{groupId}/matrix")]
    public async Task<IActionResult> GetGroupMatrix(Guid groupId)
    {
        var currentUserId = GetCurrentUserId();
        
        // Zabezpieczenie z wykorzystaniem naszego serwisu
        var hasAccess = await _accessService.CanAccessGroupAsync(currentUserId, groupId);
        
        if (!hasAccess)
        {
            return Forbid(); 
        }
        
        // KROK 1: Pobierz grupę razem ze studentami
        var group = await _context.Groups
            .AsNoTracking()
            .Include(g => g.Branch)
            .Include(g => g.Students)
            .FirstOrDefaultAsync(g => g.Id == groupId);

        if (group == null)
        {
            return NotFound(new { message = "Nie znaleziono grupy o podanym ID." });
        }

        // KROK 2: Wyciągnij ID studentów (pomijając potencjalne nulle)
        var studentIds = group.Students.Where(s => s != null).Select(s => s.Id).ToList();

        // KROK 3: Pobierz wszystkie statusy projektów dla tych studentów w jednym zapytaniu
        var allProjectsForStudents = await _context.StudentProjects
            .AsNoTracking()
            .Where(sp => studentIds.Contains(sp.StudentId))
            .Include(sp => sp.Project) // Zaciągnij od razu dane projektu
            .ToListAsync();

        // KROK 4: Zbuduj odpowiedź w pamięci
        var groupMatrix = new
        {
            GroupId = group.Id,
            GroupName = group.Name,
            GroupType = group.Type,
            BranchName = group.Branch?.Name ?? string.Empty,
            Students = group.Students.Where(s => s != null).Select(s => new
            {
                StudentId = s.Id,
                FullName = s.FirstName + " " + s.LastName,
                DateOfBirth = s.DateOfBirth.HasValue ? s.DateOfBirth.Value.ToString("yyyy-MM-dd") : null,
                Projects = allProjectsForStudents
                    .Where(sp => sp.StudentId == s.Id) // Filtruj w pamięci
                    .Select(sp => new
                    {
                        sp.ProjectId,
                        ProjectName = sp.Project?.Name ?? "Nieznany projekt",
                        sp.Status
                    }).ToList()
            })
        };

        return Ok(groupMatrix);
    }
    
    [HttpPost("matrix/bulk")]
    public async Task<IActionResult> GetGroupMatrixBulk([FromBody] List<Guid> groupIds)
    {
        var currentUserId = GetCurrentUserId();
        var accessibleGroups = await _accessService.GetAccessibleGroupsQueryAsync(currentUserId);
        
        var allowedGroupIds = await accessibleGroups
            .AsNoTracking()
            .Where(g => groupIds.Contains(g.Id))
            .Select(g => g.Id)
            .ToListAsync();

        if (!allowedGroupIds.Any())
            return Ok(new List<object>()); // Empty result if no access

        // Zoptymalizowane zapytanie pobierające wiele grup z uczniami na raz
        var groups = await _context.Groups
            .AsNoTracking()
            .Include(g => g.Branch)
            .Include(g => g.Students)
            .Where(g => allowedGroupIds.Contains(g.Id))
            .ToListAsync();

        var studentIds = groups.SelectMany(g => g.Students).Where(s => s != null).Select(s => s.Id).ToList();

        var allProjectsForStudents = await _context.StudentProjects
            .AsNoTracking()
            .Where(sp => studentIds.Contains(sp.StudentId))
            .Include(sp => sp.Project)
            .ToListAsync();

        var response = groups.Select(group => new
        {
            GroupId = group.Id,
            GroupName = group.Name,
            GroupType = group.Type,
            Location = group.Branch?.Name ?? string.Empty,
            Students = group.Students.Where(s => s != null).Select(s => new
            {
                StudentId = s.Id,
                FullName = s.FirstName + " " + s.LastName,
                DateOfBirth = s.DateOfBirth.HasValue ? s.DateOfBirth.Value.ToString("yyyy-MM-dd") : null,
                Projects = allProjectsForStudents
                    .Where(sp => sp.StudentId == s.Id)
                    .Select(sp => new
                    {
                        sp.ProjectId,
                        ProjectName = sp.Project?.Name ?? "Nieznany projekt",
                        sp.Status
                    }).ToList()
            }).ToList()
        }).ToList();

        return Ok(response);
    }
    
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin,Coordinator")]
    public async Task<IActionResult> DeleteGroup(Guid id)
    {
        var currentUserId = GetCurrentUserId();
        var userRoleStr = User.FindFirstValue(ClaimTypes.Role);
        
        // DODATKOWE ZABEZPIECZENIE: Koordynator nie usunie grupy z cudzego oddziału
        if (userRoleStr == "Coordinator")
        {
             if (!await _accessService.CanAccessGroupAsync(currentUserId, id)) return Forbid();
        }

        var group = await _context.Groups.FindAsync(id);
        
        if (group == null)
        {
            return NotFound(new {message = "Nie odnaleziono grupy o podanym ID."});
        }
        
        var hasStudents = await _context.Students.AnyAsync(s => s.GroupId == id);
        if (hasStudents)
        {
            return BadRequest(new {message = "Nie można usunąć grupy, która ma przypisanych uczniów."});
        }
        
        _context.Groups.Remove(group);
        await _context.SaveChangesAsync();

        await _auditLogService.LogAsync(
            "Groups",
            "DeleteGroup",
            $"Usunięto grupę '{group.Name}'",
            entityId: group.Id,
            entityName: group.Name,
            userId: currentUserId,
            userRole: userRoleStr,
            ipAddress: HttpContext.GetClientIpAddress());
        
        return NoContent();
    }

    [HttpPost("bulk-delete")]
    [Authorize(Roles = "Admin,Coordinator")]
    public async Task<IActionResult> BulkDeleteGroups([FromBody] BulkDeleteGroupsRequest request)
    {
        var currentUserId = GetCurrentUserId();
        var userRoleStr = User.FindFirstValue(ClaimTypes.Role);

        if (request.GroupIds == null || !request.GroupIds.Any())
        {
            return BadRequest(new { message = "Lista grup do usunięcia jest pusta." });
        }

        var groups = await _context.Groups
            .Where(g => request.GroupIds.Contains(g.Id))
            .ToListAsync();

        if (!groups.Any())
        {
            return NotFound(new { message = "Nie znaleziono podanych grup." });
        }

        // Zabezpieczenie dla Koordynatora
        if (userRoleStr == "Coordinator")
        {
            foreach (var group in groups)
            {
                if (!await _accessService.CanAccessGroupAsync(currentUserId, group.Id))
                {
                    return Forbid();
                }
            }
        }

        // Sprawdzamy czy którekolwiek z grup mają uczniów
        var groupsWithStudents = await _context.Students
            .Where(s => s.GroupId.HasValue && request.GroupIds.Contains(s.GroupId.Value))
            .Select(s => s.Group!.Name)
            .Distinct()
            .ToListAsync();

        if (groupsWithStudents.Any())
        {
            return BadRequest(new { message = $"Nie można usunąć grup z przypisanymi uczniami: {string.Join(", ", groupsWithStudents)}. Najpierw przenieś lub wypisz uczniów." });
        }

        _context.Groups.RemoveRange(groups);
        await _context.SaveChangesAsync();

        await _auditLogService.LogAsync(
            "Groups",
            "BulkDeleteGroups",
            $"Usunięto masowo {groups.Count} grup: {string.Join(", ", groups.Select(g => g.Name))}",
            userId: currentUserId,
            userRole: userRoleStr,
            ipAddress: HttpContext.GetClientIpAddress());

        return Ok(new { count = groups.Count, message = $"Pomyślnie usunięto {groups.Count} grup." });
    }

    [HttpPost("bulk-change-branch")]
    [Authorize(Roles = "Admin,Coordinator")]
    public async Task<IActionResult> BulkChangeGroupBranch([FromBody] BulkChangeBranchRequest request)
    {
        var currentUserId = GetCurrentUserId();
        var userRoleStr = User.FindFirstValue(ClaimTypes.Role);

        if (request.GroupIds == null || !request.GroupIds.Any())
        {
            return BadRequest(new { message = "Lista grup jest pusta." });
        }

        var branchExists = await _context.Branches.AnyAsync(b => b.Id == request.BranchId);
        if (!branchExists)
        {
            return BadRequest(new { message = "Docelowy oddział nie istnieje." });
        }

        // Sprawdzenie dostępu koordynatora do docelowego oddziału
        if (userRoleStr == "Coordinator")
        {
            var hasAccessToTargetBranch = await _context.UserBranches
                .AnyAsync(ub => ub.UserId == currentUserId && ub.BranchId == request.BranchId);
            if (!hasAccessToTargetBranch)
            {
                return Forbid();
            }
        }

        var groups = await _context.Groups
            .Where(g => request.GroupIds.Contains(g.Id))
            .ToListAsync();

        if (!groups.Any())
        {
            return NotFound(new { message = "Nie znaleziono podanych grup." });
        }

        // Sprawdzenie dostępu koordynatora do dotychczasowych grup
        if (userRoleStr == "Coordinator")
        {
            foreach (var group in groups)
            {
                if (!await _accessService.CanAccessGroupAsync(currentUserId, group.Id))
                {
                    return Forbid();
                }
            }
        }

        foreach (var group in groups)
        {
            group.BranchId = request.BranchId;
        }

        // Aktualizacja BranchId także dla wszystkich uczniów w tych grupach dla spójności
        var studentsInGroups = await _context.Students
            .Where(s => s.GroupId.HasValue && request.GroupIds.Contains(s.GroupId.Value))
            .ToListAsync();

        foreach (var student in studentsInGroups)
        {
            student.BranchId = request.BranchId;
        }

        await _context.SaveChangesAsync();

        await _auditLogService.LogAsync(
            "Groups",
            "BulkChangeBranch",
            $"Zmieniono oddział dla {groups.Count} grup na '{branchExists}'",
            userId: currentUserId,
            userRole: userRoleStr,
            ipAddress: HttpContext.GetClientIpAddress());

        return Ok(new { count = groups.Count, message = $"Pomyślnie zmieniono oddział dla {groups.Count} grup." });
    }

    [HttpPost("bulk-assign-printer")]
    [Authorize(Roles = "Admin,Coordinator")]
    public async Task<IActionResult> BulkAssignPrinter([FromBody] BulkAssignPrinterRequest request)
    {
        var currentUserId = GetCurrentUserId();
        var userRoleStr = User.FindFirstValue(ClaimTypes.Role);

        if (request.GroupIds == null || !request.GroupIds.Any())
        {
            return BadRequest(new { message = "Lista grup jest pusta." });
        }

        string? printerName = null;
        // Jeśli wybrano drukarza, upewnijmy się, że istnieje i ma rolę Printer lub jest trenerem z uprawnieniem drukarza
        if (request.PrinterId.HasValue)
        {
            var printerUser = await _context.Users.FindAsync(request.PrinterId.Value);
            if (printerUser == null || (printerUser.Role != UserRole.Printer && !(printerUser.Role == UserRole.Trainer && printerUser.CanActAsPrinter)))
            {
                return BadRequest(new { message = "Wybrany użytkownik nie istnieje lub nie posiada uprawnień Drukarza." });
            }
            printerName = $"{printerUser.FirstName} {printerUser.LastName}";
        }

        var groups = await _context.Groups
            .Where(g => request.GroupIds.Contains(g.Id))
            .ToListAsync();

        if (!groups.Any())
        {
            return NotFound(new { message = "Nie znaleziono podanych grup." });
        }

        // Sprawdzenie dostępu koordynatora do grup
        if (userRoleStr == "Coordinator")
        {
            foreach (var group in groups)
            {
                if (!await _accessService.CanAccessGroupAsync(currentUserId, group.Id))
                {
                    return Forbid();
                }
            }
        }

        foreach (var group in groups)
        {
            group.AssignedPrinterId = request.PrinterId;
        }

        await _context.SaveChangesAsync();

        await _auditLogService.LogAsync(
            "Groups",
            "BulkAssignPrinter",
            $"Przypisano drukarza: {(printerName ?? "Brak przypisania")} dla {groups.Count} grup",
            userId: currentUserId,
            userRole: userRoleStr,
            ipAddress: HttpContext.GetClientIpAddress());

        return Ok(new { count = groups.Count, message = $"Pomyślnie zaktualizowano przypisanego drukarza dla {groups.Count} grup." });
    }

    [HttpPost("{id:guid}/archive")]
    [Authorize(Roles = "Admin,Coordinator")]
    public async Task<IActionResult> ArchiveGroup(Guid id, [FromQuery] string academicYear)
    {
        if (string.IsNullOrWhiteSpace(academicYear))
        {
            return BadRequest(new { message = "Rok szkolny (academicYear) jest wymagany do archiwizacji." });
        }

        var currentUserId = GetCurrentUserId();
        var userRoleStr = User.FindFirstValue(ClaimTypes.Role);
        
        if (userRoleStr == "Coordinator")
        {
             if (!await _accessService.CanAccessGroupAsync(currentUserId, id)) return Forbid();
        }

        var group = await _context.Groups.FindAsync(id);
        
        if (group == null)
        {
            return NotFound(new {message = "Nie odnaleziono grupy o podanym ID."});
        }

        group.IsArchived = true;
        group.ArchivedAcademicYear = academicYear;
        
        // Remove students from this group since it's being archived? 
        // Typically manual archiving means the group is closed, so students should be moved to history.
        // Let's do that for consistency.
        var students = await _context.Students.Where(s => s.GroupId == id).ToListAsync();
        
        foreach (var student in students)
        {
            _context.StudentGroupHistories.Add(new StudentGroupHistory
            {
                Id = Guid.NewGuid(),
                StudentId = student.Id,
                GroupId = id,
                AcademicYear = academicYear,
                ArchivedAt = DateTime.UtcNow
            });
            student.GroupId = null; // wypisanie
        }

        await _context.SaveChangesAsync();

        await _auditLogService.LogAsync(
            "Groups",
            "ArchiveGroup",
            $"Zarchiwizowano grupę '{group.Name}' dla roku {academicYear}",
            entityId: group.Id,
            entityName: group.Name,
            userId: currentUserId,
            userRole: userRoleStr,
            ipAddress: HttpContext.GetClientIpAddress());
        
        return Ok(new { message = "Grupa została pomyślnie zarchiwizowana." });
    }
}