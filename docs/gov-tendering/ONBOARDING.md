# Onboarding Buyers — How We Actually Acquire a Publishing Entity

**Annex to** [PLAN.md](PLAN.md), [SECURITY-AND-FAIRNESS.md](SECURITY-AND-FAIRNESS.md), and BST-PROP-01.
**Status:** DESIGN ONLY. Written 2026-07-20.

> This annex exists because the proposal named a beachhead segment but never said how we
> acquire one. Gate A is defined as "≥5 entities publishing" with no motion behind it.

---

## 1. Readiness verdict

**The documents are ready to circulate. The plan is not ready to build.**

| Deliverable | State |
|---|---|
| Board proposal + deck | **Ready to send.** Internally consistent, evidence marked, risks disclosed |
| Regulatory position | **Ready.** Rule 160 path verified; STQC regime documented |
| Security & fairness design | **Ready as a design.** Not reviewed by any external security practitioner |
| **Engineering plan** | **Not ready.** See blockers below |

### Blockers before engineering starts

| # | Blocker | Owner |
|---|---|---|
| 1 | **Information barrier decision** (SECURITY §5) — shapes the Phase 1 data model; retrofitting is far harder | Management |
| 2 | **Secrets remediation** — 22 live credentials in tracked config; overdue independently of this product | Engineering |
| 3 | **No entity verification design** — was missing entirely; drafted below (§4). It is a fraud control, not a signup step | Product |
| 4 | **Gate A is likely unachievable as written** — see §6 | Management |
| 5 | **Buyer-side pain unvalidated** — no procurement officer has reviewed any of this | Product |
| 6 | **No pricing research** | Product |

Items 1–4 are cheap to resolve and none require engineering. **Item 3 was a genuine hole**: the
plan would have let an unverified party publish a tender to our supplier base.

---

## 2. The central paradox

**To sell a tendering platform to a government department, they may have to run a tender to
buy it — and that tender would likely demand STQC certification we will not hold until Phase 3.**

This is the single hardest fact about this business, and the proposal did not address it.
Restated plainly:

- We are pre-certification in Phases 1–2 **by design** (that is what makes them cheap).
- Any procurement large enough to require competitive bidding will likely list certification
  as a qualification criterion, because the DoE O.M. pushes that obligation onto the buyer.
- Therefore **any Phase 1 acquisition motion that requires a competitive procurement is a
  motion we lose.**

Every route below is a way around that, not through it.

---

## 3. Five acquisition routes

Ranked by how fast they can produce a live publishing entity.

### Route A — Free in Phase 1 *(recommended primary)*

If there is no payment, there is generally no procurement to run. A department can accept a
free service on far lighter approval than a paid one.

This is not merely a discount — **it removes the qualification gate entirely** and is the only
route that reliably works pre-certification. It also matches what Phase 1 actually is: an
information-gathering exercise whose product is five real departments and their pain, not
revenue.

*Consequence: it breaks Gate A as written. See §6.*

### Route B — Price below the direct-purchase threshold

GFR 2017 permits purchase without quotation below a stated value, and via a purchase committee
in a band above that. Pricing an annual licence under the direct-purchase ceiling lets an
officer buy on their own authority.

> ⚠️ **The exact current thresholds are NOT verified.** The salvaged research contains the rule
> numbers (154, 155, 161) but no amounts, and these have been amended since 2017. **Confirm the
> current figures with counsel in Phase 0** before pricing anything around them.

### Route C — Entities outside the central GFR regime *(our stated beachhead)*

ULBs, universities, autonomous bodies, cooperatives, trusts and state PSUs set their own
purchase rules, which are frequently more permissive. This is already the beachhead in the
proposal; what is new here is the reason — **not just that they are underserved, but that they
can actually buy.**

### Route D — Empanelment with a state IT agency

Get onto a panel (CHiPS, KEONICS and equivalents) and member departments can buy without
individual tenders. This is the standard scaling route and it is how incumbents grew.

Currently listed as a **Phase 3** item in the compliance register. **It probably needs to move
earlier** — empanelment lead times run to months, and it is the bridge between pilot and
volume. Note some panels themselves require certification; verify per panel.

### Route E — List on GeM *(worth testing early)*

Departments buy software and services on GeM routinely. Listing our own SaaS there means the
department's purchase is already compliant by construction — they are buying on the mandated
channel.

The irony is real and it is also the point: **the fastest compliant way to sell a GeM
alternative may be to sell it through GeM.** Low cost to test, and it sidesteps the paradox in
§2 entirely for the *purchase* of our product.

---

## 4. Entity verification — a fraud control, not a signup step

**This was missing and it matters more than the rest of this document.**

Fake-tender fraud is an established scam in India: a fraudster publishes a fabricated tender
and collects EMD and document fees from suppliers. If we let unverified parties publish, we do
not merely host fraud — **we distribute it to our own supplier base through our own alerting
rail, defrauding the customers our existing revenue depends on.**

That converts a signup-quality problem into an existential trust problem. Verification is
therefore a security control and belongs under the same scrutiny as bid sealing.

### Verification tiers

| Signal | Strength | Note |
|---|---|---|
| **Class 3 organisational DSC** | **Strongest** | Bound to a legal entity, verified by a CCA-licensed CA. Already in our stack |
| Authorisation letter from the Head of Department, on letterhead | Strong | Names the officer and their authority to publish |
| `.gov.in` / `.nic.in` email | Moderate | Necessary-ish, not sufficient. **Many ULBs, universities and cooperatives do not have one** — do not make it a hard gate or we exclude the beachhead |
| Callback to a number published on the entity's official site | Moderate | Cheap and effective against impersonation |
| Cross-check against a public directory of government bodies | Moderate | |
| Payment instrument | Weak | Free tier means there often isn't one |

