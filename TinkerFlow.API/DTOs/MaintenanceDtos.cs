namespace TinkerFlow.API.DTOs;

public record OrphanItemDto(
    Guid Id,
    string EntityType,
    string Reason,
    string? AdditionalInfo
);

public record OrphanCategoryDto(
    string Key,
    string Name,
    string Description,
    int Count,
    List<OrphanItemDto> Items
);

public record OrphanReportResponse(
    int TotalOrphansCount,
    DateTime CheckedAt,
    List<OrphanCategoryDto> Categories
);

public record CleanupOrphansRequest(
    List<string>? Categories
);

public record CleanupOrphansResponse(
    bool Success,
    int TotalCleaned,
    Dictionary<string, int> CleanedByCategory,
    string Message
);
