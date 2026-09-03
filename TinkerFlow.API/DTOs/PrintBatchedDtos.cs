using System.ComponentModel.DataAnnotations;
using TinkerFlow.Domain.Enums;

namespace TinkerFlow.Api.DTOs;

// ==========================================
// 1. KONTRAKTY WEJŚCIOWE (TWORZENIE ZLECENIA)
// ==========================================

public record CreatePrintBatchRequest(
    [Required] Guid GroupId,
    [Required] DateTime LessonDate,
    string? Notes,
    [Required, MinLength(1, ErrorMessage = "Paczka musi zawierać co najmniej jeden wydruk.")] 
    List<PrintJobRequest> ProjectsToPrint
);

public record PrintJobRequest(
    [Required] Guid StudentId,
    Guid? ProjectId,
    string? CustomName
);


// ==========================================
// 2. KONTRAKTY WYJŚCIOWE (WIDOK DLA DRUKARZA)
// ==========================================

// ZAKTUALIZOWANE: Zawiera pełne dane tekstowe grupy i zadań
public record PrintBatchResponse(
    Guid Id,
    Guid GroupId,
    string GroupName,
    Guid BranchId,
    string BranchName,
    DateTime LessonDate,
    DateTime Deadline,
    string? Notes,
    PrintBatchState Status,
    List<PrintJobResponse> PrintJobs,
    DateTime CreatedAt,
    Guid? AssignedPrinterId = null,
    string? AssignedPrinterName = null
);

// ZAKTUALIZOWANE: Zawiera imię, nazwisko ucznia oraz czytelną nazwę modelu
public record PrintJobResponse(
    Guid Id,
    Guid StudentId,
    string StudentName,
    Guid? StudentProjectId,
    string ProjectName,
    PrintJobsStates Status
);

public record UpdatePrintBatchRequest(
    [Required] DateTime LessonDate,
    string? Notes,
    [Required, MinLength(1, ErrorMessage = "Paczka po edycji musi zawierać co najmniej jeden wydruk.")] 
    List<PrintJobRequest> ProjectsToPrint
);

// ==========================================
// 3. KONTRAKTY ZMIANY STATUSU (AKCJE DRUKARZA)
// ==========================================

public record UpdatePrintBatchStatusRequest(
    [Required] PrintBatchState Status
);

public record UpdatePrintJobStatusRequest(
    [Required] PrintJobsStates Status
);

public record ConfirmDeliveryRequest(
    [Required] List<Guid> ConfirmedStudentProjectIds
);