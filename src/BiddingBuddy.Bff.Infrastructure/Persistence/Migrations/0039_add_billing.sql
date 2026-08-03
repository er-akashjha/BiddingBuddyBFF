-- 0039_add_billing
-- SaaS subscription billing: plan state per org, Razorpay payments, AI usage metering,
-- promo codes, and per-org entitlement overrides.
--
-- The plan CATALOG (prices, quotas, features) is code — Core/Billing/PlanCatalog.cs —
-- not rows here, for the same reason OrgRoles.cs holds the capability map: gates compile
-- against constants. These tables hold only per-org STATE.
--
-- Naming is deliberately billing_* / org_subscriptions: `invoices`, `orders` and
-- `payments` already belong to the TENDER domain (money the org bills government
-- buyers), which this must never be confused with.
--
-- Idempotent: IF NOT EXISTS / ON CONFLICT DO NOTHING throughout.

-- ── 1. org_subscriptions — one row per org, mutated in place ────────────────
-- History lives in billing_payments; this row is only "what plan is in force".
-- Entitlement resolution is DATE-driven (PlanService recomputes from trial_ends_at /
-- current_period_end on every read) — `status` is a label for the UI and the
-- lifecycle worker's email dedup, never the source of truth for access.

CREATE TABLE IF NOT EXISTS org_subscriptions (
  id                        UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  org_id                    UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  plan_code                 TEXT NOT NULL DEFAULT 'free',
  status                    TEXT NOT NULL DEFAULT 'active',
  trial_ends_at             TIMESTAMPTZ NULL,
  current_period_start      TIMESTAMPTZ NULL,
  current_period_end        TIMESTAMPTZ NULL,

  -- Lifecycle-worker email dedup stamps. Reset by a successful renewal payment.
  trial_ending_notified_at  TIMESTAMPTZ NULL,
  renewal_t14_notified_at   TIMESTAMPTZ NULL,
  renewal_t3_notified_at    TIMESTAMPTZ NULL,
  expiry_notified_at        TIMESTAMPTZ NULL,

  provider                  TEXT NOT NULL DEFAULT 'razorpay',
  created_at                TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at                TIMESTAMPTZ NULL,

  CONSTRAINT uq_org_subscriptions_org UNIQUE (org_id),
  CONSTRAINT ck_org_subscriptions_status
    CHECK (status IN ('trialing','active','past_due','expired','canceled'))
);

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'trg_org_subscriptions_updated_at') THEN
    CREATE TRIGGER trg_org_subscriptions_updated_at
      BEFORE UPDATE ON org_subscriptions
      FOR EACH ROW EXECUTE FUNCTION set_updated_at();
  END IF;
END $$;

-- ── 2. promo_codes ──────────────────────────────────────────────────────────
-- Managed via /internal/promo-codes only (no self-serve creation). Discount math
-- happens SERVER-side at checkout; the client never computes a price.

CREATE TABLE IF NOT EXISTS promo_codes (
  id               UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  -- Stored uppercase; lookups uppercase the input. TEXT (not citext) keeps it index-simple.
  code             TEXT NOT NULL,
  description      TEXT NULL,
  discount_type    TEXT NOT NULL,
  -- percent: 1–100 · flat: paise off the order
  discount_value   BIGINT NOT NULL,
  -- NULL = valid for every paid plan
  applies_to_plans TEXT[] NULL,
  max_redemptions  INT NULL,
  redeemed_count   INT NOT NULL DEFAULT 0,
  -- Lifetime codes re-apply automatically on every renewal checkout by an org that
  -- redeemed them (the "founding member 40% off for life" mechanism) — no autopay needed.
  is_lifetime      BOOLEAN NOT NULL DEFAULT FALSE,
  valid_from       TIMESTAMPTZ NULL,
  valid_until      TIMESTAMPTZ NULL,
  is_active        BOOLEAN NOT NULL DEFAULT TRUE,
  created_at       TIMESTAMPTZ NOT NULL DEFAULT now(),

  CONSTRAINT uq_promo_codes_code UNIQUE (code),
  CONSTRAINT ck_promo_codes_type CHECK (discount_type IN ('percent','flat')),
  CONSTRAINT ck_promo_codes_value CHECK (
    (discount_type = 'percent' AND discount_value BETWEEN 1 AND 100)
    OR (discount_type = 'flat' AND discount_value > 0)
  )
);

