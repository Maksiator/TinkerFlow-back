using TinkerFlow.Domain.Enums;

namespace TinkerFlow.API.DTOs;

public record CreateStudentRequest(
    string FirstName,
    string LastName,
    DateOnly DateOfBirth,
    SkillLevel Level,
    bool IsIndependent,
    bool NeedsAttention,
    Guid? GroupId
);  

public record UpdateStudentRequest(
    string FirstName,
    string LastName,
    DateOnly DateOfBirth,
    SkillLevel Level,
    bool IsIndependent,
    bool NeedsAttention,
    Guid? GroupId,
    bool RecordHistory = true,
    bool IsMidYear = true,
    string? AcademicYear = null
);

public record ChangeGroupRequest(
    Guid? GroupId,
    bool RecordHistory = true,
    bool IsMidYear = false,
    string? AcademicYear = null
);

public record StudentResponse(
    Guid Id,
    string FirstName,
    string LastName,
    DateOnly DateOfBirth,
    SkillLevel Level,
    bool IsIndependent,
    bool NeedsAttention,
    Guid? GroupId,
    string? GroupName
    
);