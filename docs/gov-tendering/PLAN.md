# Buyer-Side Tendering — Letting Government Departments Publish Tenders

**Status:** PLAN ONLY — nothing built. Written 2026-07-20.
**Scope:** Add a tender-authoring and publishing capability for government buyers, in the app and on the public tendersagent.com portal.

---

## 0. Executive summary — read this before anything else

The request was "a direct competitor for GeM and eprocure." The research says that framing needs splitting into three very different products with wildly different costs:

| Ambition | Legal position | Cost | Verdict |
|---|---|---|---|
| **Replace GeM** for central-govt common-use goods | Blocked. GFR Rule 149 makes GeM the mandated channel where the item is on the GeM catalogue. | — | **Don't.** Not a competitive problem, a legal one. |
| **e-Publishing** — host and distribute tender *notices*; bids received elsewhere | Open today. No certification required because you never receive a bid. | Weeks | **Do this first.** |
| **e-Procurement** — receive sealed bids, open and evaluate them | Legal and licensed. GFR Rule 160 lets a department use "a service provider of their own choice." 22 private systems are STQC-certified today. | 12–18 months + STQC + PKI/HSM + CERT-In | **The real prize. Phase 3.** |

**The decisive legal finding:** GFR Rule 160 and the DoE OM of 3 Sep 2012 expressly permit a department to run e-procurement through a private service provider, publishing tender information to CPPP via **XML data transfer**. This is not a grey area — it is an existing, populated market (mjunction, C1 India, nCode/GNFC nProcure, abcprocure, TenderWizard, MSTC and ~16 others hold live STQC certificates).

**The unfair advantage we already have:** every new e-procurement portal dies of the same thing — no bidders. We already have the bidder side: a scraped corpus, a supplier base, and a working matching/alerting rail. A department publishing with us reaches suppliers on day one. That is a genuine solution to the two-sided cold start and it is rare.

**The thing that would have sunk this if unchecked:** hardware DSC. See §4 — signing has a zero-install path (GeM already does it), but **bid decryption does not**, and that constraint shapes the entire Phase 3 architecture.

---

## 1. Legal and regulatory ground truth

Sources verified during research; short quotes only.

### 1.1 What binds whom

- **GFR Rule 149** — GeM covers *"common use Goods and Services."* Mandatory where the item is on the GeM catalogue. Works contracts, non-catalogue services, and bespoke procurement fall outside.
- **GFR Rule 159 / 160** — mandate publication of tender information on CPPP and receipt of bids through e-procurement portals. Rule 160 is the enabling rule for private providers.
- **CPPP guidance (NIC)** — departments may use *"service provider of their own choice"*; those already using other providers *"may continue to do so"*, publishing to CPPP via XML data transfer. XML spec: `eprocure.gov.in/cppp/sites/default/files/eproc/XMLStepbyStepDocument.pdf`
- **DoE OM No. 10/3/2012-PPC (3 Sep 2012)** — requires departments to obtain an STQC compliance certificate for the e-procurement solution they use. Binds the **department**, not the vendor — but functions as a hard commercial gate. Cites **CVC circular 01/01/2012**.
- **GeM Availability Report & Past Transaction Summary (GeMARPTS)** — a department procuring *outside* GeM must generate a report justifying that the item was not available on GeM. This is a compliance artefact we can generate for them. It is also, notably, the document that legitimises our existence.

### 1.2 STQC certification (the Phase 3 gate)

- Governing doc: *Guidelines for Compliance to Quality Requirements of e-Procurement Systems*, STQC, **v1.0 dated 31.08.2011** — still the operative document, written four years before eSign existed.
- Certificate valid **3 years**, surveillance audit at end of year 1 and year 2, **physical (not online)**.
- Requires at each surveillance: application security test report and VA report **each ≤6 months old**, impact analysis, client list, ISO 27001 continuation.
- *"Only major changes which affect core functionality, security or infrastructure... will lead to recertification."* — the real friction for a continuously-deployed SaaS.
- Unrectified deviations → **certificate withdrawn after 3 months**.
- Current list: **EPS Client List v10.7, 10 July 2026** — 22 certified systems + 2 applications.
- Cost/duration: no official schedule found. Third-party estimates (unverified) ₹3–10 lakh, 6–14 weeks. STQC can be contacted directly by the solution provider.