-- ── 3. billing_payments — one row per checkout attempt ──────────────────────
-- razorpay_order_id is UNIQUE: both the browser verify-callback and the webhook
-- activate through a FOR UPDATE on this row, which is what makes activation
-- race-safe and replay-safe.

CREATE TABLE IF NOT EXISTS billing_payments (
  id                     UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  org_id                 UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  plan_code              TEXT NOT NULL,
  cycle                  TEXT NOT NULL DEFAULT 'annual',
  -- amount_paise is what Razorpay was asked to charge (post-discount).
  amount_paise           BIGINT NOT NULL,
  currency               TEXT NOT NULL DEFAULT 'INR',
  -- Discount snapshot. Kept on the payment (not joined from promo_codes) so history
  -- stays truthful if the code is later edited or deleted.
  promo_code_id          UUID NULL REFERENCES promo_codes(id) ON DELETE SET NULL,
  original_amount_paise  BIGINT NULL,
  discount_paise         BIGINT NULL,
  razorpay_order_id      TEXT NULL,
  razorpay_payment_id    TEXT NULL,
  status                 TEXT NOT NULL DEFAULT 'created',
  signature_verified     BOOLEAN NOT NULL DEFAULT FALSE,
  raw_payload            JSONB NULL,
  created_by             UUID NULL REFERENCES users(id) ON DELETE SET NULL,
  created_at             TIMESTAMPTZ NOT NULL DEFAULT now(),
  captured_at            TIMESTAMPTZ NULL,

  CONSTRAINT uq_billing_payments_rzp_order UNIQUE (razorpay_order_id),
  CONSTRAINT ck_billing_payments_status CHECK (status IN ('created','captured','failed')),
  CONSTRAINT ck_billing_payments_cycle CHECK (cycle IN ('annual','monthly'))
);

CREATE INDEX IF NOT EXISTS idx_billing_payments_org_created
  ON billing_payments (org_id, created_at DESC);

-- ── 4. billing_webhook_events — Razorpay event dedup ────────────────────────
-- PK on the provider's event id: INSERT ... ON CONFLICT DO NOTHING; a conflict
-- means the event was already handled → return 200 without reprocessing.

CREATE TABLE IF NOT EXISTS billing_webhook_events (
  event_id     TEXT PRIMARY KEY,
  event_type   TEXT NOT NULL,
  payload      JSONB NULL,
  received_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
  processed_at TIMESTAMPTZ NULL
);

-- ── 5. promo_redemptions — one use per (code, org) ──────────────────────────

CREATE TABLE IF NOT EXISTS promo_redemptions (
  id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  promo_id            UUID NOT NULL REFERENCES promo_codes(id) ON DELETE CASCADE,
  org_id              UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  billing_payment_id  UUID NULL REFERENCES billing_payments(id) ON DELETE SET NULL,
  -- Copied from the code at redemption time so a later edit to the code can't
  -- silently revoke (or grant) a lifetime discount someone already earned.
  is_lifetime         BOOLEAN NOT NULL DEFAULT FALSE,
  redeemed_at         TIMESTAMPTZ NOT NULL DEFAULT now(),

  CONSTRAINT uq_promo_redemptions_promo_org UNIQUE (promo_id, org_id)
);

-- Renewal checkout auto-applies the org's best lifetime discount via this lookup.
CREATE INDEX IF NOT EXISTS idx_promo_redemptions_org_lifetime
  ON promo_redemptions (org_id) WHERE is_lifetime;

