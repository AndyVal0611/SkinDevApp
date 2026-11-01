# Dermatologist-review contract (V6 final assessment, schema 1.1)

> Notebook-side implementation is done (Part VIII §32.1–32.2, tests in §54, schemas exported in §51.5). **Nothing in the WPF application was changed**; §5 lists the application-side work for later.

## 1. Rules
1. **Every AI assessment requires dermatologist review** — whatever its state (including `NO_FINDINGS_SUPPORTED`, `RECAPTURE_REQUIRED`, `OUT_OF_SCOPE_REVIEW_REQUIRED`, `INFERENCE_ERROR`), confidence, stage reached or findings. There is no configuration that waives it (`requires_dermatologist_review` is constant `true` and the contract check fails otherwise).
2. **Status at issue is always `PENDING_DERMATOLOGIST_REVIEW`.** The AI object never carries any other review status.
3. **The AI assessment is immutable.** At issue it is *sealed*: `ai_content_sha256` = SHA-256 of the canonical JSON (sorted keys, compact separators) of every other field. Attribute assignment on a sealed assessment raises; nested edits are detected by `verify_integrity()`; the append-only store refuses a second, different assessment under the same `scan_id`. A correction is a **new** assessment with a new `scan_id`.
4. **The dermatologist's findings are a separate record** (`DermatologistReview`) linked to the reviewed AI version by `scan_id` **and** `ai_content_sha256`. A review whose hash does not match is refused.
5. **AI verification ≠ clinical validation.** Stage 2 and the adaptive cycle are *AI verification*: `ai_verification.clinical_validation` is always `false` and carries a statement saying so. `clinical_status` is always `NOT_CLINICALLY_VALIDATED`. State names such as `FINDINGS_SUPPORTED` describe the strength of the AI evidence, not a diagnosis (`state_meaning`).

## 2. Records
**`FinalAssessment` (schema `lumyvue-final-assessment/1.1`)** — the 1.0 fields plus `review_status`, `requires_dermatologist_review`, `clinical_status`, `ai_verification`, `state_meaning`, `ai_content_sha256`. Raw CNN scores and every YOLO box (or `null` for an unavailable detector) remain in the object.

**`DermatologistReview` (schema `lumyvue-dermatologist-review/1.0`)**

| Field | Values / meaning |
|---|---|
| `review_id`, `scan_id`, `ai_content_sha256` | identity and the exact AI version reviewed |
| `review_status` | `DERMATOLOGIST_REVIEW_IN_PROGRESS` · `DERMATOLOGIST_REVIEW_COMPLETED` · `DERMATOLOGIST_REVIEW_UNABLE_TO_ASSESS` |
| `reviewer_id`, `reviewer_role` | pseudonymous code; role must be `dermatologist` |
| `reviewed_at_utc`, `image_adequate` | timestamp; `yes` / `no` / `unknown` |
| `blinded_to_ai`, `ai_shown_before_review` | **must be recorded (true/false) for a completed review**, so agreement can be reported for blinded reviews only |
| `findings` | `{Acne, Hyperpigmentation, Eczema}` → `present` / `absent` / `unknown` — **independent clinical findings**; blank = `unknown`, never `absent` |
| `notes` | free text |

`compose_case_record(ai, review)` builds a display/exchange view (deep copies) with `review_status` taken from the review (or `PENDING_DERMATOLOGIST_REVIEW` when none); it never modifies either record.

## 3. Metrics against independently recorded clinical ground truth
`agreement_core / agreement_table / cases_to_codes` compare the AI's per-condition decision (`present` / `absent` / `uncertain` / no result) with the dermatologist's finding. Counted separately and **never forced into agreement or disagreement**: AI *uncertain*, AI *no result* (recapture, out-of-scope, error), dermatologist *unknown*. Reported per condition: pairs, both-decided, agree, disagree, AI-present/derm-absent, AI-absent/derm-present, agreement, Cohen's κ, PPV, sensitivity, specificity; plus the all-conditions exact agreement. Tables can be restricted to **blinded** reviews. On the frozen test split, the dermatologist-verified annotation file plays the role of the independent reference (Section 39.4); the same functions apply to live reviews (`dermatologist_reviews.csv` + the sealed-assessment store).

**Not done automatically:** reviews are never used to retrain, recalibrate or adjust thresholds. To use them as training data they must enter the annotation file under the annotation protocol (blind, dermatologist, verified), and never the test split.

## 4. What changed in the notebook (and what did not)
| Area | Change |
|---|---|
| Data contract | `FinalAssessment` 1.1 (new fields above), sealing, `finalize_assessment`, `DermatologistReview`, `compose_case_record`, append-only sealed store, review CSV loader/validator, `PENDING_DERMATOLOGIST_REVIEW` constant and review-status vocabulary |
| Wording | "Stage 2 verification" → "Stage 2 AI verification (not clinical validation)"; "validated rule" → "band selected on held-out data"; diagrams show the mandatory dermatologist-review step; Chapter IV limitations state that "supported/accepted/verified" are AI-evidence statements |
| Batch results | every row has `review_status`, `requires_dermatologist_review` and per-condition decisions (`dec_*`) |
| Evaluation | new §39.4 agreement/disagreement table for system G vs the dermatologist reference |
| Export | new `final_assessment.schema.json`, `dermatologist_review.schema.json`, `review_contract.json`; `model_metadata.json` carries `review_contract`; the notebook validates its own output against the schemas |
| Tests | +10 tests (every state pending, no waiver, immutability, separate record, hash/role/blinding rules, agreement arithmetic, blinded filtering, Stage-2-not-clinical, schema checks, append-only store) |
| **Unchanged** | the architecture, training, fusion, calibration, uncertainty, abstention, evaluation systems A–G, ablations, ONNX export of the models, all gates |

## 5. Application-side work for later (WPF) — not done here
Observed in `DermatologistValidation` / `StudyRepository`: the app already keeps the dermatologist record in a **separate table** with `ValidationStatus` (Pending | Completed), free-text `DermatologistAssessment`, an `AgreementWithAI` category, reviewer, credential and date — a good base. Gaps against this contract:

1. **Structured per-condition findings** (`present/absent/unknown` for Acne, Hyperpigmentation, Eczema) instead of free text + category, so agreement/κ/PPV can be computed.
2. **Link by hash:** store the sealed AI object (JSON) with `ai_content_sha256` and reference it from the review; verify the hash on every load; reject edits.
3. **Blinding fields** (`blinded_to_ai`, `ai_shown_before_review`) and `image_adequate`; today the review screen shows the AI result first.
4. **One shared result object** for Results, overlays, patient record, review window, thermal receipt and A4 report, displaying `PENDING_DERMATOLOGIST_REVIEW` (receipt and report must say *preliminary AI result, pending dermatologist review* until a completed review exists; the existing "preliminary / final reviewed" receipts are a good fit) and never presenting `FINDINGS_SUPPORTED`/Stage 2 as clinical confirmation.
5. **Immutability enforcement** in the data layer (no UPDATE of AI result rows; corrections = new scan record), audit-log entries for review creation, and role checks (only a dermatologist can complete a review).
6. A **review-status gate**: reports/receipts marked final only after a `DERMATOLOGIST_REVIEW_COMPLETED` record exists for that exact AI hash.
7. Install the **Eczema detector files** in the `Model/` folder (see the readiness report) and decide the ensemble fusion IoU (the app uses a constant 0.5; the eczema JSON mentions 0.55).
