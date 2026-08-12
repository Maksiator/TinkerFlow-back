using TinkerFlow.Domain.Enums;

namespace TinkerFlow.API.DTOs;


public record LoginRequest(string Email, string Password);
public record AuthResponse(string Token, Guid UserId, string FirstName, string LastName, UserRole Role, bool MustChangePassword);


// public record CreateUserRequest(string FirstName, string LastName, string Email, string Password, UserRole Role);
// public record UserResponse(Guid Id, string FirstName, string LastName, string Email, UserRole Role, bool IsActive);