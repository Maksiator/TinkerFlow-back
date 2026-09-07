namespace TinkerFlow.API.DTOs;

public record AuditLogResponse(
    Guid Id,
    DateTime Timestamp,
    Guid? UserId,
    string? UserEmail,
    string? UserName,
    string? UserRole,
    string Action,
    string Category,
    Guid? EntityId,
    string? EntityName,
    string? Details,
    string? IpAddress
);

public record AuditLogListResponse(
    List<AuditLogResponse> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages
);

public record AuditLogFiltersResponse(
    List<string> Categories,
    List<string> Actions
);
