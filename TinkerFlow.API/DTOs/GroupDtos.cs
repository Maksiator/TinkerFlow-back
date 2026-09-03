namespace TinkerFlow.API.DTOs;

public record CreateGroupRequest(
    string Name,
    Guid BranchId,
    DayOfWeek? ClassDayOfWeek,
    Guid? PrimaryTrainerId = null
);

public record UpdateGroupRequest(
    string Name,
    Guid BranchId,
    DayOfWeek? ClassDayOfWeek,
    Guid? PrimaryTrainerId = null
);

public record GroupResponse(
    Guid Id,  
    string Name,
    Guid BranchId,
    string BranchName,
    Guid? PrimaryTrainerId,
    string? PrimaryTrainerName,
    int StudentCount,
    DayOfWeek? ClassDayOfWeek = null,
    bool IsArchived = false,
    string? ArchivedAcademicYear = null
);

public record BulkDeleteGroupsRequest(
    List<Guid> GroupIds
);

public record BulkChangeBranchRequest(
    List<Guid> GroupIds,
    Guid BranchId
);