namespace TinkerFlow.Domain.Entities;

public class SystemSetting
{
    public Guid Id { get; set; }
    // Ustalamy domyślne wartości, gdyby rekord miał się wygenerować sam
    public int SubstituteDaysBefore { get; set; } = 2; 
    public int SubstituteDaysAfter { get; set; } = 2;
    public int PrintDeadlineDays { get; set; } = 6;
    public string CurrentAcademicYear { get; set; } = "2024/2025";
}