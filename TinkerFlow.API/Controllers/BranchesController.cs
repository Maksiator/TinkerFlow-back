using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TinkerFlow.API.DTOs;
using TinkerFlow.Domain.Entities;
using TinkerFlow.Infrastructure;

namespace TinkerFlow.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Coordinator")] // Tylko dla szefa szefów
public class BranchesController : ControllerBase
{
    private readonly TinkerFlowDbContext _context;

    public BranchesController(TinkerFlowDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetBranches()
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdString, out var userId)) return Unauthorized();

        var query = _context.Branches.AsNoTracking().AsQueryable();

        if (User.IsInRole("Coordinator"))
        {
            query = query.Where(b => _context.UserBranches.Any(ub => ub.UserId == userId && ub.BranchId == b.Id));
        }

        var branches = await query
            .OrderBy(b => b.Name)
            .Select(b => new BranchResponse(
                b.Id,
                b.Name,
                b.Groups.Count() // Od razu liczymy, ile grup ma oddział
            ))
            .ToListAsync();

        return Ok(branches);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetBranch(Guid id)
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdString, out var userId)) return Unauthorized();

        if (User.IsInRole("Coordinator"))
        {
            var hasAccess = await _context.UserBranches.AnyAsync(ub => ub.UserId == userId && ub.BranchId == id);
            if (!hasAccess) return Forbid();
        }

        var branch = await _context.Branches
            .AsNoTracking()
            .Where(b => b.Id == id)
            .Select(b => new BranchResponse(
                b.Id,
                b.Name,
                b.Groups.Count()
            ))
            .FirstOrDefaultAsync();

        if (branch == null)
            return NotFound(new { message = "Nie znaleziono oddziału o podanym ID." });

        return Ok(branch);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> CreateBranch([FromBody] CreateBranchRequest request)
    {
        var branch = new Branch
        {
            Id = Guid.NewGuid(),
            Name = request.Name
        };

        _context.Branches.Add(branch);
        await _context.SaveChangesAsync();

        // Zwracamy stworzony obiekt, GroupCount na starcie to oczywiście 0
        return CreatedAtAction(nameof(GetBranch), new { id = branch.Id }, new BranchResponse(branch.Id, branch.Name, 0));
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateBranch(Guid id, [FromBody] UpdateBranchRequest request)
    {
        var branch = await _context.Branches.FindAsync(id);

        if (branch == null)
            return NotFound(new { message = "Nie znaleziono oddziału o podanym ID." });

        branch.Name = request.Name;

        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteBranch(Guid id)
    {
        // Musimy zaciągnąć informację o grupach, żeby zweryfikować czy możemy usunąć
        var branch = await _context.Branches
            .Include(b => b.Groups)
            .FirstOrDefaultAsync(b => b.Id == id);

        if (branch == null)
            return NotFound(new { message = "Nie znaleziono oddziału o podanym ID." });

        // TWARDA ZASADA: Zanim usuniesz oddział, musisz przenieść lub usunąć jego grupy
        if (branch.Groups.Any())
            return BadRequest(new { message = $"Nie można usunąć oddziału. Zostało w nim {branch.Groups.Count} grup. Najpierw przenieś je do innego oddziału." });

        _context.Branches.Remove(branch);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}