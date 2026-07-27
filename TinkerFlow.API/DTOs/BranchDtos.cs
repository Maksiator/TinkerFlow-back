namespace TinkerFlow.API.DTOs;

public record CreateBranchRequest(
    string Name
);

public record UpdateBranchRequest(
    string Name
);

public record BranchResponse(
    Guid Id,
    string Name,
    int GroupCount // Fajny bonus dla Admina, żeby widział wielkość oddziału
);