### 1.3 What the quality guidelines actually mandate

These are the functional requirements for Phase 3, quoted from the 2011 guidelines:

- Client-side bid encryption using symmetric or PKI asymmetric keys; SSL in transit.
- PKI for authenticating bids and opening the electronic tender box.
- Key Management System *"must specify the holder of private key and public key."*
- Audit trail facilities.
- No administrator-readable passwords; no system-generated temporary passwords; forgot-password request must be digitally signed.
- **Time stamping built into the application**, or via a licensed CA's timestamping service.
- Bids must be undecryptable before the Tender Opening Event; **M-of-N key splitting** prescribed against collusion.
- All DSCs PKI-based, from a **CCA-licensed** Certifying Authority.
- **"The Digital Signature (i.e. Private Key) cannot be handed over by the owner of that key to any other person."** ← this actively resists custodial key architectures. Design around it, not through it.

Also: **GIGW 3.0** requires security audit clearance from NIC/STQC/STQC-empanelled/CERT-In-empanelled lab **before production hosting**, re-audited annually or on any source change.

### 1.4 The signing question — resolved

| Capability | Zero-install path? | Evidence |
|---|---|---|
| **Signing** (create/publish/award) | **Yes** | GeM accepts eSign for *"Bid Publishing, Order Creation, CRAC, order acceptance, invoicing"* — buyers and sellers, no dongle. GeM is retiring OTP in favour of eSign/DSC. |
| **Bid encryption/decryption** | **No** | eSign private keys are *"destroyed after one time use"* with certs valid *"not more than 30 minutes"*. Structurally impossible for bids opened weeks later. |

**The precedent for decryption:** MeitY's *Encryption-decryption mechanism for open bids in GeM* (NeST-GDL-GEN.02, v1.0, July 2017) keeps the Class 3 encryption certificate but moves the private key **from the officer's USB dongle into a FIPS 140-2 Level 2 HSM**, released by **PIN + OTP**, with the key split into three components where **any two of three holders** can open the bid. That is the architecture to copy for Phase 3.

**Blocking constraint to resolve before Phase 3:** STQC's scheme page states *"In eProcurement application/system Class 3 Digital Certificate (Signature or Encryption or both) should only be used."* eSign issues *e-KYC class* certificates, not Class 3 — so **eSign is excluded from an STQC-certified system**, regardless of its validity elsewhere. This likely stems from the guidelines' 2011 vintage rather than considered policy. **Action: write to STQC scheme officers for clarification before committing to an eSign-based Phase 3 signing design.**

**Possible thread-the-needle option (unverified):** IT Act Second Schedule entry 2 (S.O. 3472(E), 29-9-2020) + CCA e-authentication guidelines **v1.8 of 17.06.2025 §10, "Remote Key-Storage"** — a **long-term validity DSC** with the private key in a FIPS 140-2 **Level 3** HSM, sole control via eKYC PIN + 6-char Authpin, protocol eSign Remote API 1.0. Signature-only. **No confirmation any CA offers this in production.** Verify with CCA before relying on it.

---

## 2. Buyer-side pain points, and what we do about each

Marked **[E]** where research found direct evidence, **[I]** where it is reasoned inference.

