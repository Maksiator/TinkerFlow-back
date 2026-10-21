namespace TinkerFlow.API.DTOs;

public record SystemSettingsResponse(
    int SubstituteDaysBefore,
    int SubstituteDaysAfter,
    int PrintDeadlineDays,
    string CurrentAcademicYear,
    Guid? OfficePrinterUserId = null,
    string? OfficePrinterUserName = null
);

public record UpdateSystemSettingsRequest(
    int SubstituteDaysBefore,
    int SubstituteDaysAfter,
    int PrintDeadlineDays,
    string CurrentAcademicYear,
    Guid? OfficePrinterUserId = null
);