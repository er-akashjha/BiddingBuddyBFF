using BiddingBuddy.Bff.Core.DTOs.Documents;

namespace BiddingBuddy.Bff.Core.Interfaces;

public interface IDocumentService
{
    Task<IReadOnlyList<FolderDto>> ListFoldersAsync(Guid orgId, Guid? parentId, CancellationToken ct = default);
    Task<FolderDetailDto> GetFolderAsync(Guid folderId, Guid orgId, CancellationToken ct = default);
    Task<FolderDto> CreateFolderAsync(Guid orgId, Guid userId, CreateFolderDto dto, CancellationToken ct = default);
    Task<FolderDto> UpdateFolderAsync(Guid folderId, Guid orgId, UpdateFolderDto dto, CancellationToken ct = default);
    Task DeleteFolderAsync(Guid folderId, Guid orgId, CancellationToken ct = default);

    Task<IReadOnlyList<DocumentDto>> ListDocumentsAsync(Guid orgId, Guid? folderId, string? documentType, CancellationToken ct = default);
    Task<DocumentDto> GetDocumentAsync(Guid documentId, Guid orgId, CancellationToken ct = default);
    Task<DocumentDto> CreateDocumentAsync(Guid orgId, Guid userId, CreateDocumentDto dto, CancellationToken ct = default);
    Task<DocumentDto> UpdateDocumentAsync(Guid documentId, Guid orgId, UpdateDocumentDto dto, CancellationToken ct = default);
    Task DeleteDocumentAsync(Guid documentId, Guid orgId, CancellationToken ct = default);

    Task<IReadOnlyList<DocumentVersionDto>> GetVersionsAsync(Guid documentId, Guid orgId, CancellationToken ct = default);
    Task<DocumentVersionDto> AddVersionAsync(Guid documentId, Guid orgId, Guid userId, AddDocumentVersionDto dto, CancellationToken ct = default);
}