| # | Pain | Evidence | What we do |
|---|---|---|---|
| 1 | **DSC + legacy client stack.** CPPP requires Class 3 DSC and historically a Java applet; system-requirement docs still reference JRE 1.6, IE 7, Firefox 3. Officers need **two key pairs** (signing + encryption). | **[E]** eprocure.gov.in DSC page; CPPP prerequisites | Phase 1–2: **eSign, zero install, works in any browser.** Phase 3: HSM-held encryption keys per the GeM/MeitY model. Java is an *implementation* choice, not a legal requirement — nothing in the guidelines mandates an applet. |
| 2 | **Key custody strands live tenders.** Officer transfers, DSC expires, token lost → bids may be undecryptable. | **[I]**, strongly implied by M-of-N splitting existing at all, plus the no-handover rule | **M-of-N HSM custody (any 2 of 3)** with a documented, audited re-issuance path on transfer. Turn the failure mode into a feature. |
| 3 | **Five roles, five credentials.** PO Admin (creates), PO Publisher (publishes), PO Opener, PO Evaluator, Auditor — each needing separate credentials and coordination. | **[E]** GePNIC department user manual | Keep the separation (it is the control that makes the system auditable) but make delegation, deputation and standing committees first-class, with in-app handover instead of credential sharing. |
| 4 | **Corrigendum churn.** Date extensions and amendments are a constant, first-class workflow; bidders may need to resubmit. | **[E]** GePNIC corrigendum module | Corrigendum as a **versioned, append-only amendment** with automatic bidder notification down our existing alert rail, diff view, and automatic minimum-notice enforcement. |
| 5 | **Compliance checking is manual and audit-fatal.** MSE Order 2012 (25% reservation, 4% SC/ST, 3% women, L1+15% matching), PPP-MII Class-I/II local content, Land Border Sharing registration (Rule 144(xi), Order 23-07-2020), Integrity Pact thresholds, startup relaxations. | **[E]** all four instruments confirmed | **Compliance-by-construction.** Rules encoded as blocking validations in the form, with the rule citation recorded in the audit file. The department's file is audit-proof by default. This is the wedge. |
| 6 | **Justifying going off-GeM.** GeMARPTS must be generated to procure outside GeM. | **[E]** | **Auto-generate the GeM Availability Report** from our own scraped GeM catalogue corpus. We are uniquely positioned to do this — nobody else has the corpus. |
| 7 | **Authoring happens offline.** NIT/GCC/SCC drafted in Word, BOQ in a rigid XLS whose format must not be altered. No reuse, no templates, no validation. | **[E]** BOQ format rigidity; **[I]** authoring practice | **Structured authoring + clause library + AI drafting.** We already run OpenAI/Claude/Gemini in BidProcessor to *extract* specs from tenders — invert it to *generate* a compliant NIT/BOQ/spec from a plain-language description. Structured BOQ, never a spreadsheet blob. |
| 8 | **Category/spec mismatch.** If the exact item is not in the GeM catalogue, the buyer must raise a category request (slow) or fall back to a custom bid. | **[I]**, consistent with Rule 149 catalogue scoping | Our closed 40-category taxonomy plus free-form technical specs — no catalogue gatekeeping, while still producing machine-matchable output. |
| 9 | **Audit anxiety dominates.** Departments fear a CAG paragraph more than they want speed. | **[I]** but safe | **Immutable, hash-chained published versions**, server-authoritative IST timestamps, and a one-click downloadable audit file for inspection. Sell safety, not efficiency. |

---

## 3. Phasing

### Phase 1 — Tender Notice Publishing (e-publishing). No certification needed.

A department creates a tender notice, uploads NIT/BOQ documents, and publishes. Bids are still received wherever they are received today. We never touch a bid, so **no STQC, no PKI, no HSM**. This is the analogue of CPPP's e-Publishing module.

Delivers immediately: distribution to our existing supplier base, AI-assisted drafting, compliance validation, corrigendum management, a public tender page with SEO, and CPPP XML export so the department stays Rule 159/160 compliant.

This is weeks of work on the existing codebase and it builds the department relationships that make Phase 3 sellable.

### Phase 2 — Managed bid receipt for non-sealed processes.

EOI, RFI, pre-bid queries, clarifications, vendor registration, document issuance tracking. Still no sealed financial bids, so still outside the heaviest requirements. Adds real workflow value and starts capturing the bid side.

### Phase 3 — Full e-procurement with sealed bids.

