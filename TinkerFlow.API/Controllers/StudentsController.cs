using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TinkerFlow.API.DTOs;
using TinkerFlow.Domain.Entities;
using TinkerFlow.Domain.Enums;
using TinkerFlow.Infrastructure;
using TinkerFlow.Infrastructure.Services; // Dodany namespace dla serwisu!

namespace TinkerFlow.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize] // ZMIANA 1: Każdy zalogowany wejdzie, rygor nakładamy na konkretne metody
public class StudentsController : ControllerBase
{
    private readonly TinkerFlowDbContext _context; 
    private readonly UserManager<User> _userManager; 
    private readonly IGroupAccessService _accessService; // ZMIANA 2: Wstrzyknięty serwis
    
    public StudentsController(TinkerFlowDbContext context, UserManager<User> userManager, IGroupAccessService accessService)
    {
        _context = context;
        _userManager = userManager;
        _accessService = accessService;
    }

    // POMOCNICZA METODA: Pobiera oddziały Koordynatora
    private async Task<List<Guid>> GetCoordinatorBranchIdsAsync(Guid userId)
    {
        return await _context.UserBranches
            .Where(ub => ub.UserId == userId)
            .Select(ub => ub.BranchId)
            .ToListAsync();
    }
    
    [HttpGet]
    [Authorize(Roles = "Admin,Coordinator")]
    public async Task<ActionResult<PagedResult<StudentResponse>>> GetStudents(
        [FromQuery] string? search,
        [FromQuery] string sortBy = "lastName",
        [FromQuery] string sortOrder = "asc",
        [FromQuery] int page = 1, 
        [FromQuery] int pageSize = 15)
    {
        var currentUserIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (currentUserIdStr == null) return Unauthorized();

        var currentUser = await _userManager.FindByIdAsync(currentUserIdStr);
        if (currentUser == null) return Unauthorized();

        var query = _context.Students
            .Include(s => s.Group)
            .AsNoTracking()
            .AsQueryable();

        var isSearching = !string.IsNullOrWhiteSpace(search);

        // LOGIKA DLA KOORDYNATORA:
        if (currentUser.Role == UserRole.Coordinator)
        {
            var branchIds = await GetCoordinatorBranchIdsAsync(currentUser.Id);
            if (!branchIds.Any()) return Ok(new PagedResult<StudentResponse>(new List<StudentResponse>(), 0, 0, page, pageSize));

            if (isSearching)
            {
                // TRYB WYSZUKIWANIA: Pokazujemy uczniów z oddziałów Koordynatora ORAZ wszystkich bez grupy (nieaktywnych)
                query = query.Where(s => (s.GroupId != null && branchIds.Contains(s.Group!.BranchId)) || s.GroupId == null);
            }
            else
            {
                // TRYB DOMYŚLNY: Pokazujemy TYLKO uczniów przypisanych do oddziałów Koordynatora
                query = query.Where(s => s.GroupId != null && branchIds.Contains(s.Group!.BranchId));
            }
        }

        if (isSearching)
        {
            var lowerSearch = search!.ToLower();
            query = query.Where(s => 
                s.FirstName.ToLower().Contains(lowerSearch) || 
                s.LastName.ToLower().Contains(lowerSearch) ||
                (s.FirstName + " " + s.LastName).ToLower().Contains(lowerSearch));
        }

        // Sortowanie
        bool isDesc = sortOrder.ToLower() == "desc";
        if (sortBy == "group")
        {
            query = isDesc
                ? query.OrderByDescending(s => s.Group != null ? s.Group.Name : string.Empty).ThenByDescending(s => s.LastName)
                : query.OrderBy(s => s.Group != null ? s.Group.Name : string.Empty).ThenBy(s => s.LastName);
        }
        else if (sortBy == "dateOfBirth")
        {
            query = isDesc
                ? query.OrderByDescending(s => s.DateOfBirth).ThenByDescending(s => s.LastName)
                : query.OrderBy(s => s.DateOfBirth).ThenBy(s => s.LastName);
        }
        else // domyślnie lastName
        {
            query = isDesc
                ? query.OrderByDescending(s => s.LastName).ThenByDescending(s => s.FirstName)
                : query.OrderBy(s => s.LastName).ThenBy(s => s.FirstName);
        }

        var totalCount = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        var students = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new StudentResponse(
                s.Id, s.FirstName, s.LastName, s.DateOfBirth, s.Level, 
                s.IsIndependent, s.NeedsAttention, s.GroupId, 
                s.Group != null ? s.Group.Name : null 
            ))
            .ToListAsync();

