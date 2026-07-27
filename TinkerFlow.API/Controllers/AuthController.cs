using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using TinkerFlow.API.DTOs;
using TinkerFlow.Domain.Entities;

namespace TinkerFlow.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly UserManager<User> _userManager;
    private readonly IConfiguration _configuration;

    public AuthController(UserManager<User> userManager, IConfiguration configuration)
    {
        _userManager = userManager;
        _configuration = configuration;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {

        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user == null)
        {
            return Unauthorized(new { message = "Nieprawidłowy email lub hasło." });
        }
        
        if (!user.IsActive)
        {
            return Unauthorized(new { message = "Twoje konto zostało zablokowane. Skontaktuj się z administratorem." });
        }
        
        var isPasswordValid = await _userManager.CheckPasswordAsync(user, request.Password);
        if (!isPasswordValid)
        {
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
        
        return Ok(new AuthResponse(token, user.Id, user.FirstName, user.LastName, user.Role));
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