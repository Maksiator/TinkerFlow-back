namespace TinkerFlow.API.DTOs;

public record PagedResult<T>(
    IEnumerable<T> Items,
    int TotalCount,
    int TotalPages,
    int CurrentPage,
    int PageSize
);