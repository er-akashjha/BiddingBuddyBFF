-- 0026_add_bid_documents
-- Link already-uploaded org vault documents to a bid — the bid's "document folder".
--
-- Modelled as a many-to-many link table rather than a real document_folders row per bid,
-- because documents.folder_id is a single FK: filing a doc into a per-bid folder would
-- move it out of its vault folder (GST/PAN/…) and allow it on only ONE bid. Linking keeps
-- one GST certificate in the vault and lets every bid that needs it point at the same row,
-- so vault edits and new versions stay live on all linked bids.
--
-- org_id is denormalised from bids so the whole listing filters org-scoped without a join.
-- Both FKs cascade: unlinking is implicit when either the bid or the vault document dies.
--
-- Idempotent: safe to re-run.

CREATE TABLE IF NOT EXISTS bid_documents (
  id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  org_id      UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  bid_id      UUID NOT NULL REFERENCES bids(id)          ON DELETE CASCADE,
  document_id UUID NOT NULL REFERENCES documents(id)     ON DELETE CASCADE,
  linked_by   UUID NOT NULL REFERENCES users(id),
  created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- One link per (bid, document): re-linking the same doc is a no-op, not a duplicate row.
CREATE UNIQUE INDEX IF NOT EXISTS ux_bid_documents_bid_doc ON bid_documents (bid_id, document_id);
CREATE INDEX IF NOT EXISTS idx_bid_documents_bid ON bid_documents (bid_id);
CREATE INDEX IF NOT EXISTS idx_bid_documents_doc ON bid_documents (document_id);

-- The vault picker and the bid document list both filter documents by folder;
-- documents.folder_id has never been indexed (only org_id + expiry_date were).
CREATE INDEX IF NOT EXISTS idx_documents_folder ON documents (folder_id);
