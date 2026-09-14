namespace TinkerFlow.API.DTOs;

public record ImportMatrixRequest(
    Guid? GroupId,
    List<string> StudentNames,
    List<ImportProjectRow> Rows,
    List<string>? SelectedStudentNames = null
);

public record ImportProjectRow(
    string ProjectName,
    string ProjectCode,
    List<string> Statuses
);

public record MatchStudentsRequest(
    List<string> StudentNames
);

public record MatchedStudentDto(
    string NameInExcel,
    Guid? StudentId,
    string? StudentName,
    Guid? GroupId,
    string? GroupName,
    Guid? BranchId,
    string? BranchName,
    bool IsMatched
);