Two-cover/three-cover sealed bidding, client-side encryption, M-of-N HSM key custody, tender opening events, technical and financial evaluation, comparative statements, award. **Requires STQC certification, CERT-In audit, and a security architecture that is the majority of the work.** 12–18 months. Do not start until Phase 1 has real departments on it.

**Beachhead for Phases 1–2:** entities *not* bound by the central mandate and poorly served by GePNIC — municipal corporations and ULBs, universities, autonomous bodies, state PSUs, cooperative societies, trusts, panchayats. Also private-sector buyers who want government-grade transparency.

---

## 4. The field inventory — "things which shall be present"

Structured to mirror GePNIC's own tender-creation sections (Basic Details → Cover Details → NIT Documents → Work/Item Details → Fee Details → Critical Dates → Bid Opener Selection → Work Item Documents → Other Documents → Publish), extended with the compliance layer that is our differentiator.

### A. Procuring entity and authority
- Ministry / Department; Organisation; Office / Division; Circle / Zone
- Entity type: Central / State / PSU / ULB / Autonomous body / Cooperative / Trust
- Tender Inviting Authority — name, designation, email, phone
- Administrative approval reference; financial sanction reference; budget head
- Financial concurrence reference
- Address, state, district, pincode

### B. Basic details
- Tender reference / internal file number *(natural key — see §5 for the slash problem)*
- Title; brief description; scope of work
- **Tender type:** Open / Limited / Single / Global / EOI / RFP / RFQ / Two-stage
- **Procurement category:** Goods / Works / Services / Consultancy
- **Form of contract:** Item-rate / Percentage / Lump-sum / Turnkey / Rate contract / Piece-work
- **Bidding system:** Single cover / Two cover / Three cover / Multi-cover
- **Evaluation method:** L1 / QCBS (with technical:financial weightage) / LCS / Fixed-budget / Single-source
- Number of covers; cover names; document types required per cover
- Item-wise vs total evaluation
- Reverse auction applicable (y/n); multi-currency (y/n)
- Allow bid re-submission / withdrawal (y/n)
- Pre-bid meeting: y/n, date, venue, online link
- Contract duration; extension options
- GFR rule cited for the procurement mode chosen

### C. Cover details
Per cover: cover number, cover name, and for each required document — name, description, mandatory/optional, permitted file types, max size.
- Cover 1 (Technical): prequalification docs, spec compliance sheet, EMD proof, certificates, undertakings
- Cover 2 (Financial): BOQ / price schedule only

### D. Work / item details
- Line items: item code *(from canonical taxonomy)*, description, unit, quantity, estimated rate, amount
- Technical specifications: group / name / value / tolerance / mandatory-or-preferred
- **Structured Bill of Quantities** — never an opaque spreadsheet
- Delivery or completion period; delivery locations (multi-consignee with per-site quantities)
- Make / model / brand restrictions **plus mandatory written justification** (audit-sensitive)
- Sample requirements; inspection and testing regime
- Warranty / AMC / CMC period

### E. Fees and financials
- Estimated contract value; whether value is disclosed to bidders
- **EMD:** amount or %, mode (BG / DD / online / e-BG), validity, exemptions (MSE, Startup, NSIC)
- **Tender document fee:** amount, mode, exemptions
- **Performance security:** %, form, validity
- Security deposit / retention money
- Price bid format: item-rate / percentage / lump-sum
- Taxes: GST inclusive or exclusive, HSN/SAC codes
- Price variation / escalation clause
- Advance payment terms; payment milestones
- Bid validity period (days)
- Liquidated damages and penalty clauses

### F. Critical dates — all server-authoritative IST, all validated against each other
- Published on
- Document download: start / end
- Clarification window: start / end
- Pre-bid meeting date and time
- Bid submission: start / end
- Technical bid opening date and time
- Financial bid opening date and time
- Bid validity until
- **Validations:** every end after its start; submission end after clarification end; opening after submission end; minimum notice period enforced per procurement mode and value.