-- ── 6. ai_usage_records — per-org AI metering ───────────────────────────────
-- feature/resource_id are generic on purpose: v1 only writes feature='ai_summary'
-- with resource_id = the tender's Mongo id, but the same table carries any future
-- per-resource unlock (per-tender enrichment purchase, document Q&A, ...).
--
-- The UNIQUE constraint IS the consume operation: INSERT ... ON CONFLICT DO NOTHING,
-- rowcount 0 = already unlocked this month = free re-view. period_month is the IST
-- calendar month (quota "resets on the 1st", computed in AiQuotaService).

CREATE TABLE IF NOT EXISTS ai_usage_records (
  id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  org_id        UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  user_id       UUID NULL REFERENCES users(id) ON DELETE SET NULL,
  feature       TEXT NOT NULL,
  resource_id   TEXT NOT NULL,
  period_month  DATE NOT NULL,
  occurred_at   TIMESTAMPTZ NOT NULL DEFAULT now(),

  CONSTRAINT uq_ai_usage_org_feature_resource_month
    UNIQUE (org_id, feature, resource_id, period_month)
);

CREATE INDEX IF NOT EXISTS idx_ai_usage_org_feature_month
  ON ai_usage_records (org_id, feature, period_month);

-- ── 7. org_entitlement_overrides — manual comps ─────────────────────────────
-- Hand-written by ops (SQL / internal tooling). Merged over the code catalog by
-- PlanService. key examples: 'seat_cap', 'ai_quota', 'feature:competitors'.

CREATE TABLE IF NOT EXISTS org_entitlement_overrides (
  id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  org_id      UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
  key         TEXT NOT NULL,
  value       TEXT NOT NULL,
  note        TEXT NULL,
  expires_at  TIMESTAMPTZ NULL,
  created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),

  CONSTRAINT uq_org_entitlement_overrides_org_key UNIQUE (org_id, key)
);

-- ── 8. Notification templates ───────────────────────────────────────────────

