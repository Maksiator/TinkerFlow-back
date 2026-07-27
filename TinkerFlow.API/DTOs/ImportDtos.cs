namespace TinkerFlow.API.DTOs;

public record ImportMatrixRequest(
    Guid GroupId,
    List<string> StudentNames,
    List<ImportProjectRow> Rows
);

public record ImportProjectRow(
    string ProjectName,
    string ProjectCode,
    List<string> Statuses
);