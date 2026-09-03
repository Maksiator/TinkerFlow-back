using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TinkerFlow.API.DTOs;
using TinkerFlow.Domain.Entities;
using TinkerFlow.Domain.Enums;
using TinkerFlow.Infrastructure; // Potrzebne do TinkerFlowDbContext

namespace TinkerFlow.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize] 
public class UsersController : ControllerBase
{
    private readonly UserManager<User> _userManager;
    private readonly TinkerFlowDbContext _context; // NOWE: Wstrzykujemy kontekst bazy

    public UsersController(UserManager<User> userManager, TinkerFlowDbContext context)
    {
        _userManager = userManager;
        _context = context;
    }

    [HttpGet]
[Authorize(Roles = "Admin,Coordinator")]
public async Task<IActionResult> GetUsers(
    [FromQuery] string? search,
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 15)
{
    var currentUserIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    if (currentUserIdStr == null) return Unauthorized();

    var currentUser = await _userManager.FindByIdAsync(currentUserIdStr);
    if (currentUser == null) return Unauthorized();

    var usersQuery = _userManager.Users
        .AsNoTracking()
        .Include(u => u.UserBranches)
        .ThenInclude(ub => ub.Branch)
        .AsQueryable();

    if (currentUser.Role == UserRole.Coordinator)
    {
        var coordinatorBranchIds = await _context.UserBranches
            .Where(ub => ub.UserId == currentUser.Id)
            .Select(ub => ub.BranchId)
            .ToListAsync();

        if (!coordinatorBranchIds.Any())
        {
            return Ok(new PagedResult<UserResponse>(new List<UserResponse>(), 0, 0, page, pageSize));
        }

        usersQuery = usersQuery.Where(u => 
            u.Id == currentUser.Id || 
            u.UserBranches.Any(ub => coordinatorBranchIds.Contains(ub.BranchId))
        );
    }

    if (!string.IsNullOrWhiteSpace(search))
    {
        var lowerSearch = search.ToLower();
        usersQuery = usersQuery.Where(u => 
            u.FirstName.ToLower().Contains(lowerSearch) || 
            u.LastName.ToLower().Contains(lowerSearch) ||
            (u.FirstName + " " + u.LastName).ToLower().Contains(lowerSearch) ||
            u.Email!.ToLower().Contains(lowerSearch));
    }

    usersQuery = usersQuery.OrderBy(u => u.LastName).ThenBy(u => u.FirstName);

    var totalCount = await usersQuery.CountAsync();
    var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

    var users = await usersQuery
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync();

    var result = users.Select(u => new UserResponse(
        u.Id,
        u.FirstName,
        u.LastName,
        u.Email ?? string.Empty,
        u.Role,
        u.IsActive,
        u.UserBranches.Select(ub => new UserBranchDto(
            ub.BranchId,
            ub.Branch.Name
        )).ToList(),
        u.MustChangePassword
    )).ToList();

    return Ok(new PagedResult<UserResponse>(result, totalCount, totalPages, page, pageSize));
}

