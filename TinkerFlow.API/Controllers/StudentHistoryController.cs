using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TinkerFlow.Domain.Entities;
using TinkerFlow.Domain.Enums;
using TinkerFlow.Infrastructure;

namespace TinkerFlow.API.Controllers;

[ApiController]
[Route("api/students/{studentId}/history")]
[Authorize] // Otwarty dla wszystkich (Role sprawdzamy wewnątrz)
public class StudentHistoryController : ControllerBase
{
    private readonly TinkerFlowDbContext _context;
    private readonly Microsoft.AspNetCore.Identity.UserManager<User> _userManager;
    private readonly TinkerFlow.Infrastructure.Services.IGroupAccessService _accessService;

    public StudentHistoryController(
        TinkerFlowDbContext context, 
        Microsoft.AspNetCore.Identity.UserManager<User> userManager,
        TinkerFlow.Infrastructure.Services.IGroupAccessService accessService)
    {
        _context = context;
        _userManager = userManager;
        _accessService = accessService;
    }

    private async Task<bool> CanAccessStudent(Guid userId, UserRole role, Guid studentId)
    {
        if (role == UserRole.Admin) return true;

        var student = await _context.Students.Include(s => s.Group).FirstOrDefaultAsync(s => s.Id == studentId);
        if (student == null) return false;

        if (role == UserRole.Coordinator)
        {
            var branchIds = await _context.UserBranches.Where(ub => ub.UserId == userId).Select(ub => ub.BranchId).ToListAsync();
            return student.GroupId == null || branchIds.Contains(student.Group!.BranchId);
        }

        if (role == UserRole.Trainer)
        {
            if (student.GroupId == null) return false;
            return await _accessService.CanAccessGroupAsync(userId, student.GroupId.Value);
        }

        return false;
    }

    [HttpGet]
    public async Task<IActionResult> GetStudentHistory(Guid studentId)
    {
        var currentUserIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var currentUser = await _userManager.FindByIdAsync(currentUserIdStr!);
        
        if (!await CanAccessStudent(currentUser!.Id, currentUser.Role, studentId))
            return Forbid();
        var groupHistory = await _context.StudentGroupHistories
            .Include(h => h.Group)
            .Where(h => h.StudentId == studentId)
            .OrderByDescending(h => h.AcademicYear)
            .Select(h => new
            {
                h.Id,
                h.GroupId,
                GroupName = h.Group.Name,
                h.AcademicYear,
                h.ArchivedAt
            })
            .ToListAsync();

        var activeProjects = await _context.Set<StudentProject>()
            .Where(p => p.StudentId == studentId)
            .Join(_context.Set<Project>(), 
                sp => sp.ProjectId, 
                p => p.Id, 
                (sp, p) => new
                {
                    sp.Id,
                    sp.ProjectId,
                    ProjectName = p.Name,
                    sp.Status
                })
            .ToListAsync();

        return Ok(new
        {
            GroupHistory = groupHistory,
            ActiveProjects = activeProjects
        });
    }

    // Dodawanie ręczne wpisu do historii grup
    public class AddGroupHistoryRequest
    {
        public Guid GroupId { get; set; }
        public string AcademicYear { get; set; } = string.Empty;
    }

    [HttpPost("groups")]
    [Authorize(Roles = "Admin,Coordinator")] // Tylko Admin/Coordinator mogą modyfikować historię!
    public async Task<IActionResult> AddGroupHistory(Guid studentId, [FromBody] AddGroupHistoryRequest request)
    {
        var history = new StudentGroupHistory
        {
            Id = Guid.NewGuid(),
            StudentId = studentId,
            GroupId = request.GroupId,
            AcademicYear = request.AcademicYear,
            ArchivedAt = DateTime.UtcNow
        };

        _context.StudentGroupHistories.Add(history);
        await _context.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("groups/{historyId}")]
    [Authorize(Roles = "Admin,Coordinator")] // Tylko Admin/Coordinator
    public async Task<IActionResult> DeleteGroupHistory(Guid studentId, Guid historyId)
    {
        var history = await _context.StudentGroupHistories.FirstOrDefaultAsync(h => h.Id == historyId && h.StudentId == studentId);
        if (history != null)
        {
            _context.StudentGroupHistories.Remove(history);
            await _context.SaveChangesAsync();
        }
        return NoContent();
    }
}
