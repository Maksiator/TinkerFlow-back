namespace TinkerFlow.API.DTOs;

public record SystemSettingsResponse(
    int SubstituteDaysBefore,
    int SubstituteDaysAfter,
    int PrintDeadlineDays,
    string CurrentAcademicYear
);

public record UpdateSystemSettingsRequest(
    int SubstituteDaysBefore,
    int SubstituteDaysAfter,
    int PrintDeadlineDays,
    string CurrentAcademicYear
);