### Rules for Phase 1

1. **Manual verification of every entity.** There are only five. Do not automate this early;
   the checklist is the product of the pilot.
2. **A human reviews the first tender each new entity publishes**, before it goes out. Real
   operational cost, and it is the difference between a trustworthy platform and a fraud vector.
3. **No self-serve publishing for a government entity, ever** — at minimum a verified human
   gate before first publication.
4. Record who verified, how, and when. That record is itself an audit artefact.

---

## 5. The onboarding motion

Government onboarding is **not** onboarding a user. It is onboarding a **committee** — the five
GePNIC roles across separate people — and standing up a governance structure.

| Stage | What happens | Owner |
|---|---|---|
| **1. Identify** | Target entity in the beachhead segment; find the officer with publishing authority | Sales |
| **2. Verify** | §4 checks; record the evidence | Ops |
| **3. Approve** | Their IT/security sign-off and, if paid, financial approval | Their side |
| **4. Configure** | Create the entity; enrol the committee — creator, publisher, openers, evaluators, auditor. Enforce the no-two-conflicting-roles rule | Ops + them |
| **5. Import** | Ingest their last 10–20 published tenders; auto-generate their clause library and templates (§7) | Automated |
| **6. Pilot** | One low-value, low-risk tender end-to-end, reviewed by us before publication | Joint |
| **7. Live** | Subsequent tenders self-served within the verified entity | Them |

### Two things that must exist from day one

- **Officer handover.** Transfers are constant in government. If a publisher moves and their
  access dies with them, the entity stalls and we get blamed. Handover must be a first-class,
  audited action — never credential sharing, which the STQC guidelines expressly prohibit.
- **A named human on our side.** Self-serve support does not work for a first government
  customer with vigilance anxiety. Budget for it in the pilot; it is not a permanent cost
  structure but it is a real Phase 1 one.

---

## 6. Gate A is probably unachievable as written

The proposal sets Gate A at **"≥5 entities publishing and ≥1 paying."**

If Route A (free) is the primary motion — and it is the only route that reliably clears the
§2 paradox pre-certification — then **"≥1 paying" may be unachievable inside the window**, not
because the product failed but because we deliberately removed the payment step to avoid a
procurement we would lose.

A gate that a correct strategy cannot pass will produce the wrong decision.

### Recommended restatement

> **Gate A:** ≥5 verified entities have published ≥1 real tender each; ≥3 have published a
> second tender **unprompted**; and ≥2 have given a **written statement of willingness to pay**
> at a stated price.

Second-tender-unprompted is the real signal. A department publishing once is a favour to us; a
department publishing twice without being asked is a product. Written willingness-to-pay
substitutes for revenue without forcing a premature procurement.

---

## 7. The onboarding hook we can already build

We have a PDF extraction pipeline in BidProcessor that reads tender documents and pulls out
sections, tables and technical specifications.

**Point it at the department's own past tenders.** Take their last 10–20 published PDFs and
generate, automatically:

- their clause library (their GCC/SCC boilerplate, as reusable blocks)
- their tender templates by procurement category
- their standard eligibility and qualification criteria
- their recurring item and specification lists

The first-run experience becomes: *"here is your own tender document library, already
structured — write your next tender in fifteen minutes."* That is a strong, differentiated
onboarding moment built almost entirely from infrastructure we already run, and it inverts the
usual SaaS cold start where the customer must populate an empty system.

It also creates switching cost immediately, which is the honest commercial reason to do it.

---

## 8. The faster path nobody has proposed: private buyers first

Government entities take months to onboard. **Private and PSU buyers can onboard in days** —
no GFR, no procurement to run, no certification gate, a commercial contract and a credit card.

Large private buyers run structured tenders too, and some actively want government-grade
transparency (trusts, hospitals, educational institutions, family businesses with governance
requirements, PSU subsidiaries).

**Consider proving Phase 1 on private buyers before or alongside government.** It:

- de-risks the build against real users months earlier,
- generates revenue that is not blocked by §2,
- produces the reference material and case studies that make the government sale credible,
- and tests the compliance engine against a softer audience first.

The trade-off is honest: private buyers do not validate the *government* pain thesis, which is
the thing we most need to test. **Do both — private for revenue and product hardening,
government for thesis validation** — and do not let private-buyer traction be mistaken for
evidence that the government product works.

---

## 9. Open questions

1. **Approve Route A (free Phase 1)?** It is the recommendation and it changes Gate A.
2. **Restate Gate A as in §6?**
3. **Move empanelment (Route D) earlier than Phase 3?**
4. **Test the GeM listing (Route E)?** Low cost, potentially the fastest compliant channel.
5. **Run private buyers in parallel (§8)?**
6. Who owns the named-human onboarding role in the pilot?

---

## Verification status

**Verified in this session:** the BidProcessor extraction capability referenced in §7 exists;
the credential findings in §1 were confirmed in the codebase.

**NOT verified — must be confirmed in Phase 0:** current GFR purchase thresholds and their
amounts (§3 Route B); whether specific state empanelment panels require STQC certification
(Route D); whether GeM's services categories admit a product like ours (Route E); the legal
sufficiency of each verification signal in §4.

**Asserted from domain knowledge:** the fake-tender fraud pattern, the onboarding stage model,
and the claim that officer transfers are frequent enough to require first-class handover. None
reviewed by a serving procurement officer.