### G. Eligibility and qualification
- Annual turnover requirement — amount and number of years
- Similar work experience — count, individual value, look-back period
- Net worth / solvency certificate threshold
- Registrations required: GST, PAN, Udyam/MSME, ISO, statutory licences, contractor class
- Blacklisting / debarment declaration
- JV or consortium permitted; maximum members; lead partner rules
- Subcontracting permitted; cap
- OEM authorisation / MAF required
- Past performance criteria; minimum quality rating

### H. Statutory compliance — the audit-proofing layer
- **MSE Purchase Preference (Order 2012):** 25% reservation; 4% SC/ST; 3% women; price matching within L1+15%
- **Make in India (PPP-MII):** Class-I / Class-II local supplier; minimum local content %; purchase preference; whether restricted to Class-I only
- **Land Border Sharing (GFR Rule 144(xi), Order 23-07-2020):** Competent Authority registration requirement for bidders from land-bordering countries
- **Startup exemption:** turnover and prior-experience relaxation
- **Integrity Pact:** applicability threshold, Independent External Monitor details
- **GeM Availability Report (GeMARPTS)** reference — auto-generated
- Reciprocity and local-content certificate requirements
- Vigilance / CVC clause
- Arbitration and dispute resolution; governing law; jurisdiction

### I. Committee and separation of duties
- Tender creator (PO Admin); Publisher (PO Publisher)
- **Bid openers — minimum 2, configured as M-of-N key holders**
- Technical evaluation committee members
- Financial evaluation committee members
- Auditor (read-only, full visibility)
- Independent External Monitor, where Integrity Pact applies

### J. Documents
- NIT / tender notice
- Detailed tender document — ITB, GCC, SCC
- BOQ / price schedule template
- Drawings, schedules, annexures
- Corrigenda (versioned)
- Pre-bid clarification responses
- Clause library references used

### K. Publication and distribution
- Publish to tendersagent public portal
- **CPPP XML export** (Rule 159/160 compliance)
- Newspaper advertisement text generator (word-count constrained)
- Department website embed / widget / RSS
- Language: English / Hindi / regional
- Notify matched suppliers via the existing matching rail

### L. Post-publication lifecycle
- **Corrigendum:** type (date extension / amendment / cancellation / retender), reason, revised fields, minimum re-notice enforcement
- Clarification Q&A — public, anonymised
- Bid opening event with attendance record
- Technical evaluation record and scoring
- Financial opening and comparative statement (CST)
- Award of contract / LOA
- **Award publication** (Rule 159 requires contract award information to be published)
- Cancellation with recorded reason

### M. Audit trail — non-negotiable
- Every field change: who, when, old value, new value
- **Immutable published versions, hash-chained**
- Server-authoritative NTP-synced IST time; RFC 3161 timestamping in Phase 3
- Downloadable audit file for CAG / vigilance inspection
- **No hard delete — supersede only**

---

## 5. Architecture — mapping onto the existing codebase

Findings from a full read of the repo. File references are real.

### 5.1 What we can reuse as-is

- **`source.platform` / `source.platformTenderId`** (`BiddingBuddyServices/src/BiddingBuddy.Core/Models/Tender.cs:83`) is a free-form unique key with no enum constraint. A new platform value — `"direct"` — needs **no schema change** and slots into every existing read path.
- **Tender `Id` is a GUID string** (`Tender.cs:11`), and the BFF route is `{id:guid}`. Generate a GUID and both hops work unchanged.
- **`TenderDocumentRef.S3Bucket`** already exists (`Tender.cs:218`) and the presign endpoint prefers it over the default bucket — the model already anticipates per-document bucket routing.
- **Public portal surface already exists**: `PublicTendersController` (`[AllowAnonymous]`, rate-limited) plus `/explore`, `/explore/category/:slug`, `/explore/state/:slug` SEO hubs. A published department tender appears on tendersagent **with no new public-facing code**, provided the taxonomy resolves (see 5.2).
- **Matching and digest rail** — `MatchingService`, `tender_alert_rules`, `TenderMatchScanWorker`. New tenders land with `alerts_scanned_at = NULL` and get picked up automatically. Department tenders notify suppliers for free.
- **AI enrichment infrastructure** — BidProcessor already calls OpenAI/Claude/Gemini. Invert it for drafting.

