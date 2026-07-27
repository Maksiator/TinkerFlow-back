namespace TinkerFlow.API.DTOs;

public record CreateTrainerListRequest(
    string Name,
    List<Guid> GroupIds,
    DayOfWeek? TargetDay
);

public record UpdateTrainerListRequest(
    string Name,
    List<Guid> GroupIds,
    DayOfWeek? TargetDay
);

public record TrainerListResponse(
    Guid Id,
    string Name,
    List<GroupResponse> Groups,
    DayOfWeek? TargetDay
);