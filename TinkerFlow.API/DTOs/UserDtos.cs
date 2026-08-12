using System;
using System.Collections.Generic;

namespace TinkerFlow.API.DTOs;

public record CreateUserRequest(
    string FirstName,
    string LastName,
    string Email,
    string Password,
    TinkerFlow.Domain.Enums.UserRole Role,
    List<Guid> BranchIds // NOWE: Lista oddziałów do przypisania przy tworzeniu
);

public record UpdateUserRequest(
    string FirstName,
    string LastName,
    TinkerFlow.Domain.Enums.UserRole Role,
    List<Guid> BranchIds // NOWE: Lista oddziałów po aktualizacji
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
    bool MustChangePassword
);

public record ChangePasswordRequest(
    string CurrentPassword,
    string NewPassword
);

public record ResetPasswordRequest(
    string NewPassword
);