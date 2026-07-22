# Security & Fairness — Buyer-Side Tendering

**Annex to** [PLAN.md](PLAN.md) and the board proposal (BST-PROP-01).
**Status:** DESIGN ONLY — nothing built. Written 2026-07-20.

---

## 0. The threat model is inverted

In most products the adversary is outside the system. In public procurement the most
dangerous adversary is **an authorised insider using legitimate credentials** — the
procuring officer who leaks a rival's price, tailors specifications to a favoured vendor,
or extends a deadline until their preferred bidder is ready.

The regulator is explicit that **we are also in the threat model.** The 2011 STQC quality
guidelines state:

> "The Digital Signature (i.e. Private Key) cannot be handed over by the owner of that key
> to any other person. (It has been observed that in some e-tendering portals, the private
> digital keys of the authorized officers are handed over to the staff of the service
> provider… This practice should be stopped forthwith)."

That is a documented, observed abuse — by service-provider staff. So the design goal is not
"keep attackers out." It is:

1. **We must be structurally unable to read a sealed bid** — not trusted not to, *unable* to.
2. **An officer must not be able to manipulate an outcome without leaving a trace.**

---

## 1. The honest promise

**We cannot prevent procurement corruption.** A department determined to rig a tender can
brief a favoured vendor verbally, write specifications offline, or simply not tell anyone a
tender exists. No software prevents that.

What software can do is make every manipulable act **leave a permanent, tamper-evident,
attributable trace**. The promise is:

> Not "no one can cheat here."
> But "cheating here is recorded, and the record cannot be altered."

This is the same wedge as the rest of the proposal — we sell audit-defensibility, not purity.
It is also the honest claim, and the one we can actually keep. **Any marketing that implies
we prevent corruption should be rejected**; it is both false and the kind of claim that
invites a vigilance complaint.

---

## 2. Fairness attack surface

Eleven ways a tender is rigged, and what each control actually does. **P** = prevented
structurally, **D** = detected and recorded, **X** = outside our reach.

| # | Attack | Control | |
|---|---|---|---|
| 1 | **Bid peeking** — officer or insider reads a rival's price before the deadline | Client-side encryption; we hold no decryption key; M-of-N split custody | **P** |
| 2 | **Bid tampering** — a bid substituted or altered after submission | Bidder-signed payload + append-only hash chain; any alteration breaks the chain | **P** |
| 3 | **Late-bid acceptance** — accepting a favoured bid after cutoff | Server-authoritative IST clock; RFC 3161 timestamp; hard cutoff enforced server-side | **P** |
| 4 | **Evaluation manipulation** — adjusting technical scores after seeing prices | Financial cover cannot open until technical evaluation is **committed and signed**; sequence enforced by state machine | **P** |
| 5 | **Discriminatory access** — one bidder gets the document or an answer early | All clarifications public and anonymised; simultaneous release; document access logged | **P** |
| 6 | **Selective non-publication** — publishing where rivals will not look | Publication is simultaneous to all subscribers plus CPPP XML by construction | **P** |
| 7 | **Deadline manipulation** — extending until the favoured bidder is ready | Every corrigendum records a reason, resets minimum notice, and notifies everyone who accessed the tender | **D** |
| 8 | **Spec tailoring** — writing requirements only one vendor can meet | Automated flagging: brand names without "or equivalent", oddly precise dimensions, thresholds that historically yield one bidder | **D** |
| 9 | **Corrigendum abuse** — quietly changing a material term late | Material-change detection forces a notice-period reset; versioned diff is public | **D** |
| 10 | **Bidder collusion / cover bidding** — cartel rotates wins, files losing bids to fake competition | Pattern detection across our award corpus: rotation, price clustering, always-together bidder sets | **D** |
| 11 | **Off-platform collusion** — verbal briefing, offline spec-writing, coercion | — | **X** |

Note the distribution. **Cryptography solves only rows 1–3.** The rest is workflow design and
detection. A proposal that answers "how will you keep it fair?" with "we use encryption" has
answered about a quarter of the question.

---

## 3. Structural controls

### 3.1 Sealed bids we cannot open

Bids are encrypted **in the bidder's browser** before transmission. The platform stores
ciphertext. Decryption keys live in an HSM under the bid openers' control, never in our
application database and never in our application memory in usable form.

Copy the design MeitY published for GeM (NeST-GDL-GEN.02): the Class 3 encryption key moves
off the officer's USB token into a **FIPS 140-2 HSM**, released by PIN + OTP, with the private
key **split into three components where any two of three holders can open a bid**. That
threshold is deliberate: it defeats a single corrupt officer, and it survives one officer
being transferred, ill, or unavailable.