INSERT INTO notification_templates (code, channel, name, subject, body, body_format, metadata)
VALUES
  ('PAYMENT_RECEIPT', 'Email',
   'Subscription payment receipt',
   'Payment received — {{PlanName}} plan for {{OrganizationName}}',
   '<p>Hi {{FirstName}},</p>'
   || '<p>We''ve received your payment for the <b>{{PlanName}}</b> plan.</p>'
   || '<table cellpadding="6" style="border-collapse:collapse">'
   || '<tr><td><b>Organization</b></td><td>{{OrganizationName}}</td></tr>'
   || '<tr><td><b>Plan</b></td><td>{{PlanName}} (annual)</td></tr>'
   || '<tr><td><b>Amount paid</b></td><td>₹{{AmountInr}}</td></tr>'
   || '{{#if DiscountInr}}<tr><td><b>Discount applied</b></td><td>−₹{{DiscountInr}} ({{PromoCode}})</td></tr>{{/if}}'
   || '<tr><td><b>Payment reference</b></td><td>{{PaymentRef}}</td></tr>'
   || '<tr><td><b>Active until</b></td><td>{{PeriodEnd}}</td></tr>'
   || '</table>'
   || '<p><a href="{{BillingUrl}}">View your billing details</a></p>'
   || '<hr><p style="color:#64748b;font-size:12px">This is a payment acknowledgement. A GST invoice, if applicable, follows separately.</p>',
   'Html',
   '{}'::jsonb),
  ('PAYMENT_RECEIPT', 'InApp',
   'Subscription payment receipt in-app',
   'Payment received — {{PlanName}} plan active',
   'Your {{PlanName}} plan for {{OrganizationName}} is active until {{PeriodEnd}}.',
   'Text',
   '{"actionUrl":"/billing"}'::jsonb),

  ('TRIAL_ENDING', 'Email',
   'Trial ending reminder',
   'Your Growth trial ends in {{DaysLeft}} days',
   '<p>Hi {{FirstName}},</p>'
   || '<p>Your 14-day <b>Growth</b> trial for <b>{{OrganizationName}}</b> ends on <b>{{TrialEndsAt}}</b>.</p>'
   || '<p>Keep real-time tender alerts, AI summaries and your bid workspace by choosing a plan — from ₹2,999/year (about ₹8 a day).</p>'
   || '<p><a href="{{BillingUrl}}">Choose your plan</a></p>'
   || '<p style="color:#64748b;font-size:12px">If you do nothing, your workspace moves to the Free plan. Your data stays safe — nothing is deleted.</p>',
   'Html',
   '{}'::jsonb),
  ('TRIAL_ENDING', 'InApp',
   'Trial ending in-app',
   'Your Growth trial ends in {{DaysLeft}} days',
   'Choose a plan to keep real-time alerts and AI summaries for {{OrganizationName}}.',
   'Text',
   '{"actionUrl":"/billing"}'::jsonb),

  ('RENEWAL_REMINDER', 'Email',
   'Subscription renewal reminder',
   'Your {{PlanName}} plan renews soon — {{DaysLeft}} days left',
   '<p>Hi {{FirstName}},</p>'
   || '<p>The <b>{{PlanName}}</b> plan for <b>{{OrganizationName}}</b> is paid up to <b>{{PeriodEnd}}</b>.</p>'
   || '<p>Renew now to keep alerts, AI summaries and your bid pipeline uninterrupted.</p>'
   || '<p><a href="{{BillingUrl}}">Renew your plan</a></p>'
   || '<p style="color:#64748b;font-size:12px">After the paid period there is a 7-day grace window before your workspace moves to the Free plan. Nothing is deleted.</p>',
   'Html',
   '{}'::jsonb),
  ('RENEWAL_REMINDER', 'InApp',
   'Subscription renewal in-app',
   '{{PlanName}} plan renews in {{DaysLeft}} days',
   'Renew before {{PeriodEnd}} to keep your plan uninterrupted.',
   'Text',
   '{"actionUrl":"/billing"}'::jsonb),

  ('PAYMENT_FAILED', 'Email',
   'Subscription payment failed',
   'Payment failed for the {{PlanName}} plan',
   '<p>Hi {{FirstName}},</p>'
   || '<p>A payment attempt for the <b>{{PlanName}}</b> plan for <b>{{OrganizationName}}</b> did not go through.</p>'
   || '<p>No money was taken and nothing changed on your account. You can retry anytime:</p>'
   || '<p><a href="{{BillingUrl}}">Retry payment</a></p>',
   'Html',
   '{}'::jsonb),
  ('PAYMENT_FAILED', 'InApp',
   'Subscription payment failed in-app',
   'Payment failed — {{PlanName}} plan',
   'The payment did not go through. No money was taken; you can retry from Billing.',
   'Text',
   '{"actionUrl":"/billing"}'::jsonb)
ON CONFLICT ON CONSTRAINT uq_template_code_channel DO NOTHING;

-- ── 9. Backfill: existing orgs start a 14-day Growth trial ──────────────────
-- Grandfathering existing orgs straight to 'free' would be a feature regression
-- with zero warning (they have full access today). The trial keeps their
-- experience unchanged for 14 days and TRIAL_ENDING doubles as the pricing
-- announcement. New orgs get the same row from OrganizationService.CreateAsync.

INSERT INTO org_subscriptions (org_id, plan_code, status, trial_ends_at)
SELECT id, 'growth', 'trialing', now() + interval '14 days'
FROM organizations
ON CONFLICT (org_id) DO NOTHING;

-- ── 10. Seed the founding-member code, INACTIVE ─────────────────────────────
-- Ops flips is_active at launch:
--   UPDATE promo_codes SET is_active = TRUE WHERE code = 'FOUNDING40';

INSERT INTO promo_codes (code, description, discount_type, discount_value,
                         max_redemptions, is_lifetime, is_active)
VALUES ('FOUNDING40', 'Founding member — lifetime 40% off (first 100 customers)',
        'percent', 40, 100, TRUE, FALSE)
ON CONFLICT ON CONSTRAINT uq_promo_codes_code DO NOTHING;
