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
    int GroupCount, // Liczba grup w oddziale
    int TrainersCount = 0 // Liczba trenerów przypisanych do oddziału
);