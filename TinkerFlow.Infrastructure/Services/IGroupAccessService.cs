using TinkerFlow.Domain.Entities;

namespace TinkerFlow.Infrastructure.Services;

public interface IGroupAccessService
{
    // Do sprawdzania dostępu do pojedynczej grupy (np. Matrix)
    Task<bool> CanAccessGroupAsync(Guid userId, Guid groupId);
    
    // Do pobierania odgórnie przefiltrowanej bazy grup
    Task<IQueryable<Group>> GetAccessibleGroupsQueryAsync(Guid userId);
}