**Consequence to accept:** if all key holders lose access, those bids are unrecoverable. That
is correct behaviour, not a defect. The recovery path is re-tender, and the process must say
so in writing before a department onboards.

### 3.2 Separation of duties, enforced

GePNIC already models this and we should not weaken it: **Procurement Officer Admin**
(creates) → **Publisher** (publishes) → **Opener** → **Evaluator** → **Auditor** (read-only,
sees everything).

Two rules beyond simply having the roles:

- **No single human may hold two conflicting roles on the same tender.** Enforced per-tender,
  not per-organisation — the same person may create tender A and open tender B.
- **The auditor role is read-only and cannot be revoked by the department mid-tender.**
  Otherwise the control is theatre.

### 3.3 Immutable record

Every published version is hash-chained: each entry commits to the hash of its predecessor.
Nothing is ever hard-deleted or updated in place; corrections supersede. An auditor can verify
the chain independently, and we should publish the verification method so they do not have to
trust our word.

**This directly contradicts the existing tender write path**, which upserts and merges — correct
for re-scraping a public portal, catastrophic for a legal record. See PLAN.md §5.2.

### 3.4 Time

The server clock is authoritative and NTP-synced to IST. Client-supplied timestamps are never
trusted for anything that matters. Phase 3 adds RFC 3161 timestamping from a licensed CA so
that submission time is provable **against us** — a bidder must be able to prove they submitted
on time even if we claim otherwise.

---

## 4. Detection — where our corpus is the differentiator

The controls above are table stakes; twenty-two certified incumbents have them. Detection is
where we have something they do not: **a national corpus of scraped tenders and awards.**

Signals we could compute that GeM and CPPP do not surface:

- **Single-bidder rate by department and by officer.** A department where most tenders attract
  exactly one bidder is the single strongest published indicator of restrictive specification.
- **Specification restrictiveness score.** Brand names without "or equivalent"; dimensions
  specified to implausible precision; eligibility thresholds that historically produce one
  qualifier.
- **Bid rotation.** The same small set of suppliers winning in turn across a buyer.
- **Cover-bidding signature.** Losing bids clustered just above a winner, repeatedly, from the
  same firms.
- **Corrigendum patterns.** Deadline extensions correlated with a particular winner.

**Two hard constraints on this feature, and they are not optional:**

1. **Detection output is advisory, never an accusation.** A flag says "this pattern warrants
   review," never "this is corrupt." We are not a vigilance authority and defamation exposure
   is real.
2. **Do not surface a flag against a named officer to anyone but that department and its own
   auditor.** Publishing a corruption-shaped score about an identifiable public servant is a
   different and much riskier product than we are proposing to build.

---

## 5. The conflict of interest — decide this before building

**This is the most serious issue in this document and it is not a technical one.**

Our existing business sells bidders market intelligence — including price-to-win analysis
derived from award data. If we also host the auction, then on a hosted tender we are
simultaneously:

- advising Bidder A on what to bid,
- operating the sealed process they bid into, and
- possibly advising Bidder B as well.

Even with scrupulous conduct, the *appearance* is disqualifying. A losing bidder writes to the
CVC: *"the platform that ran this auction sells price intelligence to my competitor."* That is
a plausible debarment event and a headline, and it would damage the bidder-side business we
already have.

It gets sharper. Our current platform does **tiered alerting** — paying customers get matched
and notified. On a tender *we host*, a paying supplier receiving an alert before a non-paying
one is **us materially advantaging one bidder in a government procurement.** That is not a
theoretical conflict; it falls straight out of merging the two businesses, and today's
architecture would do it by default.

### Recommended rules

| # | Rule | Enforced how |
|---|---|---|
| 1 | On a hosted tender, **notification is simultaneous and tier-blind** — every eligible supplier is alerted at the same instant regardless of plan | Separate dispatch path for hosted tenders; no entitlement check |
| 2 | **Nothing derived from a hosted tender's sealed contents ever enters the intelligence product** — not aggregated, not anonymised, not "just for modelling" | Schema-level separation; hosted sealed data in a store the analytics pipeline cannot read |
| 3 | Post-award data from a hosted tender may enter the corpus **only once published**, on the same terms as any scraped public award | Publication-gated ingestion |
| 4 | **No paid placement, ranking boost, or early access on hosted tenders — ever** | Product rule; must be a stated non-goal |
| 5 | Publish rules 1–4 publicly and let anyone audit them | Trust is the asset |

