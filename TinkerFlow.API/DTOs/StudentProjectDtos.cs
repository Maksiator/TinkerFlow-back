using TinkerFlow.Domain.Enums;

namespace TinkerFlow.API.DTOs;

public record UpsertProjectStatusRequest(
    Guid StudentId,
    Guid ProjectId,
    ProjectState? Status
);