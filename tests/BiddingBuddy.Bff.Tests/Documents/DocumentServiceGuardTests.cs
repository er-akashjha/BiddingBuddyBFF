using BiddingBuddy.Bff.Core.DTOs.Documents;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using BiddingBuddy.Bff.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BiddingBuddy.Bff.Tests.Documents;

/// <summary>
/// The document vault's tenant isolation rests on the object key never escaping
/// orgs/{orgId}/docs/, because /view-url and /download-url presign whatever key the row holds.
/// Registration takes the key back from the client, so these tests pin the re-check.
/// </summary>
public sealed class DocumentServiceGuardTests
{
    private static readonly Guid OrgA = Guid.NewGuid();
    private static readonly Guid OrgB = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();

    private static BffDbContext Db(string name) =>
        new(new DbContextOptionsBuilder<BffDbContext>().UseInMemoryDatabase(name).Options);

    private static string KeyFor(Guid orgId) => $"orgs/{orgId}/docs/{Guid.NewGuid()}/gst_cert.pdf";

    private static CreateDocumentDto CreateDoc(string s3Key, Guid? folderId = null) =>
        new("GST certificate", "gst_cert.pdf", s3Key, null, folderId, 245, "application/pdf", "gst", null, null);

    private static async Task<DocumentFolder> SeedFolderAsync(
        BffDbContext db, Guid orgId, Guid? parentId = null, string name = "Compliance")
    {
        var folder = new DocumentFolder
        {
            Id = Guid.NewGuid(), OrgId = orgId, Name = name, ParentId = parentId, CreatedBy = User,
        };
        db.DocumentFolders.Add(folder);
        await db.SaveChangesAsync();
        return folder;
    }

    // ── S3 key ownership (the cross-tenant read) ─────────────────────────────

    [Fact]
    public async Task CreateDocument_WithOwnOrgKey_Succeeds()
    {
        using var db = Db(nameof(CreateDocument_WithOwnOrgKey_Succeeds));
        var key = KeyFor(OrgA);

        var doc = await new DocumentService(db).CreateDocumentAsync(OrgA, User, CreateDoc(key), default);

        Assert.Equal(key, doc.S3Key);
        Assert.Equal(key, (await db.Documents.SingleAsync()).S3Key);
    }

    [Fact]
    public async Task CreateDocument_WithAnotherOrgsKey_Throws_AndPersistsNothing()
    {
        using var db = Db(nameof(CreateDocument_WithAnotherOrgsKey_Throws_AndPersistsNothing));

        // Org A registering org B's object — the row would make /view-url presign B's file.
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new DocumentService(db).CreateDocumentAsync(OrgA, User, CreateDoc(KeyFor(OrgB)), default));

