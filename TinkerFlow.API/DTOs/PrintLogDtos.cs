namespace TinkerFlow.API.DTOs;

public record CreatePrintLogRequest(
    Guid StudentId,
    DateTime? PrintDate
);

// --- NOWE DTO DO EDYCJI ---
public record UpdatePrintLogRequest(
    Guid StudentId, // Żeby móc przepiąć wydruk na inne dziecko, jak się "omsknie" palec
    DateTime PrintDate
);

// --- NOWE DTO DO HISTORII ---
public record PrintLogHistoryResponse(
    Guid Id,
    Guid StudentId,
    string FullName,
    DateTime PrintDate
);