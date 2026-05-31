namespace BiddingBuddy.Bff.Core.DTOs.Documents;

public record FolderDto(
    Guid Id,
    string Name,
    Guid? ParentId,
    int DocumentCount,
    int ChildFolderCount,
    DateTime CreatedAt
);

public record FolderDetailDto(
    Guid Id,
    string Name,
    Guid? ParentId,
    DateTime CreatedAt,
    IReadOnlyList<FolderDto> Children,
    IReadOnlyList<DocumentDto> Documents
);

public record DocumentDto(
    Guid Id,
    string Name,
    string FileName,
    string S3Key,
    Guid? FolderId,
    string? FolderName,
    int? FileSizeKb,
    string? MimeType,
    string? DocumentType,
    DateOnly? ExpiryDate,
    string[]? Tags,
    int? HealthScore,
    Guid UploadedBy,
    string? UploaderName,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int VersionCount
);

public record DocumentVersionDto(
    Guid Id,
    string S3Key,
    string? S3VersionId,
    int? FileSizeKb,
    string? Notes,
    Guid UploadedBy,
    string? UploaderName,
    DateTime CreatedAt
);

public record CreateFolderDto(string Name, Guid? ParentId);

public record UpdateFolderDto(string Name, Guid? ParentId);

public record CreateDocumentDto(
    string Name,
    string FileName,
    string S3Key,
    string? S3VersionId,
    Guid? FolderId,
    int? FileSizeKb,
    string? MimeType,
    string? DocumentType,
    DateOnly? ExpiryDate,
    string[]? Tags
);

public record UpdateDocumentDto(
    string? Name,
    Guid? FolderId,
    string? DocumentType,
    DateOnly? ExpiryDate,
    string[]? Tags
);

public record AddDocumentVersionDto(
    string S3Key,
    string? S3VersionId,
    int? FileSizeKb,
    string? Notes
);
