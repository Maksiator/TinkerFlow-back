using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using TinkerFlow.Api.DTOs;
using TinkerFlow.API.Extensions;
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
    private readonly TinkerFlow.Infrastructure.Services.IAuditLogService _auditLogService;

    public PrintBatchesController(TinkerFlowDbContext context, ILogger<PrintBatchesController> logger, TinkerFlow.Infrastructure.Services.IAuditLogService auditLogService)
    {
        _context = context;
        _logger = logger;
        _auditLogService = auditLogService;
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

            var groupName = await _context.Groups.Where(g => g.Id == request.GroupId).Select(g => g.Name).FirstOrDefaultAsync() ?? "Grupa";
            await _auditLogService.LogAsync(
                "PrintBatches",
                "SendToFarm",
                $"Trener wysłał paczkę ({batch.PrintJobs.Count} projektów) dla grupy '{groupName}'",
                entityId: batch.Id,
                entityName: groupName,
                userId: trainerId,
                ipAddress: HttpContext.GetClientIpAddress());

            return Ok(new { 
                message = "Paczka wysłana pomyślnie do drukarza.", 
                batchId = batch.Id, 
                deadline = batch.Deadline 
            });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Krytyczny błąd podczas tworzenia PrintBatch dla grupy {GroupId}.", request.GroupId);
            return StatusCode(500, new { message = "Wystąpił błąd krytyczny serwera podczas wysyłania do drukarza." });
        }
    }

    [HttpPost("report-no-prints")]
    public async Task<IActionResult> ReportNoPrints([FromBody] ReportNoPrintsRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out Guid trainerId))
        {
            return Unauthorized(new { message = "Sesja wygasła lub jest nieprawidłowa. Zaloguj się ponownie." });
        }

        var group = await _context.Groups
            .Include(g => g.Branch)
            .FirstOrDefaultAsync(g => g.Id == request.GroupId);

        if (group == null)
        {
            return NotFound(new { message = "Podana grupa nie istnieje w systemie." });
        }

        var lessonDateUtc = DateTime.SpecifyKind(request.LessonDate.Date, DateTimeKind.Utc);

        var reasonText = !string.IsNullOrWhiteSpace(request.Reason) ? request.Reason.Trim() : "Inna technologia (brak wydruków)";
        var noteContent = !string.IsNullOrWhiteSpace(request.AdditionalNotes)
            ? $"{reasonText}: {request.AdditionalNotes.Trim()}"
            : reasonText;

        // Sprawdzamy czy dla tej grupy w tym dniu już nie zgłoszono braku wydruków
        var existingNoPrints = await _context.PrintBatches
            .FirstOrDefaultAsync(b => b.GroupId == request.GroupId && b.LessonDate == lessonDateUtc && b.Status == PrintBatchState.NoPrints);

        if (existingNoPrints != null)
        {
            existingNoPrints.Notes = noteContent;
            existingNoPrints.CreatedAt = DateTime.UtcNow;
            existingNoPrints.CreatedByTrainerId = trainerId;
            await _context.SaveChangesAsync();

            await _auditLogService.LogAsync(
                "PrintBatches",
                "ReportNoPrints",
                $"Trener zaktualizował zgłoszenie braku wydruków dla grupy '{group.Name}' ({noteContent})",
                entityId: existingNoPrints.Id,
                entityName: group.Name,
                userId: trainerId,
                ipAddress: HttpContext.GetClientIpAddress());

            return Ok(new
            {
                message = "Zaktualizowano informację o braku projektów do druku.",
                batchId = existingNoPrints.Id
            });
        }

        var batch = new PrintBatch
        {
            Id = Guid.NewGuid(),
            GroupId = request.GroupId,
            LessonDate = lessonDateUtc,
            Deadline = lessonDateUtc,
            Notes = noteContent,
            CreatedAt = DateTime.UtcNow,
            CreatedByTrainerId = trainerId,
            Status = PrintBatchState.NoPrints,
            PrintJobs = new List<PrintJob>()
        };

        _context.PrintBatches.Add(batch);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Trener {TrainerId} zgłosił brak wydruków dla grupy {GroupId} ({Notes}).", trainerId, request.GroupId, noteContent);

        await _auditLogService.LogAsync(
            "PrintBatches",
            "ReportNoPrints",
            $"Trener zgłosił brak wydruków dla grupy '{group.Name}' ({noteContent})",
            entityId: batch.Id,
            entityName: group.Name,
            userId: trainerId,
            ipAddress: HttpContext.GetClientIpAddress());

        return Ok(new
        {
            message = "Poinformowano drukarza o braku projektów do druku.",
            batchId = batch.Id
        });
    }

    [HttpGet("farm")]
    public async Task<IActionResult> GetBatchesForFarm([FromQuery] PrintBatchState? statusFilter, [FromQuery] bool includeCompleted = false)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim))
            return Unauthorized();

        var userId = Guid.Parse(userIdClaim);
        var user = await _context.Users
            .Include(u => u.UserBranches)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
            return Unauthorized();

        var query = _context.PrintBatches
            .Include(pb => pb.Group)
                .ThenInclude(g => g.Branch)
            .Include(pb => pb.Group)
                .ThenInclude(g => g.AssignedPrinter)
            .Include(pb => pb.PrintJobs)
                .ThenInclude(pj => pj.Student)
            .Include(pb => pb.PrintJobs)
                .ThenInclude(pj => pj.StudentProject)
                    .ThenInclude(sp => sp!.Project) 
            .AsQueryable();

        // Jeśli użytkownik nie jest administratorem, filtrujemy zlecenia
        if (user.Role == UserRole.Printer)
        {
            // Drukarz widzi tylko paczki z grup bezpośrednio do niego przypisanych
            query = query.Where(pb => pb.Group != null && pb.Group.AssignedPrinterId == user.Id);
        }
        else if (user.Role != UserRole.Admin)
        {
            var allowedBranchIds = user.UserBranches.Select(ub => ub.BranchId).ToList();
            query = query.Where(pb => pb.Group != null && allowedBranchIds.Contains(pb.Group.BranchId));
        }

        // Domyślnie pomijamy zakończone zlecenia, chyba że includeCompleted = true
        if (!includeCompleted)
        {
            query = query.Where(pb => pb.Status != PrintBatchState.Completed);
        }

        // Zgłoszenia "brak wydruków / inna technologia" wygasają automatycznie 7 dni po dacie zajęć (pb.LessonDate)
        var expirationThreshold = DateTime.UtcNow.Date.AddDays(-7);
        query = query.Where(pb => pb.Status != PrintBatchState.NoPrints || pb.LessonDate >= expirationThreshold);

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
            )).ToList(),
            pb.CreatedAt,
            pb.Group?.AssignedPrinterId,
            pb.Group?.AssignedPrinter != null ? $"{pb.Group.AssignedPrinter.FirstName} {pb.Group.AssignedPrinter.LastName}".Trim() : null,
            pb.Group?.ClassDayOfWeek
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

            await _auditLogService.LogAsync(
                "PrintBatches",
                "UpdateBatchStatus",
                $"Zmieniono status paczki na: {request.Status}",
                entityId: batch.Id,
                ipAddress: HttpContext.GetClientIpAddress());

            return Ok(new { message = "Status paczki i wydruków zaktualizowany.", newStatus = batch.Status });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Błąd podczas zmiany statusu paczki {BatchId}", batchId);
            return StatusCode(500, new { message = "Wystąpił błąd podczas masowej aktualizacji." });
        }
    }

    [HttpPatch("/api/printjobs/{jobId:guid}/status")]
    [HttpPatch("jobs/{jobId:guid}/status")]
    public async Task<IActionResult> UpdateJobStatus(Guid jobId, [FromBody] UpdatePrintJobStatusRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var job = await _context.PrintJobs
                .Include(pj => pj.PrintBatch)
                    .ThenInclude(pb => pb.PrintJobs)
                .FirstOrDefaultAsync(pj => pj.Id == jobId);

            if (job == null)
                return NotFound(new { message = "Nie znaleziono podanego wydruku." });

            job.Status = request.Status;

            var batch = job.PrintBatch;
            if (batch != null && batch.Status != PrintBatchState.Completed && batch.Status != PrintBatchState.NoPrints)
            {
                var allJobs = batch.PrintJobs?.ToList() ?? new List<PrintJob>();

                // 1. Jeśli wszystkie modele w paczce zostały wydrukowane (lub zepsute, ale co najmniej 1 wydrukowany i zero w toku/oczekujących)
                if (allJobs.Count > 0 && allJobs.All(j => j.Status == PrintJobsStates.Printed || j.Status == PrintJobsStates.Failed)
                    && allJobs.Any(j => j.Status == PrintJobsStates.Printed))
                {
                    batch.Status = PrintBatchState.ReadyForCollection;
                }
                // 2. Jeśli jakikolwiek model jest w druku lub został wydrukowany (a nie wszystkie są gotowe)
                else if (allJobs.Any(j => j.Status == PrintJobsStates.Printing || j.Status == PrintJobsStates.Printed))
                {
                    batch.Status = PrintBatchState.Printing;
                }
                // 3. Jeśli wszystkie modele są oczekujące (cofnięte)
                else if (allJobs.Count > 0 && allJobs.All(j => j.Status == PrintJobsStates.Pending))
                {
                    batch.Status = PrintBatchState.Pending;
                }
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation("Zmieniono status wydruku {JobId} na {Status}. Status paczki {BatchId}: {BatchStatus}",
                jobId, request.Status, batch?.Id, batch?.Status);

            await _auditLogService.LogAsync(
                "PrintJobs",
                "UpdateJobStatus",
                $"Zmieniono status wydruku na: {request.Status}",
                entityId: job.Id,
                ipAddress: HttpContext.GetClientIpAddress());

            return Ok(new
            {
                message = "Status wydruku zaktualizowany.",
                newStatus = job.Status,
                batchStatus = batch?.Status,
                batchId = batch?.Id
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd podczas zmiany statusu wydruku {JobId}", jobId);
            return StatusCode(500, new { message = "Wystąpił błąd podczas aktualizacji statusu wydruku." });
        }
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
            )).ToList(),
            batch.CreatedAt
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

            await _auditLogService.LogAsync(
                "PrintBatches",
                "DeleteBatch",
                $"Usunięto paczkę wydruków ({batch.PrintJobs.Count} modeli)",
                entityId: batch.Id,
                ipAddress: HttpContext.GetClientIpAddress());

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
            )).ToList(),
            pb.CreatedAt
        )).ToList();

        return Ok(response);
    }
}