        Assert.Empty(await db.Documents.ToListAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("gst_cert.pdf")]                              // no prefix at all
    [InlineData("orgs/")]
    [InlineData("tenders/gem/2026/secret.pdf")]               // the pipeline's S3 bucket layout
    public async Task CreateDocument_WithMalformedKey_Throws(string s3Key)
    {
        using var db = Db($"{nameof(CreateDocument_WithMalformedKey_Throws)}_{s3Key.GetHashCode()}");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new DocumentService(db).CreateDocumentAsync(OrgA, User, CreateDoc(s3Key), default));
    }

    [Fact]
    public async Task CreateDocument_WithPrefixMatchingButTraversingKey_Throws()
    {
        using var db = Db(nameof(CreateDocument_WithPrefixMatchingButTraversingKey_Throws));

        // Clears StartsWith, still points at org B.
        var key = $"orgs/{OrgA}/docs/../../{OrgB}/docs/secret.pdf";

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new DocumentService(db).CreateDocumentAsync(OrgA, User, CreateDoc(key), default));
    }

    [Fact]
    public async Task CreateDocument_WithOrgIdAsKeySuffix_Throws()
    {
        using var db = Db(nameof(CreateDocument_WithOrgIdAsKeySuffix_Throws));

        // Contains the caller's org id, but the prefix is still org B's.
        var key = $"orgs/{OrgB}/docs/{Guid.NewGuid()}/{OrgA}.pdf";

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new DocumentService(db).CreateDocumentAsync(OrgA, User, CreateDoc(key), default));
    }

    [Fact]
    public async Task AddVersion_WithAnotherOrgsKey_Throws()
    {
        using var db = Db(nameof(AddVersion_WithAnotherOrgsKey_Throws));
        var svc = new DocumentService(db);
        var doc = await svc.CreateDocumentAsync(OrgA, User, CreateDoc(KeyFor(OrgA)), default);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.AddVersionAsync(doc.Id, OrgA, User, new AddDocumentVersionDto(KeyFor(OrgB), null, 10, null), default));

        Assert.Empty(await db.DocumentVersions.ToListAsync());
    }

    [Fact]
    public async Task AddVersion_WithOwnOrgKey_Succeeds()
    {
        using var db = Db(nameof(AddVersion_WithOwnOrgKey_Succeeds));
        var svc = new DocumentService(db);
        var doc = await svc.CreateDocumentAsync(OrgA, User, CreateDoc(KeyFor(OrgA)), default);
        var key = KeyFor(OrgA);

        var version = await svc.AddVersionAsync(
            doc.Id, OrgA, User, new AddDocumentVersionDto(key, null, 10, null), default);

        Assert.Equal(key, version.S3Key);
    }

    // ── Folder ownership ─────────────────────────────────────────────────────

    [Fact]
    public async Task CreateDocument_IntoAnotherOrgsFolder_Throws()
    {
        using var db = Db(nameof(CreateDocument_IntoAnotherOrgsFolder_Throws));
        var foreign = await SeedFolderAsync(db, OrgB);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            new DocumentService(db).CreateDocumentAsync(OrgA, User, CreateDoc(KeyFor(OrgA), foreign.Id), default));

        Assert.Empty(await db.Documents.ToListAsync());
    }

    [Fact]
    public async Task CreateFolder_UnderAnotherOrgsParent_Throws()
    {
        using var db = Db(nameof(CreateFolder_UnderAnotherOrgsParent_Throws));
        var foreign = await SeedFolderAsync(db, OrgB);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            new DocumentService(db).CreateFolderAsync(OrgA, User, new CreateFolderDto("Mine", foreign.Id), default));

        Assert.Empty(await db.DocumentFolders.Where(f => f.OrgId == OrgA).ToListAsync());
    }

    [Fact]
    public async Task CreateFolder_AtRoot_Succeeds()
    {
        using var db = Db(nameof(CreateFolder_AtRoot_Succeeds));

        var folder = await new DocumentService(db).CreateFolderAsync(OrgA, User, new CreateFolderDto("Mine", null), default);

        Assert.Equal("Mine", folder.Name);
        Assert.Null(folder.ParentId);
    }

    [Fact]
    public async Task UpdateFolder_ToAnotherOrgsParent_Throws()
    {
        using var db = Db(nameof(UpdateFolder_ToAnotherOrgsParent_Throws));
        var mine    = await SeedFolderAsync(db, OrgA);
        var foreign = await SeedFolderAsync(db, OrgB);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            new DocumentService(db).UpdateFolderAsync(mine.Id, OrgA, new UpdateFolderDto("Mine", foreign.Id), default));
    }

    [Fact]
    public async Task UpdateDocument_IntoAnotherOrgsFolder_Throws()
    {
        using var db = Db(nameof(UpdateDocument_IntoAnotherOrgsFolder_Throws));
        var svc     = new DocumentService(db);
        var doc     = await svc.CreateDocumentAsync(OrgA, User, CreateDoc(KeyFor(OrgA)), default);
        var foreign = await SeedFolderAsync(db, OrgB);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            svc.UpdateDocumentAsync(doc.Id, OrgA, new UpdateDocumentDto(null, foreign.Id, null, null, null), default));

        Assert.Null((await db.Documents.SingleAsync()).FolderId);
    }

    // ── Folder cycles ────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateFolder_ToItself_Throws()
    {
        using var db = Db(nameof(UpdateFolder_ToItself_Throws));
        var folder = await SeedFolderAsync(db, OrgA);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new DocumentService(db).UpdateFolderAsync(folder.Id, OrgA, new UpdateFolderDto("Compliance", folder.Id), default));
    }

    [Fact]
    public async Task UpdateFolder_ToItsOwnDescendant_Throws()
    {
        using var db = Db(nameof(UpdateFolder_ToItsOwnDescendant_Throws));
        var root  = await SeedFolderAsync(db, OrgA, null,      "root");
        var mid   = await SeedFolderAsync(db, OrgA, root.Id,   "mid");
        var leaf  = await SeedFolderAsync(db, OrgA, mid.Id,    "leaf");

        // root → leaf would orphan root/mid/leaf from every root listing.
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new DocumentService(db).UpdateFolderAsync(root.Id, OrgA, new UpdateFolderDto("root", leaf.Id), default));

        Assert.Null((await db.DocumentFolders.SingleAsync(f => f.Id == root.Id)).ParentId);
    }

    [Fact]
    public async Task UpdateFolder_ToUnrelatedFolder_Succeeds()
    {
        using var db = Db(nameof(UpdateFolder_ToUnrelatedFolder_Succeeds));
        var a = await SeedFolderAsync(db, OrgA, null, "a");
        var b = await SeedFolderAsync(db, OrgA, null, "b");

        var moved = await new DocumentService(db).UpdateFolderAsync(a.Id, OrgA, new UpdateFolderDto("a", b.Id), default);

        Assert.Equal(b.Id, moved.ParentId);
    }

    [Fact]
    public async Task UpdateFolder_ToRoot_Succeeds()
    {
        using var db = Db(nameof(UpdateFolder_ToRoot_Succeeds));
        var root  = await SeedFolderAsync(db, OrgA, null,    "root");
        var child = await SeedFolderAsync(db, OrgA, root.Id, "child");

        var moved = await new DocumentService(db).UpdateFolderAsync(child.Id, OrgA, new UpdateFolderDto("child", null), default);

        Assert.Null(moved.ParentId);
    }

    [Fact]
    public async Task UpdateFolder_WhenAncestryAlreadyCyclic_Throws_RatherThanHanging()
    {
        using var db = Db(nameof(UpdateFolder_WhenAncestryAlreadyCyclic_Throws_RatherThanHanging));

        // A cycle that predates the guard: x ↔ y, neither of which is the folder being moved.
        var x = await SeedFolderAsync(db, OrgA, null, "x");
        var y = await SeedFolderAsync(db, OrgA, x.Id, "y");
        x.ParentId = y.Id;
        await db.SaveChangesAsync();

        var mover = await SeedFolderAsync(db, OrgA, null, "mover");

        // The walk up from x never reaches `mover` and never reaches a root — the depth cap
        // is what stops it, so this must terminate rather than spin.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new DocumentService(db).UpdateFolderAsync(mover.Id, OrgA, new UpdateFolderDto("mover", x.Id), default));
    }
}
