using BiddingBuddy.Bff.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BiddingBuddy.Bff.Infrastructure.Persistence.Configurations;

public class TenderDocumentConfiguration : IEntityTypeConfiguration<TenderDocument>
{
    public void Configure(EntityTypeBuilder<TenderDocument> b)
    {
        b.ToTable("tender_documents");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.TenderId).HasColumnName("tender_id");
        b.Property(x => x.FileName).HasColumnName("file_name").IsRequired();
        b.Property(x => x.S3Key).HasColumnName("s3_key").IsRequired();
        b.Property(x => x.DocumentType).HasColumnName("document_type");
        b.Property(x => x.FileSizeKb).HasColumnName("file_size_kb");
        b.Property(x => x.ExtractedText).HasColumnName("extracted_text");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.HasIndex(x => x.TenderId);
    }
}

public class BidActivityConfiguration : IEntityTypeConfiguration<BidActivity>
{
    public void Configure(EntityTypeBuilder<BidActivity> b)
    {
        b.ToTable("bid_activities");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.BidId).HasColumnName("bid_id");
        b.Property(x => x.ActorId).HasColumnName("actor_id");
        b.Property(x => x.Action).HasColumnName("action").IsRequired();
        b.Property(x => x.FromValue).HasColumnName("from_value");
        b.Property(x => x.ToValue).HasColumnName("to_value");
        b.Property(x => x.Note).HasColumnName("note");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.HasIndex(x => x.BidId);
        b.HasOne(x => x.Actor).WithMany().HasForeignKey(x => x.ActorId);
    }
}

public class BidChecklistItemConfiguration : IEntityTypeConfiguration<BidChecklistItem>
{
    public void Configure(EntityTypeBuilder<BidChecklistItem> b)
    {
        b.ToTable("bid_checklist_items");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.BidId).HasColumnName("bid_id");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.Title).HasColumnName("title").IsRequired();
        b.Property(x => x.IsDone).HasColumnName("is_done").HasDefaultValue(false);
        b.Property(x => x.DueDate).HasColumnName("due_date");
        b.Property(x => x.AssignedTo).HasColumnName("assigned_to");
        b.Property(x => x.DoneAt).HasColumnName("done_at");
        b.Property(x => x.DoneBy).HasColumnName("done_by");
        b.Property(x => x.SortOrder).HasColumnName("sort_order").HasDefaultValue(0);
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.HasIndex(x => x.BidId);
    }
}

public class OrganizationInviteConfiguration : IEntityTypeConfiguration<OrganizationInvite>
{
    public void Configure(EntityTypeBuilder<OrganizationInvite> b)
    {
        b.ToTable("organization_invites");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.Email).HasColumnName("email").IsRequired();
        b.Property(x => x.Role).HasColumnName("role").IsRequired();
        b.Property(x => x.Department).HasColumnName("department");
        b.Property(x => x.InvitedBy).HasColumnName("invited_by");
        b.Property(x => x.TokenHash).HasColumnName("token_hash").IsRequired();
        b.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        b.Property(x => x.AcceptedAt).HasColumnName("accepted_at");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.HasIndex(x => x.TokenHash).IsUnique();
        // Partial unique index "one pending invite per (org, email)" lives in the
        // SQL migration — EF Core can't model a partial index in the fluent API.
        b.HasIndex(x => new { x.OrgId, x.Email });
        b.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrgId);
        b.HasOne(x => x.Inviter).WithMany().HasForeignKey(x => x.InvitedBy);
    }
}

public class PendingRegistrationConfiguration : IEntityTypeConfiguration<PendingRegistration>
{
    public void Configure(EntityTypeBuilder<PendingRegistration> b)
    {
        b.ToTable("pending_registrations");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.Email).HasColumnName("email").IsRequired();
        b.Property(x => x.Name).HasColumnName("name").IsRequired();
        b.Property(x => x.PasswordHash).HasColumnName("password_hash").IsRequired();
        b.Property(x => x.OrgName).HasColumnName("org_name");
        b.Property(x => x.Phone).HasColumnName("phone");
        b.Property(x => x.InviteToken).HasColumnName("invite_token");
        b.Property(x => x.CodeHash).HasColumnName("code_hash").IsRequired();
        b.Property(x => x.AttemptCount).HasColumnName("attempt_count").HasDefaultValue(0);
        b.Property(x => x.ResendCount).HasColumnName("resend_count").HasDefaultValue(0);
        b.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        b.Property(x => x.ConsumedAt).HasColumnName("consumed_at");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        // The partial unique "one active pending row per email" (WHERE consumed_at IS NULL)
        // lives in the SQL migration — EF Core can't model a partial index in the fluent API.
        b.HasIndex(x => x.Email);
    }
}