### 5.2 Landmines that will bite

1. **The taxonomy rewrite is silent and destructive.** `TenderService.MapToDocument` forces `Category.Primary` and `Location.State` through `TenderTaxonomy.ResolveCategory` / `ResolveState` — *"never persists an off-taxonomy value regardless of the caller."* A department typing a free-text category gets it silently rewritten, and since alert matching is **exact** on category, an unresolved value means the tender **matches nobody, forever**. → **The create form must constrain input to the canonical taxonomy at entry.** Dropdowns, not free text.

2. **Tenders are global — verified, no `org_id` anywhere.** Neither the Mongo model nor the Postgres entity has one; org association lives only in the `OrgTenderSettings` join. "This department owns and may edit this tender" is **net-new ownership state**.

3. **The existing write path has no caller identity.** `POST /api/tenders` sits behind a single shared admin JWT — anyone holding it can write any tender. (Note: root and Services CLAUDE.md both say "basic auth admin/admin123"; the actual mechanism is a JWT obtained from `POST /auth/token`. **The docs are wrong — fix them.**) A department-facing write path must **not** reuse this credential.

4. **Upsert semantics are wrong for published government tenders.** `MongoTenderRepository` merges documents by `DocumentId` and overwrites fields — correct for a re-scrape, **catastrophic for a published notice that must be immutable**. Phase 1 needs append-only versioning with corrigendum semantics, not upsert.

5. **`DateTimeOffset` serialises as a BSON `[ticks, offset]` array.** Range queries and naive sorts on date fields are already known-broken; deadline ordering needs the existing aggregation-pipeline workaround. Any new date field inherits this. Do not add a range query on a new `DateTimeOffset` without checking `SearchByDeadlineAsync`.

6. **Bucket routing is not as free as it looks.** `TenderDocumentRef.S3Bucket` is honoured, but the presign endpoint uses the **`"TenderS3"`-keyed AWS client**. R2 needs a different endpoint and credentials. Storing department uploads in R2 and serving them through the tender presign path **will fail** until the client is selected by bucket. Either add bucket→client routing, or add a presigned **PUT** for the AWS tender bucket. (Today presigned PUT exists only for R2.)

7. **No RBAC enforcement point exists.** There is no authorization attribute, policy or handler anywhere. `OrgContextMiddleware` checks **membership only and never reads the role**. The single real role check is `RequireRoleAsync`, **private to `OrganizationService`**. The GePNIC five-role separation-of-duties model has nothing to hook into — it is a from-scratch build, and it is a security-critical one.

8. **No org type.** `organizations` has no buyer/supplier discriminator. Net-new.

9. **`platformTenderId` containing slashes** is already why the enrichment-status endpoint takes its id in the body rather than the route. **Decide the department tender-id format before it hardens** — prefer a URL-safe scheme.

10. **Notification changes are a paired edit.** Adding a digest channel requires `AlertDtos.cs` + `MatchingService.DispatchDigestAsync` + a `notification_templates` migration row, **together** — the code comments say so explicitly, and skipping one silently drops messages. New template codes need an idempotent raw-SQL migration (not EF migrations).

### 5.3 Net-new build

