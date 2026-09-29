using System;
using System.Collections.Generic;
using TinkerFlow.API;
using TinkerFlow.Domain.Enums; // Upewnij się, że masz tu ścieżkę do enuma ProjectState

namespace TinkerFlow.API.DTOs;

// --- TWOJE OBECNE DTO (Zostają bez zmian) ---
public record CreateProjectRequest(
    string Name,
    string Code,
    int SequenceOrder,
    bool IsPractice = false,
    bool IsYearBoundary = false,
    ProjectSoftware Software = ProjectSoftware.Tinkercad,
    bool IsAdvanced = false
);

public record UpdateProjectRequest(
    string Name,
    string Code,
    int SequenceOrder,
    bool IsPractice = false,
    bool IsYearBoundary = false,
    ProjectSoftware Software = ProjectSoftware.Tinkercad,
    bool IsAdvanced = false
);

public record ProjectResponse(
    Guid Id,
    string Name,
    string Code,
    int SequenceOrder,
    bool IsPractice,
    bool IsYearBoundary,
    ProjectSoftware Software = ProjectSoftware.Tinkercad,
    bool IsAdvanced = false
);

// --- NOWE DTO SPECJALNIE DO "RENTGENA" ---
public record ProjectUsageStudentDto(
    Guid StudentId,
    string FullName,
    string GroupName,
    ProjectState Status 
);

public record ProjectUsageResponse(
    Guid Id,
    string Name,
    string Code,
    List<ProjectUsageStudentDto> Students
);

public record MergeProjectsRequest(
    Guid SourceProjectId, 
    Guid TargetProjectId
    );