public class PasswordResetCodeConfiguration : IEntityTypeConfiguration<PasswordResetCode>
{
    public void Configure(EntityTypeBuilder<PasswordResetCode> b)
    {
        b.ToTable("password_reset_codes");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.UserId).HasColumnName("user_id");
        b.Property(x => x.CodeHash).HasColumnName("code_hash").IsRequired();
        b.Property(x => x.AttemptCount).HasColumnName("attempt_count").HasDefaultValue(0);
        b.Property(x => x.ResendCount).HasColumnName("resend_count").HasDefaultValue(0);
        b.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        b.Property(x => x.ConsumedAt).HasColumnName("consumed_at");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        // The partial unique "one active reset per user" (WHERE consumed_at IS NULL)
        // lives in the SQL migration — EF Core can't model a partial index in the fluent API.
        b.HasIndex(x => x.UserId);
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
    }
}

public class BidCommentConfiguration : IEntityTypeConfiguration<BidComment>
{
    public void Configure(EntityTypeBuilder<BidComment> b)
    {
        b.ToTable("bid_comments");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.BidId).HasColumnName("bid_id");
        b.Property(x => x.AuthorId).HasColumnName("author_id");
        b.Property(x => x.Body).HasColumnName("body").IsRequired();
        b.Property(x => x.ChecklistItemId).HasColumnName("checklist_item_id");
        b.Property(x => x.Kind).HasColumnName("kind").HasDefaultValue("comment");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        b.HasIndex(x => x.BidId);
        b.HasOne(x => x.Bid).WithMany(x => x.Comments).HasForeignKey(x => x.BidId);
        b.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorId);
    }
}

public class BidAttachmentConfiguration : IEntityTypeConfiguration<BidAttachment>
{
    public void Configure(EntityTypeBuilder<BidAttachment> b)
    {
        b.ToTable("bid_attachments");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.BidId).HasColumnName("bid_id");
        b.Property(x => x.ChecklistItemId).HasColumnName("checklist_item_id");
        b.Property(x => x.CommentId).HasColumnName("comment_id");
        b.Property(x => x.FileName).HasColumnName("file_name").IsRequired();
        b.Property(x => x.ContentType).HasColumnName("content_type").IsRequired();
        b.Property(x => x.SizeBytes).HasColumnName("size_bytes");
        b.Property(x => x.StorageKey).HasColumnName("storage_key").IsRequired();
        b.Property(x => x.UploadedBy).HasColumnName("uploaded_by");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.HasIndex(x => x.BidId);
        b.HasIndex(x => x.CommentId);
        b.HasOne(x => x.Bid).WithMany().HasForeignKey(x => x.BidId);
        b.HasOne(x => x.Uploader).WithMany().HasForeignKey(x => x.UploadedBy);
    }
}

public class BidDocumentConfiguration : IEntityTypeConfiguration<BidDocument>
{
    public void Configure(EntityTypeBuilder<BidDocument> b)
    {
        b.ToTable("bid_documents");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.BidId).HasColumnName("bid_id");
        b.Property(x => x.DocumentId).HasColumnName("document_id");
        b.Property(x => x.LinkedBy).HasColumnName("linked_by");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.HasIndex(x => new { x.BidId, x.DocumentId }).IsUnique();
        b.HasIndex(x => x.DocumentId);
        b.HasOne(x => x.Bid).WithMany().HasForeignKey(x => x.BidId);
        b.HasOne(x => x.Document).WithMany().HasForeignKey(x => x.DocumentId);
        b.HasOne(x => x.Linker).WithMany().HasForeignKey(x => x.LinkedBy);
    }
}

