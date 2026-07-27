using Microsoft.EntityFrameworkCore;
using TinkerFlow.Domain.Entities;
using TinkerFlow.Domain.Enums;
using TinkerFlow.Infrastructure;

namespace TinkerFlow.Infrastructure.Services;

public class GroupAccessService : IGroupAccessService
{
    private readonly TinkerFlowDbContext _context;

    public GroupAccessService(TinkerFlowDbContext context)
    {
        _context = context;
    }

    // ZWRACA ZAPYTANIE ZAWIERAJĄCE TYLKO DOZWOLONE GRUPY
    public async Task<IQueryable<Group>> GetAccessibleGroupsQueryAsync(Guid userId)
    {
        var user = await _context.Users.FindAsync(userId);
        var query = _context.Groups.AsQueryable();

        // Brak użytkownika = brak dostępu do niczego
        if (user == null) return query.Where(g => false); 

        // Admin widzi absolutnie wszystko
        if (user.Role == UserRole.Admin) return query; 

        // Koordynator widzi tylko grupy przypisane do swoich oddziałów
        if (user.Role == UserRole.Coordinator)
        {
            var branchIds = await _context.UserBranches
                .Where(ub => ub.UserId == userId)
                .Select(ub => ub.BranchId)
                .ToListAsync();

            return query.Where(g => branchIds.Contains(g.BranchId));
        }

        // Trener widzi swoje własne grupy ORAZ te, na których jest aktywnym zastępcą
        var today = DateTime.UtcNow.Date;
        return query.Where(g => 
            g.PrimaryTrainerId == userId || 
            g.Substitutes.Any(s => 
                s.SubstituteTrainerId == userId && 
                s.ValidFrom.Date <= today && 
                s.ValidUntil.Date >= today));
    }

    // SPRAWDZENIE POJEDYNCZEJ GRUPY - teraz to 2 linijki!
    public async Task<bool> CanAccessGroupAsync(Guid userId, Guid groupId)
    {
        var accessibleGroups = await GetAccessibleGroupsQueryAsync(userId);
        return await accessibleGroups.AnyAsync(g => g.Id == groupId);
    }
}