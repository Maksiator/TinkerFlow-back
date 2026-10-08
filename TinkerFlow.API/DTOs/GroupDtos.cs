using TinkerFlow.Domain.Enums;

namespace TinkerFlow.API.DTOs;

public record CreateGroupRequest(
    string Name,
    Guid BranchId,
    DayOfWeek? ClassDayOfWeek,
    Guid? PrimaryTrainerId = null,
    Guid? AssignedPrinterId = null,
    GroupType Type = GroupType.Standard,
    string? TinkercadUrl = null
);

public record UpdateGroupRequest(
    string Name,
    Guid BranchId,
    DayOfWeek? ClassDayOfWeek,
    Guid? PrimaryTrainerId = null,
    Guid? AssignedPrinterId = null,
    GroupType Type = GroupType.Standard,
    string? TinkercadUrl = null
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
    string? ArchivedAcademicYear = null,
    Guid? AssignedPrinterId = null,
    string? AssignedPrinterName = null,
    GroupType Type = GroupType.Standard,
    string? TinkercadUrl = null
)
{
    public GroupResponse(
        Guid id,
        string name,
        Guid branchId,
        string branchName,
        Guid? primaryTrainerId,
        string? primaryTrainerName,
        int studentCount,
        DayOfWeek? classDayOfWeek,
        bool isArchived,
        string? archivedAcademicYear)
        : this(id, name, branchId, branchName, primaryTrainerId, primaryTrainerName, studentCount, classDayOfWeek, isArchived, archivedAcademicYear, null, null, GroupType.Standard, null)
    {
    }
}

public record BulkDeleteGroupsRequest(
    List<Guid> GroupIds
);

public record BulkChangeBranchRequest(
    List<Guid> GroupIds,
    Guid BranchId
);

public record BulkAssignPrinterRequest(
    List<Guid> GroupIds,
    Guid? PrinterId
);