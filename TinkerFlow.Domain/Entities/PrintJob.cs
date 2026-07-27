using TinkerFlow.Domain.Enums;

namespace TinkerFlow.Domain.Entities;

public class PrintJob
{
    public Guid Id { get; set; }
    
    // --- RELACJA DO PACZKI (BATCHA) ---
    public Guid PrintBatchId { get; set; }
    public PrintBatch PrintBatch { get; set; } = null!;

    // --- RELACJA DO UCZNIA ---
    public Guid StudentId { get; set; }
    public Student Student { get; set; } = null!;

    // --- RELACJA DO PROJEKTU (Z Matrycy) ---
    // Nullable, bo może to być projekt wpisany ręcznie
    public Guid? StudentProjectId { get; set; }
    public StudentProject? StudentProject { get; set; }

    // Nazwa wpisywana z palca, jeśli StudentProjectId jest null
    public string? CustomName { get; set; }
    
    // Status konkretnego wydruku (np. żeby oznaczyć, że ten jeden pająk z całej paczki się nie udał)
    public PrintJobsStates Status { get; set; } = PrintJobsStates.Pending;
    
    public DateTime CreatedAt { get; set; }
}