public class ComplianceRequirementConfiguration : IEntityTypeConfiguration<ComplianceRequirement>
{
    public void Configure(EntityTypeBuilder<ComplianceRequirement> b)
    {
        b.ToTable("compliance_requirements");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.Name).HasColumnName("name").IsRequired();
        b.Property(x => x.Description).HasColumnName("description");
        b.Property(x => x.Category).HasColumnName("category");
        b.Property(x => x.IsMandatory).HasColumnName("is_mandatory").HasDefaultValue(true);
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.HasIndex(x => x.OrgId);
        b.HasMany(x => x.Documents).WithOne(x => x.Requirement).HasForeignKey(x => x.RequirementId);
        b.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrgId);
    }
}

public class ComplianceDocumentConfiguration : IEntityTypeConfiguration<ComplianceDocument>
{
    public void Configure(EntityTypeBuilder<ComplianceDocument> b)
    {
        b.ToTable("compliance_documents");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.RequirementId).HasColumnName("requirement_id");
        b.Property(x => x.DocumentId).HasColumnName("document_id");
        b.Property(x => x.Status).HasColumnName("status").HasDefaultValue("pending");
        b.Property(x => x.ExpiryDate).HasColumnName("expiry_date");
        b.Property(x => x.VerifiedBy).HasColumnName("verified_by");
        b.Property(x => x.VerifiedAt).HasColumnName("verified_at");
        b.Property(x => x.Notes).HasColumnName("notes");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        b.HasIndex(x => x.OrgId);
        b.HasOne(x => x.Document).WithMany().HasForeignKey(x => x.DocumentId).IsRequired(false);
        b.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrgId);
    }
}

public class DocumentFolderConfiguration : IEntityTypeConfiguration<DocumentFolder>
{
    public void Configure(EntityTypeBuilder<DocumentFolder> b)
    {
        b.ToTable("document_folders");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.Name).HasColumnName("name").IsRequired();
        b.Property(x => x.ParentId).HasColumnName("parent_id");
        b.Property(x => x.CreatedBy).HasColumnName("created_by");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.HasIndex(x => x.OrgId);
        b.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrgId);
        b.HasOne(x => x.Parent).WithMany(x => x.Children).HasForeignKey(x => x.ParentId).IsRequired(false);
    }
}

public class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> b)
    {
        b.ToTable("documents");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.FolderId).HasColumnName("folder_id");
        b.Property(x => x.Name).HasColumnName("name").IsRequired();
        b.Property(x => x.FileName).HasColumnName("file_name").IsRequired();
        b.Property(x => x.S3Key).HasColumnName("s3_key").IsRequired();
        b.Property(x => x.S3VersionId).HasColumnName("s3_version_id");
        b.Property(x => x.FileSizeKb).HasColumnName("file_size_kb");
        b.Property(x => x.MimeType).HasColumnName("mime_type");
        b.Property(x => x.DocumentType).HasColumnName("document_type");
        b.Property(x => x.ExpiryDate).HasColumnName("expiry_date");
        b.Property(x => x.Tags).HasColumnName("tags").HasColumnType("text[]");
        b.Property(x => x.HealthScore).HasColumnName("health_score");
        b.Property(x => x.UploadedBy).HasColumnName("uploaded_by");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        b.HasIndex(x => x.OrgId);
        b.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrgId);
        b.HasOne(x => x.Folder).WithMany(x => x.Documents).HasForeignKey(x => x.FolderId).IsRequired(false);
        b.HasMany(x => x.Versions).WithOne(x => x.Document).HasForeignKey(x => x.DocumentId);
    }
}

