using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TinkerFlow.API.DTOs;
using TinkerFlow.Domain.Entities;
using TinkerFlow.Infrastructure;

namespace TinkerFlow.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize] 
public class SystemSettingsController : ControllerBase
{
    private readonly TinkerFlowDbContext _context;

    public SystemSettingsController(TinkerFlowDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetSettings()
    {
        var settings = await _context.SystemSettings.FirstOrDefaultAsync();
        
        if (settings == null)
        {
            // Fallback
            return Ok(new SystemSettingsResponse(2, 2, 6, "2024/2025"));
        }

        return Ok(new SystemSettingsResponse(
            settings.SubstituteDaysBefore, 
            settings.SubstituteDaysAfter,
            settings.PrintDeadlineDays,
            settings.CurrentAcademicYear));
    }

    [HttpPut]
    [Authorize(Roles = "Admin")] 
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateSystemSettingsRequest request)
    {
        // Poszerzona walidacja
        if (request.SubstituteDaysBefore < 0 || request.SubstituteDaysAfter < 0 || request.PrintDeadlineDays < 0)
        {
            return BadRequest(new { message = "Liczba dni nie może być ujemna." });
        }
        if (string.IsNullOrWhiteSpace(request.CurrentAcademicYear))
        {
            return BadRequest(new { message = "Rok szkolny nie może być pusty." });
        }

        var settings = await _context.SystemSettings.FirstOrDefaultAsync();

        if (settings == null)
        {
            settings = new SystemSetting
            {
                Id = Guid.NewGuid(), 
                SubstituteDaysBefore = request.SubstituteDaysBefore,
                SubstituteDaysAfter = request.SubstituteDaysAfter,
                PrintDeadlineDays = request.PrintDeadlineDays,
                CurrentAcademicYear = request.CurrentAcademicYear
            };
            _context.SystemSettings.Add(settings);
        }
        else
        {
            settings.SubstituteDaysBefore = request.SubstituteDaysBefore;
            settings.SubstituteDaysAfter = request.SubstituteDaysAfter;
            settings.PrintDeadlineDays = request.PrintDeadlineDays;
            settings.CurrentAcademicYear = request.CurrentAcademicYear;
        }

        await _context.SaveChangesAsync();
        
        return Ok(new SystemSettingsResponse(
            settings.SubstituteDaysBefore, 
            settings.SubstituteDaysAfter,
            settings.PrintDeadlineDays,
            settings.CurrentAcademicYear));
    }
}