        return Ok(new PagedResult<StudentResponse>(students, totalCount, totalPages, page, pageSize));
    }

    // 2. POBIERZ PO GRUPIE
    [HttpGet("group/{groupId}")]
    // ZMIANA 4: BRAK [Authorize(Roles = ...)] -> TRENER MOŻE TU WEJŚĆ!
    public async Task<IActionResult> GetStudentsByGroup(Guid groupId)
    {
        var currentUserIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(currentUserIdStr, out var currentUserId)) return Unauthorized();

        // ZABEZPIECZENIE: Sprawdzamy, czy ten konkretny użytkownik ma dostęp do tej grupy (Matrix)
        var hasAccess = await _accessService.CanAccessGroupAsync(currentUserId, groupId);
        if (!hasAccess) return Forbid(); // Zablokuje Trenera tylko, jeśli próbuje pobrać uczniów z cudzej grupy

        var query = _context.Students
            .Include(s => s.Group)
            .AsNoTracking()
            .Where(s => s.GroupId == groupId);

        var students = await query
            .Select(s => new StudentResponse(
                s.Id, s.FirstName, s.LastName, s.DateOfBirth, s.Level, 
                s.IsIndependent, s.NeedsAttention, s.GroupId, 
                s.Group != null ? s.Group.Name : null
            ))
            .ToListAsync();

        return Ok(students);
    }