public class DocumentVersionConfiguration : IEntityTypeConfiguration<DocumentVersion>
{
    public void Configure(EntityTypeBuilder<DocumentVersion> b)
    {
        b.ToTable("document_versions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.DocumentId).HasColumnName("document_id");
        b.Property(x => x.VersionNum).HasColumnName("version_num");
        b.Property(x => x.S3Key).HasColumnName("s3_key").IsRequired();
        b.Property(x => x.S3VersionId).HasColumnName("s3_version_id");
        b.Property(x => x.FileSizeKb).HasColumnName("file_size_kb");
        b.Property(x => x.UploadedBy).HasColumnName("uploaded_by");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.HasIndex(x => x.DocumentId);
    }
}

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.ToTable("orders");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.BidId).HasColumnName("bid_id");
        b.Property(x => x.TenderId).HasColumnName("tender_id");
        b.Property(x => x.GemOrderId).HasColumnName("gem_order_id");
        b.Property(x => x.OrderNumber).HasColumnName("order_number");
        b.Property(x => x.BuyerOrg).HasColumnName("buyer_org");
        b.Property(x => x.OrderDate).HasColumnName("order_date");
        b.Property(x => x.DeliveryDate).HasColumnName("delivery_date");
        b.Property(x => x.TotalValue).HasColumnName("total_value").HasPrecision(15, 2);
        b.Property(x => x.Status).HasColumnName("status").HasDefaultValue("received");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        b.HasIndex(x => x.GemOrderId).IsUnique().HasFilter("gem_order_id IS NOT NULL");
        b.HasMany(x => x.Items).WithOne(x => x.Order).HasForeignKey(x => x.OrderId);
        b.HasMany(x => x.Milestones).WithOne(x => x.Order).HasForeignKey(x => x.OrderId);
        b.HasMany(x => x.Invoices).WithOne(x => x.Order).HasForeignKey(x => x.OrderId);
        b.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrgId);
        b.HasOne(x => x.Bid).WithMany().HasForeignKey(x => x.BidId).IsRequired(false);
        b.HasOne(x => x.Tender).WithMany().HasForeignKey(x => x.TenderId).IsRequired(false);
    }
}

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> b)
    {
        b.ToTable("order_items");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.OrderId).HasColumnName("order_id");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.Description).HasColumnName("description").IsRequired();
        b.Property(x => x.Quantity).HasColumnName("quantity");
        b.Property(x => x.UnitPrice).HasColumnName("unit_price").HasPrecision(15, 2);
        b.Property(x => x.TotalPrice).HasColumnName("total_price").HasPrecision(15, 2);
        b.Property(x => x.HsnCode).HasColumnName("hsn_code");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.HasIndex(x => x.OrderId);
    }
}

public class DeliveryMilestoneConfiguration : IEntityTypeConfiguration<DeliveryMilestone>
{
    public void Configure(EntityTypeBuilder<DeliveryMilestone> b)
    {
        b.ToTable("delivery_milestones");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.OrderId).HasColumnName("order_id");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.Title).HasColumnName("title").IsRequired();
        b.Property(x => x.DueDate).HasColumnName("due_date");
        b.Property(x => x.CompletedAt).HasColumnName("completed_at");
        b.Property(x => x.Notes).HasColumnName("notes");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.HasIndex(x => x.OrderId);
    }
}

public class EmdPaymentConfiguration : IEntityTypeConfiguration<EmdPayment>
{
    public void Configure(EntityTypeBuilder<EmdPayment> b)
    {
        b.ToTable("emd_payments");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.BidId).HasColumnName("bid_id");
        b.Property(x => x.TenderId).HasColumnName("tender_id");
        b.Property(x => x.GemTenderId).HasColumnName("gem_tender_id");
        b.Property(x => x.TenderTitle).HasColumnName("tender_title");
        b.Property(x => x.Amount).HasColumnName("amount").HasPrecision(15, 2);
        b.Property(x => x.PaymentDate).HasColumnName("payment_date");
        b.Property(x => x.PaymentMode).HasColumnName("payment_mode");
        b.Property(x => x.TransactionRef).HasColumnName("transaction_ref");
        b.Property(x => x.BankName).HasColumnName("bank_name");
        b.Property(x => x.Status).HasColumnName("status").HasDefaultValue("held");
        b.Property(x => x.RefundDate).HasColumnName("refund_date");
        b.Property(x => x.RefundAmount).HasColumnName("refund_amount").HasPrecision(15, 2);
        b.Property(x => x.RefundRef).HasColumnName("refund_ref");
        b.Property(x => x.Notes).HasColumnName("notes");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        // Instrument details (migration 0029)
        b.Property(x => x.InstrumentNumber).HasColumnName("instrument_number");
        b.Property(x => x.InstrumentDate).HasColumnName("instrument_date");
        b.Property(x => x.ValidUntil).HasColumnName("valid_until");
        b.Property(x => x.IssuingBranch).HasColumnName("issuing_branch");
        b.Property(x => x.Favouring).HasColumnName("favouring");
        b.Property(x => x.DueDate).HasColumnName("due_date");
        b.Property(x => x.DocumentId).HasColumnName("document_id");

