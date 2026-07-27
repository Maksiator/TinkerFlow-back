using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TinkerFlow.API.DTOs;
using TinkerFlow.Infrastructure;
using TinkerFlow.Domain.Entities;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace TinkerFlow.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TrainerListsController : ControllerBase
{
    private readonly TinkerFlowDbContext _context;

    public TrainerListsController(TinkerFlowDbContext context)
    {
        _context = context;
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
    
    [HttpGet]
    public async Task<IActionResult> GetMyLists()
    {
        var currentTrainerId = GetCurrentUserId();

        var lists = await _context.TrainerGroupLists
            .AsNoTracking()
            .Where(l => l.TrainerId == currentTrainerId) 
            .Include(l => l.ListItems)
                .ThenInclude(li => li.Group)
                    .ThenInclude(g => g.Branch) 
            .Include(l => l.ListItems)
                .ThenInclude(li => li.Group)
                    .ThenInclude(g => g.PrimaryTrainer) 
            .OrderBy(l => l.TargetDay.HasValue ? 0 : 1)
            .ThenBy(l => l.TargetDay)
            .ThenBy(l => l.Name)
            .Select(l => new TrainerListResponse(
                l.Id,
                l.Name,
                // Sortujemy po indeksie i mapujemy z nowej tabeli łączącej
                l.ListItems.OrderBy(li => li.OrderIndex).Select(li => new GroupResponse(
                    li.Group.Id, 
                    li.Group.Name, 
                    li.Group.BranchId,
                    li.Group.Branch.Name,
                    li.Group.PrimaryTrainerId,
                    li.Group.PrimaryTrainer != null ? li.Group.PrimaryTrainer.FirstName + " " + li.Group.PrimaryTrainer.LastName : null,
                    li.Group.Students.Count(),
                    li.Group.ClassDayOfWeek,
                    li.Group.IsArchived,
                    li.Group.ArchivedAcademicYear
                )).ToList(),
                l.TargetDay
            ))
            .ToListAsync();

        return Ok(lists);
    }

    [HttpPost]
    public async Task<IActionResult> CreateList([FromBody] CreateTrainerListRequest request)
    {
        var currentTrainerId = GetCurrentUserId();
        
        var newList = new TrainerGroupList
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            TrainerId = currentTrainerId,
            TargetDay = request.TargetDay,
            // Używamy POPRAWNEJ nazwy encji: TrainerGroupListItem
            ListItems = request.GroupIds.Select((groupId, index) => new TrainerGroupListItem
            {
                GroupId = groupId,
                OrderIndex = index
            }).ToList()
        };

        _context.TrainerGroupLists.Add(newList);
        await _context.SaveChangesAsync();

        return Ok(); 
    }
    
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateList(Guid id, [FromBody] UpdateTrainerListRequest request)
    {
        var currentTrainerId = GetCurrentUserId();

        var existingList = await _context.TrainerGroupLists
            .Include(l => l.ListItems)
            .FirstOrDefaultAsync(l => l.Id == id);

        if (existingList == null) return NotFound();

        if (existingList.TrainerId != currentTrainerId)
        {
            return Forbid(); 
        }

        existingList.Name = request.Name;
        existingList.TargetDay = request.TargetDay;

        // Czyścimy stare powiązania
        _context.RemoveRange(existingList.ListItems);
        
        // Zapisujemy nowe, z nową kolejnością
        existingList.ListItems = request.GroupIds.Select((groupId, index) => new TrainerGroupListItem
        {
            GroupId = groupId,
            OrderIndex = index
        }).ToList();

        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteList(Guid id)
    {
        var currentTrainerId = GetCurrentUserId();

        var list = await _context.TrainerGroupLists
            .Include(l => l.ListItems) 
            .FirstOrDefaultAsync(l => l.Id == id);

        if (list == null) return NotFound();

        if (list.TrainerId != currentTrainerId)
        {
            return Forbid(); 
        }

        _context.TrainerGroupLists.Remove(list);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}