| Layer | Work |
|---|---|
| **Postgres (BFF)** | `organizations.org_type`; `tender_ownership` (org_id, tender_id, role); `tender_drafts`; `tender_versions` (hash-chained); `tender_committee_members`; `corrigenda`; `audit_events`; new roles; new notification templates. All as idempotent raw-SQL migrations. |
| **Mongo (Services)** | `source.platform = "direct"`; publication state machine; version pointer. New indexes — **and remember the initializer only ever creates, never drops.** |
| **BFF API** | Org-scoped, user-authenticated tender authoring: draft CRUD, validation, compliance engine, publish, corrigendum, award. A **real RBAC enforcement point** (attribute + handler) — this is the security-critical piece. |
| **Services API** | A department write path distinct from the pipeline's shared-credential upsert, with append-only semantics. |
| **UI** | Entire authoring surface — multi-step form, clause library, BOQ builder, committee management, preview, corrigendum diff, audit viewer. **100% net-new: there is no tender create/edit UI anywhere today.** |
| **Compliance engine** | MSE / MII / LBS / Integrity Pact / startup rules as declarative, versioned, citable rules. Must be versioned — the rules change and old tenders must evaluate under the rules in force at publication. |
| **CPPP XML exporter** | Rule 159/160 compliance. |
| **AI drafting** | Invert the BidProcessor enrichment pipeline. |
| **GeMARPTS generator** | Off-GeM justification from our own GeM corpus. |

---

## 6. Risks

1. **Regulatory drift.** Rules change; a compliance engine that silently applies today's rules to a two-year-old tender produces a *wrong* audit answer. Version the rules and pin each tender to the rule set in force at publication.
2. **STQC change-control vs SaaS cadence.** Major changes force recertification; security and VA reports must stay ≤6 months old. Phase 3 imposes a release-process change on the whole company, not just one service.
3. **The eSign/Class-3 contradiction (§1.4) is unresolved** and gates the Phase 3 signing design. Resolve it with STQC in writing before building.
4. **Remote Key Storage is unproven** — no confirmed production CA offering. Do not put it on the critical path without verification.
5. **Trust and liability.** Hosting a government tender means a bug becomes a procurement dispute, and possibly litigation. Phase 1 keeps us out of the bid path deliberately; that is a feature, not timidity.
6. **Incumbents are entrenched.** 22 certified systems, some backed by PSUs (MSTC, PowerGrid). Competing on features alone loses. Compete on the two things they cannot copy quickly: **zero-install UX** and **a bidder base that already exists**.
7. **Prod constraints still apply.** The 2GB box, the ~500MB Atlas ceiling and the index-pruning backlog are unresolved. A buyer-side product with document authoring will add storage pressure. Size this before Phase 1 ships, not after.

---

## 7. Recommended next steps

1. **Decide the beachhead segment** — ULBs / universities / state PSUs / cooperatives. This determines everything downstream.
2. **Write to STQC** (scheme officers listed on the ePS page) on the eSign/Class-3 question, and to CCA on Remote Key Storage production availability. Both are long-lead; start now.
3. **Get the CPPP XML spec** and confirm our model can produce a valid document. If it can't, that changes the Phase 1 data model.
4. **Interview 3–5 real procurement officers.** Everything in §2 marked **[I]** is inference and should be validated by someone who has actually published a tender. The evidence base for buyer-side pain is much thinner than for seller-side pain.
5. **Then** build Phase 1.

---

## Appendix — verification status

Verified with primary sources: GFR Rules 149/159/160 framing, DoE OM 10/3/2012-PPC, STQC guidelines v1.0 (31.08.2011), EPS surveillance guidelines v2.0 (May 2024), EPS Client List v10.7 (10 Jul 2026), CCA e-authentication guidelines v1.8 (17.06.2025), IT Act Second Schedule amendment chain, MeitY NeST-GDL-GEN.02 (Jul 2017), GeM eSign training module (Mar 2022), GeM buyer/seller FAQs, GIGW 3.0, GePNIC department user manual, eprocure.gov.in DSC page.

**Not verified:** CVC circular 01/01/2012 (quoted inside the DoE OM but not read directly — cvc.gov.in unreachable); STQC cost and timeline (no official schedule found); production availability of Remote Key Storage; buyer-side pain points marked **[I]**.

**Corrections owed to the codebase docs:** root `CLAUDE.md` and `BiddingBuddyServices/CLAUDE.md` describe the Services auth as HTTP basic `admin/admin123`; it is actually a JWT obtained by exchanging those credentials at `POST /auth/token`.