        b.HasOne(x => x.Organization).WithMany(x => x.EmdPayments).HasForeignKey(x => x.OrgId);
        b.HasOne(x => x.Bid).WithMany().HasForeignKey(x => x.BidId).IsRequired(false);
        b.HasOne(x => x.Tender).WithMany().HasForeignKey(x => x.TenderId).IsRequired(false);
        b.HasOne(x => x.Document).WithMany().HasForeignKey(x => x.DocumentId).IsRequired(false);
    }
}

/// <summary>
/// The courier leg of an EMD instrument (migration 0029). See <see cref="BidDispatch"/> for
/// why this is its own table rather than columns on <c>emd_payments</c>.
/// </summary>
public class BidDispatchConfiguration : IEntityTypeConfiguration<BidDispatch>
{
    public void Configure(EntityTypeBuilder<BidDispatch> b)
    {
        b.ToTable("bid_dispatches");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.BidId).HasColumnName("bid_id");
        b.Property(x => x.EmdPaymentId).HasColumnName("emd_payment_id");
        b.Property(x => x.Purpose).HasColumnName("purpose").HasDefaultValue("emd_instrument");
        b.Property(x => x.Direction).HasColumnName("direction").HasDefaultValue("outbound");
        b.Property(x => x.CourierName).HasColumnName("courier_name");
        b.Property(x => x.TrackingNumber).HasColumnName("tracking_number");
        b.Property(x => x.TrackingUrl).HasColumnName("tracking_url");
        b.Property(x => x.DispatchedOn).HasColumnName("dispatched_on");
        b.Property(x => x.DispatchedBy).HasColumnName("dispatched_by");
        b.Property(x => x.RecipientName).HasColumnName("recipient_name");
        b.Property(x => x.RecipientDesignation).HasColumnName("recipient_designation");
        b.Property(x => x.RecipientAddress).HasColumnName("recipient_address");
        b.Property(x => x.RecipientPhone).HasColumnName("recipient_phone");
        b.Property(x => x.DeliverBy).HasColumnName("deliver_by");
        b.Property(x => x.ExpectedDeliveryOn).HasColumnName("expected_delivery_on");
        b.Property(x => x.DeliveredOn).HasColumnName("delivered_on");
        b.Property(x => x.ReceivedBy).HasColumnName("received_by");
        b.Property(x => x.Status).HasColumnName("status").HasDefaultValue("draft");
        b.Property(x => x.PodDocumentId).HasColumnName("pod_document_id");
        b.Property(x => x.Notes).HasColumnName("notes");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        b.HasIndex(x => x.BidId);
        b.HasIndex(x => x.OrgId);

        b.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrgId);
        b.HasOne(x => x.Bid).WithMany(x => x.Dispatches).HasForeignKey(x => x.BidId);
        b.HasOne(x => x.EmdPayment).WithMany(x => x.Dispatches).HasForeignKey(x => x.EmdPaymentId).IsRequired(false);
        b.HasOne(x => x.Dispatcher).WithMany().HasForeignKey(x => x.DispatchedBy).IsRequired(false);
        b.HasOne(x => x.PodDocument).WithMany().HasForeignKey(x => x.PodDocumentId).IsRequired(false);
    }
}

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> b)
    {
        b.ToTable("invoices");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.OrderId).HasColumnName("order_id");
        b.Property(x => x.InvoiceNumber).HasColumnName("invoice_number");
        b.Property(x => x.BuyerOrg).HasColumnName("buyer_org");
        b.Property(x => x.Amount).HasColumnName("amount").HasPrecision(15, 2);
        b.Property(x => x.GstAmount).HasColumnName("gst_amount").HasPrecision(15, 2);
        b.Property(x => x.TotalAmount).HasColumnName("total_amount").HasPrecision(15, 2);
        b.Property(x => x.InvoiceDate).HasColumnName("invoice_date");
        b.Property(x => x.DueDate).HasColumnName("due_date");
        b.Property(x => x.PaidDate).HasColumnName("paid_date");
        b.Property(x => x.PaidAmount).HasColumnName("paid_amount").HasPrecision(15, 2);
        b.Property(x => x.Status).HasColumnName("status").HasDefaultValue("pending");
        b.Property(x => x.PaymentRef).HasColumnName("payment_ref");
        b.Property(x => x.Notes).HasColumnName("notes");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        b.HasOne(x => x.Organization).WithMany(x => x.Invoices).HasForeignKey(x => x.OrgId);
    }
}

