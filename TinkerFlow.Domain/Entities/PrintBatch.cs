using TinkerFlow.Domain.Enums;

namespace TinkerFlow.Domain.Entities;

public class PrintBatch
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Group Group { get; set; } = null!;
    
    // Data bieżących zajęć, na których trener klika "Wyślij" (domyślnie DateTime.Today z frontendu)
    public DateTime LessonDate { get; set; }
    
    // To pole jest WYLICZANE przez Twój backend, trener nie ma do niego dostępu!
    // np. Deadline = LessonDate.AddDays(6);
    public DateTime Deadline { get; set; }
    
    // Zbiorcza notatka z textarea (np. "Wszystko białe, Janek x2 mniejsze")
    public string? Notes { get; set; } 
    
    // Notatka / informacja od drukarza dla trenera
    public string? PrinterNotes { get; set; }
    
    public DateTime CreatedAt { get; set; }
    public Guid CreatedByTrainerId { get; set; }

    public PrintBatchState Status { get; set; } = PrintBatchState.Pending;

    public ICollection<PrintJob> PrintJobs { get; set; } = new List<PrintJob>();
}