    [HttpPost]
    [Authorize(Roles = "Admin,Coordinator")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
    {
        var currentUserIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (currentUserIdStr == null) return Unauthorized();
        var currentUser = await _userManager.FindByIdAsync(currentUserIdStr);
        if (currentUser == null) return Unauthorized();

        if (currentUser.Role == UserRole.Coordinator)
        {
            if (request.Role == UserRole.Admin || request.Role == UserRole.Coordinator)
            {
                return BadRequest(new { message = "Koordynator nie ma uprawnień do nadawania tej roli." });
            }

            var coordinatorBranchIds = await _context.UserBranches
                .Where(ub => ub.UserId == currentUser.Id)
                .Select(ub => ub.BranchId)
                .ToListAsync();

            if (request.BranchIds.Any(id => !coordinatorBranchIds.Contains(id)))
            {
                return Forbid();
            }
        }

        var user = new User
        {
            UserName = request.Email,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            Role = request.Role,
            IsActive = true,
            MustChangePassword = true
        };

        var result = await _userManager.CreateAsync(user, request.Password);

        if (!result.Succeeded)
        {
            return BadRequest(result.Errors);
        }

        // Przypisywanie do oddziałów (tylko Trener i Koordynator)
        var assignedBranches = new List<UserBranchDto>();
        if (request.Role != UserRole.Printer && request.Role != UserRole.Admin && request.BranchIds != null && request.BranchIds.Any())
        {
            // Pobieramy nazwy oddziałów z bazy, żeby od razu poprawnie zbudować obiekt Response
            var branchesFromDb = await _context.Branches
                .Where(b => request.BranchIds.Contains(b.Id))
                .ToListAsync();

            foreach (var branch in branchesFromDb)
            {
                _context.UserBranches.Add(new UserBranch { UserId = user.Id, BranchId = branch.Id });
                assignedBranches.Add(new UserBranchDto(branch.Id, branch.Name));
            }
            await _context.SaveChangesAsync();
        }

        return Ok(new UserResponse(user.Id, user.FirstName, user.LastName, user.Email, user.Role, user.IsActive, assignedBranches, user.MustChangePassword));
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Admin,Coordinator")]
    public async Task<IActionResult> UpdateUser(Guid id, [FromBody] UpdateUserRequest request)
    {
        var currentUserIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (currentUserIdStr == null) return Unauthorized();
        var currentUser = await _userManager.FindByIdAsync(currentUserIdStr);
        if (currentUser == null) return Unauthorized();

        // Musimy zaciągnąć usera od razu z jego oddziałami, żeby móc je zedytować
        var user = await _userManager.Users
            .Include(u => u.UserBranches)
            .FirstOrDefaultAsync(u => u.Id == id);
        
        if (user == null)
        {
            return NotFound(new { message = "Nie znaleziono użytkownika o podanym ID." });
        }

        if (currentUser.Role == UserRole.Coordinator)
        {
            if (user.Role == UserRole.Admin || user.Role == UserRole.Coordinator)
            {
                return BadRequest(new { message = "Koordynator nie może edytować konta administratora ani innego koordynatora." });
            }

            if (request.Role == UserRole.Admin || request.Role == UserRole.Coordinator)
            {
                return BadRequest(new { message = "Koordynator nie może przypisać tej roli." });
            }

            var coordinatorBranchIds = await _context.UserBranches
                .Where(ub => ub.UserId == currentUser.Id)
                .Select(ub => ub.BranchId)
                .ToListAsync();

            // ZABEZPIECZENIE: Koordynator może edytować tylko użytkowników z przypisanych do siebie oddziałów (lub samego siebie)
            var sharesBranch = user.UserBranches.Any(ub => coordinatorBranchIds.Contains(ub.BranchId)) || user.Id == currentUser.Id;
            if (!sharesBranch)
            {
                return Forbid();
            }

            if (request.BranchIds.Any(id => !coordinatorBranchIds.Contains(id)))
            {
                return Forbid();
            }
        }

        user.FirstName = request.FirstName;
        user.LastName = request.LastName;
        user.Role = request.Role;

        var result = await _userManager.UpdateAsync(user);

        if (!result.Succeeded)
        {
            return BadRequest(result.Errors);
        }

        // Aktualizacja oddziałów:
        // 1. Zawsze czyścimy dotychczasowe oddziały usera (pozwala to na wyzerowanie/odznaczenie wszystkich)
        _context.UserBranches.RemoveRange(user.UserBranches);

        // 2. Tylko Trener i Koordynator posiadają przypisane oddziały (Drukarz i Admin nie)
        if (request.Role != UserRole.Printer && request.Role != UserRole.Admin && request.BranchIds != null && request.BranchIds.Any())
        {
            foreach (var branchId in request.BranchIds)
            {
                _context.UserBranches.Add(new UserBranch { UserId = user.Id, BranchId = branchId });
            }
        }

        await _context.SaveChangesAsync();

        // Zwracamy zaktualizowanego usera, dociągając na nowo oddziały, żeby mieć ich nazwy
        var updatedBranches = await _context.UserBranches
            .Where(ub => ub.UserId == user.Id)
            .Include(ub => ub.Branch)
            .Select(ub => new UserBranchDto(ub.BranchId, ub.Branch.Name))
            .ToListAsync();

        return Ok(new UserResponse(user.Id, user.FirstName, user.LastName, user.Email!, user.Role, user.IsActive, updatedBranches, user.MustChangePassword));
    }
    
    [HttpPut("{id}/status")]
    [Authorize(Roles = "Admin,Coordinator")]
    public async Task<IActionResult> ToggleUserStatus(Guid id)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user == null) return NotFound(new { message = "Nie znaleziono użytkownika o podanym ID." });

        // 1. Zabezpieczenie przed zablokowaniem samego siebie
        var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (currentUserId == user.Id.ToString())
            return BadRequest(new { message = "Nie możesz zablokować własnego konta!" });

        // 2. Zabezpieczenie: Koordynator nie może zablokować Admina (ZMIENIONO SPOSÓB SPRAWDZANIA ROLI)
        // Zakładam, że Twoja klasa User ma property np. 'Role' typu 'UserRole'
        if (user.Role == UserRole.Admin && !User.IsInRole("Admin"))
        {
            return BadRequest(new { message = "Koordynator nie może blokować konta Administratora." });
        }

        // 3. Zabezpieczenie: Koordynator może zmieniać status tylko użytkownikom ze swoich oddziałów
        if (!User.IsInRole("Admin"))
        {
            if (!Guid.TryParse(currentUserId, out var parsedCurrentUserId)) return Unauthorized();

            var coordinatorBranchIds = await _context.UserBranches
                .Where(ub => ub.UserId == parsedCurrentUserId)
                .Select(ub => ub.BranchId)
                .ToListAsync();

            var userBranchIds = await _context.UserBranches
                .Where(ub => ub.UserId == user.Id)
                .Select(ub => ub.BranchId)
                .ToListAsync();

            var sharesBranch = userBranchIds.Any(bid => coordinatorBranchIds.Contains(bid));
            if (!sharesBranch)
            {
                return Forbid();
            }
        }

        user.IsActive = !user.IsActive;
        var result = await _userManager.UpdateAsync(user);

        if (!result.Succeeded) return BadRequest(result.Errors);

        return Ok(new { message = user.IsActive ? "Konto zostało odblokowane." : "Konto zostało zablokowane." });
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")] // TYLKO ADMIN
    public async Task<IActionResult> DeleteUser(Guid id)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user == null) return NotFound("Nie znaleziono użytkownika.");