public class CompetitorConfiguration : IEntityTypeConfiguration<Competitor>
{
    public void Configure(EntityTypeBuilder<Competitor> b)
    {
        b.ToTable("competitors");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.CompanyName).HasColumnName("company_name").IsRequired();
        b.Property(x => x.GemSellerId).HasColumnName("gem_seller_id");
        b.Property(x => x.Tier).HasColumnName("tier");
        b.Property(x => x.ThreatLevel).HasColumnName("threat_level");
        b.Property(x => x.WinRate).HasColumnName("win_rate").HasPrecision(5, 2);
        b.Property(x => x.TotalContracts).HasColumnName("total_contracts").HasDefaultValue(0);
        b.Property(x => x.TotalWinValue).HasColumnName("total_win_value").HasPrecision(15, 2);
        b.Property(x => x.AvgBidValue).HasColumnName("avg_bid_value").HasPrecision(15, 2);
        b.Property(x => x.ActiveStates).HasColumnName("active_states").HasColumnType("text[]");
        b.Property(x => x.ActiveCategories).HasColumnName("active_categories").HasColumnType("text[]");
        b.Property(x => x.FirstSeenAt).HasColumnName("first_seen_at");
        b.Property(x => x.LastSeenAt).HasColumnName("last_seen_at");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        b.HasIndex(x => new { x.OrgId, x.CompanyName }).IsUnique();
        b.HasOne(x => x.Organization).WithMany(x => x.Competitors).HasForeignKey(x => x.OrgId);
        b.HasMany(x => x.BidObservations).WithOne(x => x.Competitor).HasForeignKey(x => x.CompetitorId);
    }
}

public class CompetitorBidObservationConfiguration : IEntityTypeConfiguration<CompetitorBidObservation>
{
    public void Configure(EntityTypeBuilder<CompetitorBidObservation> b)
    {
        b.ToTable("competitor_bid_observations");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.CompetitorId).HasColumnName("competitor_id");
        b.Property(x => x.TenderId).HasColumnName("tender_id");
        b.Property(x => x.GemTenderId).HasColumnName("gem_tender_id").IsRequired();
        b.Property(x => x.ObservedBidValue).HasColumnName("observed_bid_value").HasPrecision(15, 2);
        b.Property(x => x.WasWinner).HasColumnName("was_winner").HasDefaultValue(false);
        b.Property(x => x.AwardedValue).HasColumnName("awarded_value").HasPrecision(15, 2);
        b.Property(x => x.ObservedDate).HasColumnName("observed_date");
        b.Property(x => x.RawData).HasColumnName("raw_data").HasColumnType("jsonb");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.HasOne(x => x.Tender).WithMany().HasForeignKey(x => x.TenderId).IsRequired(false);
    }
}

public class AiAnalysisResultConfiguration : IEntityTypeConfiguration<AiAnalysisResult>
{
    public void Configure(EntityTypeBuilder<AiAnalysisResult> b)
    {
        b.ToTable("ai_analysis_results");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.TenderId).HasColumnName("tender_id");
        b.Property(x => x.ModelUsed).HasColumnName("model_used");
        b.Property(x => x.EligibilityBreakdown).HasColumnName("eligibility_breakdown").HasColumnType("jsonb");
        b.Property(x => x.RiskFactors).HasColumnName("risk_factors").HasColumnType("jsonb");
        b.Property(x => x.WinStrategy).HasColumnName("win_strategy");
        b.Property(x => x.SuggestedBidRange).HasColumnName("suggested_bid_range").HasColumnType("jsonb");
        b.Property(x => x.RequiredDocuments).HasColumnName("required_documents").HasColumnType("text[]");
        b.Property(x => x.KeyClauses).HasColumnName("key_clauses").HasColumnType("text[]");
        b.Property(x => x.RawResponse).HasColumnName("raw_response");
        b.Property(x => x.GeneratedAt).HasColumnName("generated_at").HasDefaultValueSql("NOW()");
        b.HasIndex(x => x.TenderId).IsUnique();
    }
}

