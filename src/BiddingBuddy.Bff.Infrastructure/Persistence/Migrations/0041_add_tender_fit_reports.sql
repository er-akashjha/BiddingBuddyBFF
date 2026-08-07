-- 0041_add_tender_fit_reports
--
-- The paid artifact: one org's verdict on one tender. Supersedes `ai_analysis_results`, which
-- has an entity, a DTO, an upsert and a POST /internal/analysis endpoint — and, in the entire
-- history of the system, no caller. Every request to the analysis endpoint therefore spent a
-- credit and returned null, and the client painted invented filler over the hole.
--
-- WHY A NEW TABLE RATHER THAN FILLING THE OLD ONE:
--   * `ai_analysis_results` is five free-text blobs keyed by tender alone — GLOBAL, so it
--     cannot hold a per-org answer, which is the entire point of a fit verdict.
--   * a finding has to carry its source, its confidence and its citation or it is just prose
--     with a percentage attached. That is a structured shape, so `findings` is JSONB.
--
-- `ai_analysis_results` stays in place, dormant, for one release; dropping it is a follow-up
-- once nothing reads it.

CREATE TABLE IF NOT EXISTS tender_fit_reports (
  id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  org_id              UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  tender_id           UUID NOT NULL REFERENCES tenders(id) ON DELETE CASCADE,

  -- go | go_with_gaps | blocked | insufficient_data
  --
  -- Deliberately NOT a percentage. A number like "68% win probability" is precisely the figure
  -- a bidder acts on and precisely the one this data cannot support; `insufficient_data` is a
  -- first-class outcome so the engine can decline to answer instead of guessing.
  verdict             TEXT NOT NULL,
  verdict_reason      TEXT,

  -- FitFinding[] — see Core/Fit/FitFinding.cs. Each carries Source (tender_structured |
  -- tender_clause | org_profile | award_data | model) and Confidence, both of which the client
  -- renders. A finding sourced from a model is visually distinct from one sourced from a field.
  findings            JSONB NOT NULL DEFAULT '[]'::jsonb,

  -- Deterministic rupee total (EMD + ePBG exposure + tender fee + document prep), with MSE and
  -- startup exemptions applied. Null when the tender does not state enough to compute it.
  cost_to_bid         NUMERIC(18,2),
  cost_breakdown      JSONB,

  -- Pinned for the same reason TenderComplianceRules pins its own: re-rendering a stored report
  -- under a newer rule set produces a confidently wrong answer. The report shows this and its
  -- date, and a version mismatch is what makes it re-runnable rather than silently restated.
  rule_set_version    TEXT NOT NULL,

  -- Staleness inputs. A corrigendum can change eligibility outright, so a report computed
  -- before one is not merely old, it may be wrong. Compared against the tender's current
  -- values on every read; a drift banners the report and offers a FREE re-run (the customer
  -- must not pay again because the buyer amended the notice).
  profile_updated_at  TIMESTAMPTZ,
  tender_updated_at   TIMESTAMPTZ,
  corrigendum_count   INT,

  -- The one LLM-authored block. NULL until the async worker returns, and the report is complete
  -- and correct without it — the verdict above is decided by the rules engine, never by a model.
  -- pending | ready | failed | skipped
  narrative           TEXT,
  narrative_model     TEXT,
  narrative_state     TEXT NOT NULL DEFAULT 'pending',
  narrative_error     TEXT,

  generated_at        TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- One live report per (org, tender). A re-run overwrites — history of a verdict is not a
-- product requirement, and keeping every recomputation would make "is this stale?" ambiguous.
CREATE UNIQUE INDEX IF NOT EXISTS ux_tender_fit_reports_org_tender
  ON tender_fit_reports (org_id, tender_id);

CREATE INDEX IF NOT EXISTS idx_tender_fit_reports_org_generated
  ON tender_fit_reports (org_id, generated_at DESC);

-- Drives the async narrative worker's "what is still pending?" sweep.
CREATE INDEX IF NOT EXISTS idx_tender_fit_reports_narrative_pending
  ON tender_fit_reports (narrative_state)
  WHERE narrative_state = 'pending';

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'trg_tender_fit_reports_updated_at') THEN
    CREATE TRIGGER trg_tender_fit_reports_updated_at
      BEFORE UPDATE ON tender_fit_reports
      FOR EACH ROW EXECUTE FUNCTION set_updated_at();
  END IF;
END $$;
