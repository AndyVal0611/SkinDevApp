# Original file integrity

Compared before/after SHA-256 values for **251 pre-existing files** in the reference V6 tree and WPF project, including original notebook, all notebook_src files, existing models/metadata, tools, documentation, logs/reports and backups present at snapshot time.

Changed original files: **0**. Details: none.

The exact list and hashes are in original_integrity_before.json / original_integrity_after.json. The original working tree already contained uncommitted modifications; those were preserved rather than reset or mistaken for Astra changes.

Read-only WSL inspection separately hashed **21 original annotation/split/V5 artifact files** before/after its check; unchanged = True. Synthetic model tests also recorded original model hashes unchanged. No operation wrote curated images or WPF code. The complete image collection was not independently hashed byte by byte, so this report does not claim a full image-binary snapshot.

All delivered additions are the Astra notebook, astra_training_src, astra_docs and astra_tests. No real astra_runs research training output was created. Existing V5/Codex V6 checkpoints and logs were never used as destinations for synthetic or Astra outputs.
