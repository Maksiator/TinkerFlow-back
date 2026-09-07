using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using TinkerFlow.API.DTOs;
using TinkerFlow.API.Extensions;
using TinkerFlow.Domain.Entities;

namespace TinkerFlow.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly UserManager<User> _userManager;
    private readonly IConfiguration _configuration;
    private readonly TinkerFlow.Infrastructure.Services.IAuditLogService _auditLogService;

    public AuthController(
        UserManager<User> userManager,
        IConfiguration configuration,
        TinkerFlow.Infrastructure.Services.IAuditLogService auditLogService)
    {
        _userManager = userManager;
        _configuration = configuration;
        _auditLogService = auditLogService;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var ipAddress = HttpContext.GetClientIpAddress();

        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user == null)
        {
            await _auditLogService.LogAsync("Auth", "LoginFailed", $"Nieudana próba logowania (nieistniejący email: {request.Email})", userEmail: request.Email, ipAddress: ipAddress);
            return Unauthorized(new { message = "Nieprawidłowy email lub hasło." });
        }
        
        if (!user.IsActive)
        {
            await _auditLogService.LogAsync("Auth", "LoginBlocked", "Próba logowania na zablokowane konto", userId: user.Id, userEmail: user.Email, userName: $"{user.FirstName} {user.LastName}", userRole: user.Role.ToString(), ipAddress: ipAddress);
            return Unauthorized(new { message = "Twoje konto zostało zablokowane. Skontaktuj się z administratorem." });
        }
        
        var isPasswordValid = await _userManager.CheckPasswordAsync(user, request.Password);
        if (!isPasswordValid)
        {
            await _auditLogService.LogAsync("Auth", "LoginFailed", "Błędne hasło podczas logowania", userId: user.Id, userEmail: user.Email, userName: $"{user.FirstName} {user.LastName}", userRole: user.Role.ToString(), ipAddress: ipAddress);
            return Unauthorized(new { message = "Nieprawidłowy email lub hasło." });
        }
        
        var token = GenerateJwtToken(user);
        
        var cookieSecure = _configuration.GetValue<bool>("Jwt:CookieSecure");
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Expires = DateTime.UtcNow.AddDays(7),
            Secure = cookieSecure
        };
        Response.Cookies.Append("tinkerflow_token", token, cookieOptions);

        await _auditLogService.LogAsync("Auth", "LoginSuccess", "Pomyślne logowanie do systemu", userId: user.Id, userEmail: user.Email, userName: $"{user.FirstName} {user.LastName}", userRole: user.Role.ToString(), ipAddress: ipAddress);
        
        return Ok(new AuthResponse(token, user.Id, user.FirstName, user.LastName, user.Role, user.MustChangePassword));
    }

    private string GenerateJwtToken(User user)
    {
        
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email!),
            new Claim(ClaimTypes.Role, user.Role.ToString()) 
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddDays(7),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
    [HttpPost("logout")]
    public IActionResult Logout()
    {
        // Ta linijka nakazuje przeglądarce nadpisać ciastko z datą wygaśnięcia w przeszłości, co je usuwa.
        Response.Cookies.Delete("tinkerflow_token");
        return Ok(new { message = "Wylogowano pomyślnie" });
    }
}