Rule 4 deserves emphasis: an obvious future monetisation is "pay to have your product
promoted to buyers." **On a hosted government tender that is selling unfairness**, and it must
be ruled out now, in writing, before someone proposes it as a growth lever in two years.

Rules 1 and 2 have real revenue cost. That is why this is a management decision and not an
engineering one. **It should be settled before Phase 1 ships, because retrofitting an
information barrier after launch is far harder than building behind one.**

---

## 6. Our own house — Phase 0 blockers

We cannot credibly sell custody of sealed government bids in our current state. Verified in
the codebase on 2026-07-20:

| Finding | Detail | Severity |
|---|---|---|
| **Live credentials in tracked config** | **22 real credentials** across `BidProcessor.Host`, `BiddingBuddy.Api` and the Downloader — AWS access key + secret (correct 20/40 format), MongoDB Atlas connection strings, an OpenAI key, JWT signing secret, RabbitMQ, SMTP, Twilio, Resend | **Critical** |
| **`.gitignore` force-includes them** | Line 38 is `!appsettings.json`, explicitly negating the ignore rule above it — the leak is configured, not accidental | **Critical** |
| **Rotation still pending** | Known since the earlier secrets incident; documented as scrubbed, **never actually scrubbed** | **Critical** |
| **No authorization layer** | No `AddAuthorization` policy, no attribute, no handler. `OrgContextMiddleware` checks membership and **never reads the role**. The only role check is `RequireRoleAsync`, private to `OrganizationService` | **High** |
| **No audit infrastructure** | 28 migrations, no audit table. Nothing records who changed what | **High** |
| **Untracked logs in the source tree** | 14 log files under `BiddingBuddy.Bff.Api/logs/`, not in `.gitignore` — one `git add -A` from being committed. At least one contains `token=` | **Medium** |
| **Known-vulnerable dependency** | Npgsql high-severity CVE outstanding | **Medium** |
| **Single 2 GB instance, no DR** | Prior host-OOM outage. A government customer will ask for RPO/RTO and we have no answer | **Medium** |
| **Data residency unverified** | S3 is `ap-south-1` (Mumbai), good. **MongoDB Atlas and Cloudflare R2 regions are unconfirmed.** Government data is often required to remain in India | **Open question** |

The first three are the ones that matter for this proposal. **A CERT-In or STQC auditor finds
committed credentials in the first hour**, and the finding is not "you had a vulnerability" —
it is "you do not operate a secrets discipline." That is a competence judgement, and it is
much harder to recover from than a patch.

**Recommendation: rotate and remove before Phase 1 work begins, not before Phase 3.** This is
already overdue independently of this product; buyer-side tendering only raises the cost of
leaving it.

---

## 7. What we must not build

Stating non-goals now, because each is a plausible future proposal that would compromise
fairness:

- **No paid visibility, ranking, or promotion on hosted tenders.**
- **No bidder-side fees on hosted tenders.** (Already recommended in the proposal; it is a
  fairness control as well as a strategic one — a fee is an access barrier.)
- **No "premium early access" to hosted tender notices.**
- **No platform-side access to sealed bid contents, for any reason** — including support,
  debugging, and analytics. If a support workflow needs it, the workflow is wrong.
- **No corruption scores published about named officials.**

---

## 8. Open questions for management

1. **The information barrier (§5) — approve, modify, or reject?** This has revenue implications
   and blocks Phase 1 architecture.
2. **Secrets remediation (§6) — schedule now or accept the audit risk?** Recommendation: now.
3. **Data residency — do we commit to India-only storage** for hosted tender data? Likely
   required by state customers; needs verification of Atlas and R2 regions.
4. Do we want the detection product (§4) at all? It is our strongest differentiator and our
   largest reputational exposure.

---

## Verification status

**Verified in the codebase 2026-07-20:** credential counts and field shapes; the `.gitignore`
negation; absence of an authorization layer; absence of an audit table across 28 migrations;
log files present and unignored.

**Verified against primary regulatory sources** (see PLAN.md appendix): the STQC private-key
handover prohibition; M-of-N key splitting; client-side encryption and time-stamping
requirements; the GeM/MeitY HSM design.

**Asserted from domain knowledge, not verified here:** the taxonomy of eleven fairness attacks
in §2, and the detection signals in §4. These are reasoned, not cited, and no procurement
officer or vigilance official has reviewed them. Treat as a design starting point.