// 3. WYSZUKIWARKA (Do Dropdownów/Formularzy w edycji grupy)
    [HttpGet("search")]
    [Authorize(Roles = "Admin,Coordinator")]
    public async Task<IActionResult> SearchStudents([FromQuery(Name = "query")] string queryStr)
    {
        if (string.IsNullOrWhiteSpace(queryStr) || queryStr.Length < 3) return BadRequest();

        var currentUserIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var currentUser = await _userManager.FindByIdAsync(currentUserIdStr!);

        var searchTerm = queryStr.ToLower();
        var query = _context.Students
            .Include(s => s.Group)
            .AsNoTracking()
            .Where(s => s.FirstName.ToLower().Contains(searchTerm) || 
                        s.LastName.ToLower().Contains(searchTerm) ||
                        (s.FirstName + " " + s.LastName).ToLower().Contains(searchTerm));

        // ZABEZPIECZENIE DLA KOORDYNATORA:
        if (currentUser!.Role == UserRole.Coordinator)
        {
            var branchIds = await GetCoordinatorBranchIdsAsync(currentUser.Id);
            
            // Szukamy w przypisanych oddziałach ORAZ wśród uczniów bez grupy (Duchów)
            query = query.Where(s => (s.GroupId != null && branchIds.Contains(s.Group!.BranchId)) || s.GroupId == null);
        }

        var students = await query
            .Take(10)
            .Select(s => new StudentResponse(
                s.Id, s.FirstName, s.LastName, s.DateOfBirth, s.Level, 
                s.IsIndependent, s.NeedsAttention, s.GroupId, 
                s.Group != null ? s.Group.Name : null
            ))
            .ToListAsync();

        return Ok(students);
    }
    [HttpGet("{id}")]
    [Authorize] // Dostęp dla wszystkich zalogowanych (Admin, Koordynator, Trener)
    public async Task<IActionResult> GetStudent(Guid id)
    {
        var currentUserIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var currentUser = await _userManager.FindByIdAsync(currentUserIdStr!);

        var query = _context.Students
            .Include(s => s.Group)
            .AsNoTracking()
            .Where(s => s.Id == id);

        var student = await query.FirstOrDefaultAsync();
        if (student == null) return NotFound(new { message = "Nie znaleziono ucznia." });

        // ZABEZPIECZENIE: Koordynator lub Trener
        if (currentUser!.Role == UserRole.Coordinator || currentUser.Role == UserRole.Trainer)
        {
            if (currentUser.Role == UserRole.Coordinator)
            {
                var branchIds = await GetCoordinatorBranchIdsAsync(currentUser.Id);
                if (student.GroupId.HasValue && !branchIds.Contains(student.Group!.BranchId))
                    return Forbid();
            }
            else if (currentUser.Role == UserRole.Trainer)
            {
                if (!student.GroupId.HasValue) return Forbid(); // Trener nie widzi duchów
                var hasAccess = await _accessService.CanAccessGroupAsync(currentUser.Id, student.GroupId.Value);
                if (!hasAccess) return Forbid(); // Trener nie widzi uczniów z cudzych grup
            }
        }

        var response = new StudentResponse(
            student.Id, student.FirstName, student.LastName, student.DateOfBirth, student.Level, 
            student.IsIndependent, student.NeedsAttention, student.GroupId, 
            student.Group?.Name
        );

        return Ok(response);
    }
    
    // 5. STWÓRZ
    [HttpPost]
    [Authorize(Roles = "Admin,Coordinator")]
    public async Task<IActionResult> CreateStudent([FromBody] CreateStudentRequest request)
    {
        var currentUserIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (currentUserIdStr == null) return Unauthorized();
        var currentUser = await _userManager.FindByIdAsync(currentUserIdStr);
        if (currentUser == null) return Unauthorized();

        if (currentUser.Role == UserRole.Coordinator && request.GroupId.HasValue)
        {
            var hasAccess = await _accessService.CanAccessGroupAsync(currentUser.Id, request.GroupId.Value);
            if (!hasAccess) return Forbid();
        }

        var newStudent = new Student
        {
            Id = Guid.NewGuid(),
            FirstName = request.FirstName,
            LastName = request.LastName,
            DateOfBirth = request.DateOfBirth,
            Level = request.Level,
            IsIndependent = request.IsIndependent,
            NeedsAttention = request.NeedsAttention,
            GroupId = request.GroupId
        };

        _context.Students.Add(newStudent);
        await _context.SaveChangesAsync();

        var groupName = request.GroupId.HasValue
            ? await _context.Groups.Where(g => g.Id == request.GroupId.Value).Select(g => g.Name).FirstOrDefaultAsync()
            : null;
        
        var response = new StudentResponse(
            newStudent.Id, newStudent.FirstName, newStudent.LastName, newStudent.DateOfBirth,
            newStudent.Level, newStudent.IsIndependent, newStudent.NeedsAttention,
            newStudent.GroupId, groupName
        );
        return CreatedAtAction(nameof(GetStudent), new { id = newStudent.Id }, response);
    }
    
    // 6. AKTUALIZUJ
    [HttpPut("{id}")]
    [Authorize(Roles = "Admin,Coordinator")]
    public async Task<IActionResult> UpdateStudent(Guid id, [FromBody] UpdateStudentRequest request)
    {
        var currentUserIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (currentUserIdStr == null) return Unauthorized();
        var currentUser = await _userManager.FindByIdAsync(currentUserIdStr);
        if (currentUser == null) return Unauthorized();

        var existingStudent = await _context.Students.FindAsync(id);
        if (existingStudent == null) return NotFound(new { message = "Nie odnaleziono ucznia o podanym ID." });

        if (currentUser.Role == UserRole.Coordinator)
        {
            if (existingStudent.GroupId.HasValue && !await _accessService.CanAccessGroupAsync(currentUser.Id, existingStudent.GroupId.Value))
            {
                return Forbid();
            }
            if (request.GroupId.HasValue && !await _accessService.CanAccessGroupAsync(currentUser.Id, request.GroupId.Value))
            {
                return Forbid();
            }
        }

        if (request.RecordHistory && existingStudent.GroupId.HasValue && existingStudent.GroupId.Value != request.GroupId)
        {
            _context.StudentGroupHistories.Add(new StudentGroupHistory
            {
                Id = Guid.NewGuid(),
                StudentId = existingStudent.Id,
                GroupId = existingStudent.GroupId.Value,
                AcademicYear = !string.IsNullOrWhiteSpace(request.AcademicYear) ? request.AcademicYear : "Wypisano w trakcie roku",
                ArchivedAt = DateTime.UtcNow,
                IsMidYear = request.IsMidYear
            });
        }

        existingStudent.FirstName = request.FirstName;
        existingStudent.LastName = request.LastName;
        existingStudent.DateOfBirth = request.DateOfBirth;
        existingStudent.Level = request.Level;
        existingStudent.IsIndependent = request.IsIndependent;
        existingStudent.NeedsAttention = request.NeedsAttention;
        existingStudent.GroupId = request.GroupId;
        
        await _context.SaveChangesAsync();
        return NoContent();
    }
    
    // 7. SZYBKA ZMIANA GRUPY
    [HttpPatch("{id}/group")]
    [Authorize(Roles = "Admin,Coordinator")]
    public async Task<IActionResult> ChangeStudentGroup(Guid id, [FromBody] ChangeGroupRequest request)
    {
        var currentUserIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (currentUserIdStr == null) return Unauthorized();
        var currentUser = await _userManager.FindByIdAsync(currentUserIdStr);
        if (currentUser == null) return Unauthorized();

        var student = await _context.Students.FindAsync(id);
        if (student == null) return NotFound(new { message = "Nie odnaleziono ucznia." });

        if (currentUser.Role == UserRole.Coordinator)
        {
            if (student.GroupId.HasValue && !await _accessService.CanAccessGroupAsync(currentUser.Id, student.GroupId.Value))
            {
                return Forbid();
            }
            if (request.GroupId.HasValue && !await _accessService.CanAccessGroupAsync(currentUser.Id, request.GroupId.Value))
            {
                return Forbid();
            }
        }

        if (request.RecordHistory && student.GroupId.HasValue && student.GroupId.Value != request.GroupId)
        {
            _context.StudentGroupHistories.Add(new StudentGroupHistory
            {
                Id = Guid.NewGuid(),
                StudentId = student.Id,
                GroupId = student.GroupId.Value,
                AcademicYear = !string.IsNullOrWhiteSpace(request.AcademicYear) ? request.AcademicYear : "Wypisano w trakcie roku",
                ArchivedAt = DateTime.UtcNow,
                IsMidYear = request.IsMidYear
            });
        }

        student.GroupId = request.GroupId; 
        await _context.SaveChangesAsync();
        return NoContent();
    }
    
    // 8. USUŃ
    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin,Coordinator")]
    public async Task<IActionResult> DeleteStudent(Guid id)
    {
        var currentUserIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (currentUserIdStr == null) return Unauthorized();
        var currentUser = await _userManager.FindByIdAsync(currentUserIdStr);
        if (currentUser == null) return Unauthorized();

        var student = await _context.Students.FindAsync(id);
        if (student == null) return NotFound(new { message = "Nie odnaleziono ucznia o podanym ID." });

        if (currentUser.Role == UserRole.Coordinator && student.GroupId.HasValue)
        {
            if (!await _accessService.CanAccessGroupAsync(currentUser.Id, student.GroupId.Value))
            {
                return Forbid();
            }
        }

        if (student.GroupId.HasValue)
        {
            _context.StudentGroupHistories.Add(new StudentGroupHistory
            {
                Id = Guid.NewGuid(),
                StudentId = student.Id,
                GroupId = student.GroupId.Value,
                AcademicYear = "Usunięto ucznia z systemu",
                ArchivedAt = DateTime.UtcNow,
                IsMidYear = true
            });
        }

        _context.Students.Remove(student);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("group/{groupId}/history")]
    [Authorize]
    public async Task<IActionResult> GetGroupStudentHistory(Guid groupId)
    {
        var currentUserIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(currentUserIdStr, out var currentUserId)) return Unauthorized();

        var hasAccess = await _accessService.CanAccessGroupAsync(currentUserId, groupId);
        if (!hasAccess) return Forbid();

        var history = await _context.StudentGroupHistories
            .Include(h => h.Student)
            .Where(h => h.GroupId == groupId)
            .OrderByDescending(h => h.ArchivedAt)
            .Select(h => new {
                StudentId = h.StudentId,
                FirstName = h.Student != null ? h.Student.FirstName : "Uczeń usunięty",
                LastName = h.Student != null ? h.Student.LastName : "",
                DateOfBirth = h.Student != null ? h.Student.DateOfBirth.ToString("yyyy-MM-dd") : "",
                ArchivedAt = h.ArchivedAt,
                AcademicYear = h.AcademicYear,
                IsMidYear = h.IsMidYear
            })
            .ToListAsync();

        return Ok(history);
    }
    
    // 9. BULK
    [HttpPost("bulk")]
    [Authorize(Roles = "Admin,Coordinator")]
    public async Task<IActionResult> CreateStudentsBulk([FromBody] List<CreateStudentRequest> requests)
    {
        var currentUserIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (currentUserIdStr == null) return Unauthorized();
        var currentUser = await _userManager.FindByIdAsync(currentUserIdStr);
        if (currentUser == null) return Unauthorized();

        if (!requests.Any()) return BadRequest(new { message = "Lista jest pusta." });

        var groupIds = requests.Where(r => r.GroupId.HasValue).Select(r => r.GroupId!.Value).Distinct().ToList();

        if (currentUser.Role == UserRole.Coordinator)
        {
            foreach (var groupId in groupIds)
            {
                if (!await _accessService.CanAccessGroupAsync(currentUser.Id, groupId))
                {
                    return Forbid();
                }
            }
        }

        var existingStudents = await _context.Students
            .Where(s => s.GroupId != null && groupIds.Contains(s.GroupId.Value))
            .ToListAsync();

        var newStudents = new List<Student>();

        foreach (var req in requests)
        {
            var exists = existingStudents.Any(s => 
                s.FirstName.ToLower() == req.FirstName.Trim().ToLower() && 
                s.LastName.ToLower() == req.LastName.Trim().ToLower() &&
                s.GroupId == req.GroupId);

            if (!exists)
            {
                newStudents.Add(new Student
                {
                    Id = Guid.NewGuid(),
                    FirstName = req.FirstName.Trim(),
                    LastName = req.LastName.Trim(),
                    DateOfBirth = req.DateOfBirth,
                    Level = req.Level, 
                    IsIndependent = req.IsIndependent,
                    NeedsAttention = req.NeedsAttention,
                    GroupId = req.GroupId
                });
            }
        }

        if (newStudents.Any())
        {
            _context.Students.AddRange(newStudents);
            await _context.SaveChangesAsync();
        }

        return Ok(new { message = $"Pomyślnie dodano {newStudents.Count} nowych uczniów. (Pominięto duplikaty, jeśli były)." });
    }
}