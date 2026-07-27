namespace TinkerFlow.API.DTOs;

public record CreateSubstituteRequest(
    Guid GroupId,
    Guid TrainerId,
    DateTime LessonDate,
    DateTime? ValidFrom = null,  // DODANE: Opcjonalne nadpisanie
    DateTime? ValidUntil = null  // DODANE: Opcjonalne nadpisanie
);

public record SubstituteResponse(
    Guid Id,
    Guid GroupId,
    string GroupName,
    Guid TrainerId,
    string TrainerName,
    DateTime LessonDate,
    DateTime ValidFrom,
    DateTime ValidUntil
);