// In-app notification inbox (was "Notification"). Renamed to UserNotification
// after the notification subsystem took the "notifications" table for dispatch events.
public class UserNotificationConfiguration : IEntityTypeConfiguration<UserNotification>
{
    public void Configure(EntityTypeBuilder<UserNotification> b)
    {
        b.ToTable("user_notifications");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.UserId).HasColumnName("user_id");
        b.Property(x => x.Type).HasColumnName("type").IsRequired();
        b.Property(x => x.Title).HasColumnName("title").IsRequired();
        b.Property(x => x.Body).HasColumnName("body");
        b.Property(x => x.EntityType).HasColumnName("entity_type");
        b.Property(x => x.EntityId).HasColumnName("entity_id");
        b.Property(x => x.IsRead).HasColumnName("is_read").HasDefaultValue(false);
        b.Property(x => x.ReadAt).HasColumnName("read_at");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.HasIndex(x => new { x.UserId, x.IsRead });
        // Without these, EF invents a shadow "OrganizationId" FK column and full-entity
        // reads fail with 42703 (the org_id column is the real FK).
        b.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrgId);
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
    }
}

// ── Notification subsystem (dispatch event + per-channel deliveries + audit log) ─

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ToTable("notifications");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.Category).HasColumnName("category").IsRequired().HasMaxLength(20);
        b.Property(x => x.TemplateCode).HasColumnName("template_code").IsRequired().HasMaxLength(100);
        b.Property(x => x.UserId).HasColumnName("user_id");
        b.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        b.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.HasMany(x => x.Deliveries).WithOne(x => x.Notification).HasForeignKey(x => x.NotificationId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class NotificationDeliveryConfiguration : IEntityTypeConfiguration<NotificationDelivery>
{
    public void Configure(EntityTypeBuilder<NotificationDelivery> b)
    {
        b.ToTable("notification_deliveries");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.NotificationId).HasColumnName("notification_id");
        b.Property(x => x.Channel).HasColumnName("channel").IsRequired().HasMaxLength(20);
        b.Property(x => x.RecipientAddress).HasColumnName("recipient_address").IsRequired().HasMaxLength(500);
        b.Property(x => x.Status).HasColumnName("status").IsRequired().HasMaxLength(20).HasDefaultValue("Pending");
        b.Property(x => x.RetryCount).HasColumnName("retry_count").HasDefaultValue(0);
        b.Property(x => x.MaxRetries).HasColumnName("max_retries").HasDefaultValue(5);
        b.Property(x => x.NextRetryAt).HasColumnName("next_retry_at");
        b.Property(x => x.LockedAt).HasColumnName("locked_at");
        b.Property(x => x.LockedBy).HasColumnName("locked_by").HasMaxLength(100);
        b.Property(x => x.LastError).HasColumnName("last_error");
        b.Property(x => x.Version).HasColumnName("version").HasDefaultValue(0);
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.ProcessedAt).HasColumnName("processed_at");
        b.Property(x => x.FailedAt).HasColumnName("failed_at");
        b.HasIndex(x => new { x.NotificationId, x.Channel }).IsUnique();
        b.HasIndex(x => x.NotificationId);
    }
}

public class NotificationTemplateConfiguration : IEntityTypeConfiguration<NotificationTemplate>
{
    public void Configure(EntityTypeBuilder<NotificationTemplate> b)
    {
        b.ToTable("notification_templates");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.Code).HasColumnName("code").IsRequired().HasMaxLength(100);
        b.Property(x => x.Channel).HasColumnName("channel").IsRequired().HasMaxLength(20);
        b.Property(x => x.Name).HasColumnName("name").IsRequired().HasMaxLength(200);
        b.Property(x => x.Subject).HasColumnName("subject").HasMaxLength(500);
        b.Property(x => x.Body).HasColumnName("body").IsRequired();
        b.Property(x => x.BodyFormat).HasColumnName("body_format").IsRequired().HasMaxLength(20).HasDefaultValue("Html");
        b.Property(x => x.Metadata).HasColumnName("metadata").HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        b.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        b.HasIndex(x => new { x.Code, x.Channel }).IsUnique();
    }
}

