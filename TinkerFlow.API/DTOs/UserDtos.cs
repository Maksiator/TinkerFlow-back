using System;
using System.Collections.Generic;

namespace TinkerFlow.API.DTOs;

public record CreateUserRequest(
    string FirstName,
    string LastName,
    string Email,
    string Password,
    TinkerFlow.Domain.Enums.UserRole Role,
    List<Guid> BranchIds, // NOWE: Lista oddziałów do przypisania przy tworzeniu
    bool? CanActAsTrainer = null,
    bool? CanActAsPrinter = null
);

public record UpdateUserRequest(
    string FirstName,
    string LastName,
    TinkerFlow.Domain.Enums.UserRole Role,
    List<Guid> BranchIds, // NOWE: Lista oddziałów po aktualizacji
    bool? CanActAsTrainer = null,
    string? Email = null,
    bool? CanActAsPrinter = null
);

public record UpdateProfileRequest(
    string FirstName,
    string LastName,
    bool? CanActAsTrainer = null
);

// NOWE: Małe DTO pomocnicze, żeby w UserResponse ładnie wyświetlać oddziały
public record UserBranchDto(
    Guid BranchId, 
    string BranchName
);

public record UserResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    TinkerFlow.Domain.Enums.UserRole Role,
    bool IsActive,
    List<UserBranchDto> Branches, // NOWE: Lista oddziałów użytkownika
    bool MustChangePassword,
    bool CanActAsTrainer = false,
    bool CanActAsPrinter = false
);

public record ChangePasswordRequest(
    string CurrentPassword,
    string NewPassword
);

public record ResetPasswordRequest(
    string NewPassword
);