        // 1. Zabezpieczenie przed usunięciem samego siebie
        var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (currentUserId == user.Id.ToString())
            return BadRequest(new { message = "Nie możesz usunąć własnego konta!" });
        
        // 2. Zablokowanie usuwania innych Adminów (ZMIENIONO SPOSÓB SPRAWDZANIA ROLI)
        if (user.Role == UserRole.Admin)
        {
            return BadRequest(new { message = "Nie możesz trwale usunąć konta innego Administratora. Najpierw zmień mu rolę." });
        }

        // CZYSZCZENIE POWIĄZAŃ PRZED USUNIĘCIEM:
        
        // A. Usuwanie zastępstw
        var substitutes = await _context.GroupSubstitutes
            .Where(gs => gs.SubstituteTrainerId == id)
            .ToListAsync();
        
        if (substitutes.Any())
        {
            _context.GroupSubstitutes.RemoveRange(substitutes);
        }

        // B. Usuwanie przypisań do oddziałów (MOCNO ZALECANE, zapobiegnie kolejnemu błędowi 500!)
        
        var userBranches = await _context.UserBranches.Where(ub => ub.UserId == id).ToListAsync();
        if (userBranches.Any())
        {
            _context.UserBranches.RemoveRange(userBranches);
        }


        await _context.SaveChangesAsync();
        
        // Twarde usunięcie
        var result = await _userManager.DeleteAsync(user);
        if (!result.Succeeded) return BadRequest(result.Errors);

        return NoContent();
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var currentUserIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (currentUserIdStr == null) return Unauthorized();

        var user = await _userManager.FindByIdAsync(currentUserIdStr);
        if (user == null) return Unauthorized();

        var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            var errors = result.Errors.Select(e => e.Description).ToList();
            return BadRequest(new { message = string.Join(" ", errors) });
        }

        user.MustChangePassword = false;
        await _userManager.UpdateAsync(user);

        return Ok(new { message = "Hasło zostało pomyślnie zmienione." });
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> GetCurrentUserDetails()
    {
        var currentUserIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (currentUserIdStr == null) return Unauthorized();

        var user = await _userManager.Users
            .AsNoTracking()
            .Include(u => u.UserBranches)
            .ThenInclude(ub => ub.Branch)
            .FirstOrDefaultAsync(u => u.Id.ToString() == currentUserIdStr);

        if (user == null) return Unauthorized();

        var response = new UserResponse(
            user.Id,
            user.FirstName,
            user.LastName,
            user.Email ?? string.Empty,
            user.Role,
            user.IsActive,
            user.UserBranches.Select(ub => new UserBranchDto(
                ub.BranchId,
                ub.Branch.Name
            )).ToList(),
            user.MustChangePassword
        );

        return Ok(response);
    }

    [HttpPost("{id}/reset-password")]
    [Authorize(Roles = "Admin,Coordinator")]
    public async Task<IActionResult> ResetPassword(Guid id, [FromBody] ResetPasswordRequest request)
    {
        var currentUserIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (currentUserIdStr == null) return Unauthorized();
        var currentUser = await _userManager.FindByIdAsync(currentUserIdStr);
        if (currentUser == null) return Unauthorized();

        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user == null) return NotFound(new { message = "Nie znaleziono użytkownika o podanym ID." });

        if (currentUser.Role == UserRole.Coordinator)
        {
            if (user.Role == UserRole.Admin || user.Role == UserRole.Coordinator)
            {
                return BadRequest(new { message = "Koordynator nie może resetować hasła administratorowi ani innemu koordynatorowi." });
            }

            var coordinatorBranchIds = await _context.UserBranches
                .Where(ub => ub.UserId == currentUser.Id)
                .Select(ub => ub.BranchId)
                .ToListAsync();

            var userBranchIds = await _context.UserBranches
                .Where(ub => ub.UserId == user.Id)
                .Select(ub => ub.BranchId)
                .ToListAsync();

            var sharesBranch = userBranchIds.Any(bid => coordinatorBranchIds.Contains(bid));
            if (!sharesBranch && user.Id != currentUser.Id)
            {
                return Forbid();
            }
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, request.NewPassword);

        if (!result.Succeeded)
        {
            var errors = result.Errors.Select(e => e.Description).ToList();
            return BadRequest(new { message = string.Join(" ", errors) });
        }

        user.MustChangePassword = true;
        await _userManager.UpdateAsync(user);

        return Ok(new { message = "Hasło tymczasowe zostało pomyślnie ustawione." });
    }
}