public class NotificationLogConfiguration : IEntityTypeConfiguration<NotificationLog>
{
    public void Configure(EntityTypeBuilder<NotificationLog> b)
    {
        // Read-only from BFF's perspective; map for completeness so admin queries can read it.
        b.ToTable("notification_logs");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.DeliveryId).HasColumnName("delivery_id");
        b.Property(x => x.NotificationId).HasColumnName("notification_id");
        b.Property(x => x.Channel).HasColumnName("channel").IsRequired().HasMaxLength(20);
        b.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(100);
        b.Property(x => x.RecipientAddress).HasColumnName("recipient_address").HasMaxLength(500);
        b.Property(x => x.Subject).HasColumnName("subject").HasMaxLength(500);
        b.Property(x => x.Status).HasColumnName("status").IsRequired().HasMaxLength(20);
        b.Property(x => x.ProviderMessageId).HasColumnName("provider_message_id").HasMaxLength(255);
        b.Property(x => x.AttemptNumber).HasColumnName("attempt_number");
        b.Property(x => x.ErrorMessage).HasColumnName("error_message");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.CompletedAt).HasColumnName("completed_at");
        b.HasIndex(x => x.DeliveryId);
        b.HasIndex(x => x.NotificationId);
    }
}

public class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> b)
    {
        b.ToTable("notification_preferences");
        b.HasKey(x => new { x.UserId, x.OrgId, x.Channel });
        b.Property(x => x.UserId).HasColumnName("user_id");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.Channel).HasColumnName("channel").HasDefaultValue("in_app");
        b.Property(x => x.EventTypes).HasColumnName("event_types").HasColumnType("text[]");
        b.Property(x => x.IsEnabled).HasColumnName("is_enabled").HasDefaultValue(true);
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        b.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrgId);
    }
}

public class GemIntegrationConfiguration : IEntityTypeConfiguration<GemIntegration>
{
    public void Configure(EntityTypeBuilder<GemIntegration> b)
    {
        b.ToTable("gem_integrations");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.GemSellerId).HasColumnName("gem_seller_id").IsRequired();
        b.Property(x => x.GemUsername).HasColumnName("gem_username");
        b.Property(x => x.SyncEnabled).HasColumnName("sync_enabled").HasDefaultValue(false);
        b.Property(x => x.LastSyncedAt).HasColumnName("last_synced_at");
        b.Property(x => x.SyncStatus).HasColumnName("sync_status").HasDefaultValue("idle");
        b.Property(x => x.SyncError).HasColumnName("sync_error");
        b.Property(x => x.Preferences).HasColumnName("preferences").HasColumnType("jsonb");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        b.HasIndex(x => x.OrgId).IsUnique();
        b.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrgId);
    }
}

public class OrgPerformanceSnapshotConfiguration : IEntityTypeConfiguration<OrgPerformanceSnapshot>
{
    public void Configure(EntityTypeBuilder<OrgPerformanceSnapshot> b)
    {
        b.ToTable("org_performance_snapshots");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.SnapshotDate).HasColumnName("snapshot_date");
        b.Property(x => x.TotalBids).HasColumnName("total_bids");
        b.Property(x => x.BidsWon).HasColumnName("bids_won");
        b.Property(x => x.BidsLost).HasColumnName("bids_lost");
        b.Property(x => x.WinRate).HasColumnName("win_rate").HasPrecision(5, 2);
        b.Property(x => x.TotalBidValue).HasColumnName("total_bid_value").HasPrecision(15, 2);
        b.Property(x => x.WonValue).HasColumnName("won_value").HasPrecision(15, 2);
        b.Property(x => x.AvgBidValue).HasColumnName("avg_bid_value").HasPrecision(15, 2);
        b.Property(x => x.TopCategories).HasColumnName("top_categories").HasColumnType("jsonb");
        b.Property(x => x.TopStates).HasColumnName("top_states").HasColumnType("jsonb");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.HasIndex(x => new { x.OrgId, x.SnapshotDate }).IsUnique();
        b.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrgId);
    }
}
