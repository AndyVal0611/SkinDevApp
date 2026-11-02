# Astra training readiness

**Engineering artifact implemented and bounded-tested; real supervised training remains blocked.**

- Notebook structure/syntax: passed; 166 cells, unexecuted research outputs.
- CPU/GPU synthetic training: passed for global CNN, ROI CNN and learned fusion.
- Recovery/masking/selection/isolation: see the actual regression results and limits in ASTRA_TECHNICAL_VALIDATION_REPORT.md.
- Real-data prerequisites: blocked. Current frozen catalog 9789 entries: {'Acne': 3000, 'Hyperpigmentation': 3000, 'Normal': 3000, 'Eczema': 789}. Normal remains in train (1,800), validation (300), fusion (450), test (450).
- Image annotations: 9789 rows, all unverified; 0 accepted fully decided rows.
- Region annotations: 0 rows.
- Source-use evidence: 12 registry rows, all permissible_use values blank. No permission was fabricated and no broad source research restarted.
- Global training: code complete; real training pending. Needs 300 train/150 validation fully decided eligible images and 40/20 Present and Absent per condition, source-use clearance and documented reviews.
- ROI training: code complete; real training pending. Needs verified matched regions, all four classes, existing per-split region counts, at least 100 matched train proposals and validation references.
- Fusion: code complete; real training pending, including eligible grouped fusion references (minimum 250 fully decided, 30 positive/negative per condition).
- Calibration/reliability rules: implemented; real fitting pending, using fusion OOF data only.
- Final evaluation: locked/pending. Needs eligible test references and adviser-approved scope addressing historical model exposure. No automatic reshuffle/exclusion or independent cohort fabrication.
- ONNX: five synthetic float32 components passed parity; actual trained component export/parity pending.
- WPF: unchanged; integration, native fallback, C# parity, latency and mandatory-review workflow verification pending.
- Browser Jupyter: WSL kernel exists; JupyterLab/Notebook server is missing. Follow the guide's explicit setup or use the existing WSL notebook editor.

The minimum next human work is genuine source-use evidence plus documented train/validation annotation. Region annotation is not required to begin the global stage. Training code will not bypass these prerequisites. Existing historical-exposure findings in `v6/docs/PRETRAINING_READINESS_REPORT.md` remain relevant: V5 and specialist training exposure, unknown subjects and metadata lineage are unresolved. This task rechecked current manifests; it did not rerun the broad historical exposure investigation or claim the frozen test is independently clean.

The preserved Stage 1/Stage 2 implementation and its eager ROI precomputation are documented in the comparison report. Normal means supported absence of the three targets, not a diagnosis of universally healthy skin. Every assessment remains pending dermatologist review.
