using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using TinkerFlow.Api.DTOs;
using TinkerFlow.Domain.Entities;
using TinkerFlow.Domain.Enums;
using TinkerFlow.Infrastructure; 

namespace TinkerFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PrintBatchesController : ControllerBase
{
    private readonly TinkerFlowDbContext _context;
    private readonly ILogger<PrintBatchesController> _logger;

    public PrintBatchesController(TinkerFlowDbContext context, ILogger<PrintBatchesController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpPost("send-to-farm")]
    public async Task<IActionResult> SendToFarm([FromBody] CreatePrintBatchRequest request)
    {
        // 1. Walidacja żądania
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        // 2. Pobranie ID trenera
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out Guid trainerId))
        {
            return Unauthorized(new { message = "Sesja wygasła lub jest nieprawidłowa. Zaloguj się ponownie." });
        }

        using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            var groupExists = await _context.Groups.AnyAsync(g => g.Id == request.GroupId);
            if (!groupExists)
            {
                _logger.LogWarning("Trener {TrainerId} próbował wysłać paczkę dla nieistniejącej grupy {GroupId}.", trainerId, request.GroupId);
                return NotFound(new { message = "Podana grupa nie istnieje w systemie." });
            }

            var settings = await _context.SystemSettings.FirstOrDefaultAsync();
            int daysToDeliver = settings?.PrintDeadlineDays ?? 6; 

            var calculatedDeadline = request.LessonDate.Date
                .AddDays(daysToDeliver)
                .AddHours(23)
                .AddMinutes(59)
                .AddSeconds(59);

            // 3. TWORZYMY OBIEKT BATCH (Zanim zaczniemy pętlę!)
            var batch = new PrintBatch
            {
                Id = Guid.NewGuid(),
                GroupId = request.GroupId,
                LessonDate = request.LessonDate.Date,
                Deadline = calculatedDeadline,
                Notes = request.Notes,
                CreatedAt = DateTime.UtcNow,
                CreatedByTrainerId = trainerId,
                Status = PrintBatchState.Pending,
                PrintJobs = new List<PrintJob>()
            };

            var studentProjectIdsToUpdate = new List<Guid>();

            // 4. PRZETWARZAMY JOBY
            foreach (var item in request.ProjectsToPrint)
            {
                Guid? actualStudentProjectId = null;

                if (item.ProjectId.HasValue)
                {
                    // Szukamy rekordu w bazie
                    var sp = await _context.StudentProjects
                        .FirstOrDefaultAsync(x => x.StudentId == item.StudentId && x.ProjectId == item.ProjectId.Value);
                    
                    if (sp != null)
                    {
                        actualStudentProjectId = sp.Id;
                        studentProjectIdsToUpdate.Add(sp.Id);
                    }
                }

                if (actualStudentProjectId == null && string.IsNullOrWhiteSpace(item.CustomName))
                {
                    return BadRequest(new { message = "Projekt niestandardowy musi posiadać zdefiniowaną nazwę." });
                }

                var job = new PrintJob
                {
                    Id = Guid.NewGuid(),
                    PrintBatchId = batch.Id, // Twarde przypisanie relacji!
                    StudentId = item.StudentId,
                    StudentProjectId = actualStudentProjectId,
                    CustomName = item.CustomName ?? (actualStudentProjectId == null ? "Brak nazwy" : null),
                    Status = PrintJobsStates.Pending,
                    CreatedAt = DateTime.UtcNow
                };

                batch.PrintJobs.Add(job);
            }

            // 5. ZAPIS DO BAZY
            _context.PrintBatches.Add(batch);

            // 6. AKTUALIZACJA STATUSÓW DLA TRENERA NA "WYSŁANO DO DRUKU"
            if (studentProjectIdsToUpdate.Any())
            {
                var studentProjects = await _context.StudentProjects
                    .Where(sp => studentProjectIdsToUpdate.Contains(sp.Id))
                    .ToListAsync();

                foreach (var sp in studentProjects)
                {
                    sp.Status = ProjectState.SentToPrint; 
                }
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation("Pomyślnie utworzono PrintBatch {BatchId} dla grupy {GroupId}.", batch.Id, request.GroupId);

            return Ok(new { 
                message = "Paczka wysłana pomyślnie na farmę.", 
                batchId = batch.Id, 
                deadline = batch.Deadline 
            });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Krytyczny błąd podczas tworzenia PrintBatch dla grupy {GroupId}.", request.GroupId);
            return StatusCode(500, new { message = "Wystąpił błąd krytyczny serwera podczas wysyłania na farmę." });
        }
    }

    [HttpGet("farm")]
    public async Task<IActionResult> GetBatchesForFarm([FromQuery] PrintBatchState? statusFilter)
    {
        var query = _context.PrintBatches
            .Include(pb => pb.Group)
                .ThenInclude(g => g.Branch)
            .Include(pb => pb.PrintJobs)
                .ThenInclude(pj => pj.Student)
            .Include(pb => pb.PrintJobs)
                .ThenInclude(pj => pj.StudentProject)
                    .ThenInclude(sp => sp!.Project) 
            .AsQueryable();

        if (statusFilter.HasValue)
        {
            query = query.Where(pb => pb.Status == statusFilter.Value);
        }

        var batches = await query
            .OrderByDescending(pb => pb.CreatedAt) // Od najnowszych
            .ToListAsync();

        var response = batches.Select(pb => new PrintBatchResponse(
            pb.Id,
            pb.GroupId,
            pb.Group != null ? pb.Group.Name : "Nieznana grupa",
            pb.Group != null ? pb.Group.BranchId : Guid.Empty,
            pb.Group != null && pb.Group.Branch != null ? pb.Group.Branch.Name : "Nieznany oddział",
            pb.LessonDate,
            pb.Deadline,
            pb.Notes,
            pb.Status,
            pb.PrintJobs.Select(pj => new PrintJobResponse(
                pj.Id,
                pj.StudentId,
                pj.Student != null ? $"{pj.Student.FirstName} {pj.Student.LastName}".Trim() : "Nieznany uczeń",
                pj.StudentProjectId,
                pj.StudentProjectId.HasValue && pj.StudentProject?.Project != null 
                    ? pj.StudentProject.Project.Name 
                    : (pj.CustomName ?? "Projekt własny"),
                pj.Status
            )).ToList()
        )).ToList();

        return Ok(response);
    }

    [HttpPatch("{batchId:guid}/status")]
    public async Task<IActionResult> UpdateBatchStatus(Guid batchId, [FromBody] UpdatePrintBatchStatusRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        // 1. Zaciągamy paczkę RAZEM z jej wydrukami (Jobami)
        var batch = await _context.PrintBatches
            .Include(pb => pb.PrintJobs)
            .FirstOrDefaultAsync(pb => pb.Id == batchId);
            
        if (batch == null)
            return NotFound(new { message = "Nie znaleziono podanej paczki." });

        // Używamy transakcji, by zaktualizować wszystko naraz
        using var transaction = await _context.Database.BeginTransactionAsync();
        try 
        {
            // Zmiana statusu samej paczki
            batch.Status = request.Status;

            // 2. KASKADOWA ZMIANA STATUSÓW WYDRUKÓW (BULK UPDATE)
            if (request.Status == PrintBatchState.Printing)
            {
                // Jeśli paczka idzie do druku, wszystkie oczekujące modele też idą do druku
                foreach (var job in batch.PrintJobs.Where(j => j.Status == PrintJobsStates.Pending))
                {
                    job.Status = PrintJobsStates.Printing;
                }
            }
            else if (request.Status == PrintBatchState.ReadyForCollection)
            {
                // Jeśli paczka jest gotowa, wszystkie drukujące się modele są Zakończone.
                // UWAGA: Nie ruszamy tych ze statusem "Failed", jeśli drukarz je tak oznaczył!
                foreach (var job in batch.PrintJobs.Where(j => j.Status == PrintJobsStates.Printing || j.Status == PrintJobsStates.Pending))
                {
                    job.Status = PrintJobsStates.Printed;
                }
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation("Zmieniono status paczki {BatchId} na {Status} oraz masowo zaktualizowano jej wydruki.", batchId, request.Status);
            return Ok(new { message = "Status paczki i wydruków zaktualizowany.", newStatus = batch.Status });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Błąd podczas zmiany statusu paczki {BatchId}", batchId);
            return StatusCode(500, new { message = "Wystąpił błąd podczas masowej aktualizacji." });
        }
    }

    [HttpPatch("~/api/printjobs/{jobId:guid}/status")]
    public async Task<IActionResult> UpdateJobStatus(Guid jobId, [FromBody] UpdatePrintJobStatusRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var job = await _context.PrintJobs.FirstOrDefaultAsync(pj => pj.Id == jobId);
        if (job == null)
            return NotFound(new { message = "Nie znaleziono podanego wydruku." });

        job.Status = request.Status;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Zmieniono status wydruku {JobId} na {Status}", jobId, request.Status);
        return Ok(new { message = "Status wydruku zaktualizowany.", newStatus = job.Status });
    }

    [HttpPut("{batchId:guid}")]
    public async Task<IActionResult> UpdateBatch(Guid batchId, [FromBody] UpdatePrintBatchRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out Guid trainerId))
        {
            return Unauthorized(new { message = "Sesja wygasła lub jest nieprawidłowa. Zaloguj się ponownie." });
        }

        using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            var batch = await _context.PrintBatches
                .Include(b => b.PrintJobs)
                .FirstOrDefaultAsync(b => b.Id == batchId);

            if (batch == null)
                return NotFound(new { message = "Podana paczka nie istnieje w systemie." });

            if (batch.Status != PrintBatchState.Pending)
            {
                return BadRequest(new { message = "Nie można edytować paczki, która została już przekazana do druku lub odebrana." });
            }

            var settings = await _context.SystemSettings.FirstOrDefaultAsync();
            int daysToDeliver = settings?.PrintDeadlineDays ?? 6;

            var calculatedDeadline = request.LessonDate.Date
                .AddDays(daysToDeliver)
                .AddHours(23)
                .AddMinutes(59)
                .AddSeconds(59);

            var oldStudentProjectIds = batch.PrintJobs
                .Where(pj => pj.StudentProjectId.HasValue)
                .Select(pj => pj.StudentProjectId!.Value)
                .ToList();

            if (oldStudentProjectIds.Any())
            {
                var oldStudentProjects = await _context.StudentProjects
                    .Where(sp => oldStudentProjectIds.Contains(sp.Id))
                    .ToListAsync();

                foreach (var sp in oldStudentProjects)
                {
                    sp.Status = ProjectState.ReadytoPrint;
                }
            }

            _context.PrintJobs.RemoveRange(batch.PrintJobs);
            batch.PrintJobs.Clear();

            batch.LessonDate = request.LessonDate.Date;
            batch.Deadline = calculatedDeadline;
            batch.Notes = request.Notes;

            var newStudentProjectIdsToUpdate = new List<Guid>();

            foreach (var item in request.ProjectsToPrint)
            {
                if (!item.ProjectId.HasValue && string.IsNullOrWhiteSpace(item.CustomName))
                {
                    return BadRequest(new { message = "Projekt niestandardowy musi posiadać zdefiniowaną nazwę." });
                }

                var job = new PrintJob
                {
                    Id = Guid.NewGuid(),
                    PrintBatchId = batch.Id,
                    StudentId = item.StudentId,
                    StudentProjectId = item.ProjectId,
                    CustomName = item.CustomName,
                    Status = PrintJobsStates.Pending,
                    CreatedAt = DateTime.UtcNow
                };

                batch.PrintJobs.Add(job);

                if (item.ProjectId.HasValue)
                {
                    newStudentProjectIdsToUpdate.Add(item.ProjectId.Value);
                }
            }

            if (newStudentProjectIdsToUpdate.Any())
            {
                var newStudentProjects = await _context.StudentProjects
                    .Where(sp => newStudentProjectIdsToUpdate.Contains(sp.Id))
                    .ToListAsync();

                foreach (var sp in newStudentProjects)
                {
                    sp.Status = ProjectState.SentToPrint;
                }
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation("Trener {TrainerId} pomyślnie zaktualizował zawartość paczki {BatchId}.", trainerId, batchId);

            return Ok(new { 
                message = "Paczka została pomyślnie zaktualizowana.", 
                batchId = batch.Id, 
                deadline = batch.Deadline 
            });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Krytyczny błąd podczas aktualizacji PrintBatch {BatchId}.", batchId);
            return StatusCode(500, new { message = "Wystąpił błąd krytyczny serwera podczas aktualizacji paczki." });
        }
    }

    [HttpGet("group/{groupId:guid}/ready")]
    public async Task<IActionResult> GetReadyBatchForGroup(Guid groupId)
    {
        var batch = await _context.PrintBatches
            .Include(pb => pb.Group)
                .ThenInclude(g => g.Branch)
            .Include(pb => pb.PrintJobs)
                .ThenInclude(pj => pj.Student)
            .Include(pb => pb.PrintJobs)
                .ThenInclude(pj => pj.StudentProject)
                    .ThenInclude(sp => sp!.Project)
            .Where(pb => pb.GroupId == groupId && pb.Status == PrintBatchState.ReadyForCollection)
            .OrderByDescending(pb => pb.CreatedAt)
            .FirstOrDefaultAsync();

        if (batch == null)
            return Ok(null);

        var response = new PrintBatchResponse(
            batch.Id,
            batch.GroupId,
            batch.Group?.Name ?? "Nieznana grupa",
            batch.Group != null ? batch.Group.BranchId : Guid.Empty,
            batch.Group != null && batch.Group.Branch != null ? batch.Group.Branch.Name : "Nieznany oddział",
            batch.LessonDate,
            batch.Deadline,
            batch.Notes,
            batch.Status,
            batch.PrintJobs.Select(pj => new PrintJobResponse(
                pj.Id,
                pj.StudentId,
                pj.Student != null ? $"{pj.Student.FirstName} {pj.Student.LastName}".Trim() : "Nieznany uczeń",
                pj.StudentProjectId,
                pj.StudentProjectId.HasValue && pj.StudentProject?.Project != null 
                    ? pj.StudentProject.Project.Name 
                    : (pj.CustomName ?? "Projekt własny"),
                pj.Status
            )).ToList()
        );

        return Ok(response);
    }

    [HttpPost("{batchId:guid}/delivery-confirm")]
    public async Task<IActionResult> ConfirmDelivery(Guid batchId, [FromBody] ConfirmDeliveryRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            var batch = await _context.PrintBatches
                .Include(b => b.PrintJobs)
                .FirstOrDefaultAsync(b => b.Id == batchId);

            if (batch == null)
                return NotFound(new { message = "Podana paczka nie istnieje." });

            if (request.ConfirmedStudentProjectIds.Any())
            {
                var studentProjects = await _context.StudentProjects
                    .Where(sp => request.ConfirmedStudentProjectIds.Contains(sp.Id))
                    .ToListAsync();

                foreach (var sp in studentProjects)
                {
                    sp.Status = ProjectState.Completed; 
                }

                foreach (var job in batch.PrintJobs)
                {
                    if (job.StudentProjectId.HasValue && request.ConfirmedStudentProjectIds.Contains(job.StudentProjectId.Value))
                    {
                        job.Status = PrintJobsStates.Printed;
                    }
                }
            }
        
            batch.Status = PrintBatchState.Completed;
            
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation("Trener pomyślnie zweryfikował i odebrał paczkę {BatchId}.", batchId);
            return Ok(new { message = "Odbiór paczki zatwierdzony. Statusy zaktualizowane na Completed." });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Błąd podczas zatwierdzania odbioru paczki {BatchId}.", batchId);
            return StatusCode(500, new { message = "Błąd serwera podczas zatwierdzania odbioru." });
        }
    }
    
    [HttpDelete("{batchId:guid}")]
    public async Task<IActionResult> DeleteBatch(Guid batchId)
    {
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var batch = await _context.PrintBatches
                .Include(b => b.PrintJobs)
                .FirstOrDefaultAsync(b => b.Id == batchId);

            if (batch == null)
                return NotFound(new { message = "Podana paczka nie istnieje." });

            // 1. Zbieramy ID przypisań projektów, żeby cofnąć im status na matrycy trenera
            var studentProjectIds = batch.PrintJobs
                .Where(pj => pj.StudentProjectId.HasValue)
                .Select(pj => pj.StudentProjectId!.Value)
                .ToList();

            if (studentProjectIds.Any())
            {
                var studentProjects = await _context.StudentProjects
                    .Where(sp => studentProjectIds.Contains(sp.Id))
                    .ToListAsync();

                foreach (var sp in studentProjects)
                {
                    // Wycofujemy z "Wysłano" z powrotem na "Do druku" (żeby trener mógł to znowu wysłać)
                    sp.Status = ProjectState.ReadytoPrint; 
                }
            }

            // 2. Usuwamy powiązane wydruki i paczkę
            _context.PrintJobs.RemoveRange(batch.PrintJobs);
            _context.PrintBatches.Remove(batch);
            
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation("Drukarz/Admin usunął paczkę {BatchId}", batchId);
            return Ok(new { message = "Paczka została trwale usunięta." });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Błąd podczas usuwania paczki {BatchId}", batchId);
            return StatusCode(500, new { message = "Wystąpił błąd krytyczny podczas usuwania paczki." });
        }
    }
    [HttpGet("group/{groupId:guid}/history")]
    public async Task<IActionResult> GetGroupBatchesHistory(Guid groupId)
    {
        // Zabezpieczenie - upewniamy się że pytający to zalogowany użytkownik
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim))
            return Unauthorized();

        var batches = await _context.PrintBatches
            .Include(pb => pb.Group)
                .ThenInclude(g => g.Branch)
            .Include(pb => pb.PrintJobs)
            .ThenInclude(pj => pj.Student)
            .Include(pb => pb.PrintJobs)
            .ThenInclude(pj => pj.StudentProject)
            .ThenInclude(sp => sp!.Project)
            .Where(pb => pb.GroupId == groupId)
            .OrderByDescending(pb => pb.CreatedAt) // Od najnowszych
            .ToListAsync();

        var response = batches.Select(pb => new PrintBatchResponse(
            pb.Id,
            pb.GroupId,
            pb.Group?.Name ?? "Nieznana grupa",
            pb.Group != null ? pb.Group.BranchId : Guid.Empty,
            pb.Group != null && pb.Group.Branch != null ? pb.Group.Branch.Name : "Nieznany oddział",
            pb.LessonDate,
            pb.Deadline,
            pb.Notes,
            pb.Status,
            pb.PrintJobs.Select(pj => new PrintJobResponse(
                pj.Id,
                pj.StudentId,
                pj.Student != null ? $"{pj.Student.FirstName} {pj.Student.LastName}".Trim() : "Nieznany uczeń",
                pj.StudentProjectId,
                pj.StudentProjectId.HasValue && pj.StudentProject?.Project != null 
                    ? pj.StudentProject.Project.Name 
                    : (pj.CustomName ?? "Projekt własny"),
                pj.Status
            )).ToList()
        )).ToList();

        return Ok(response);
    }
}