# Current build

Run `python3 tools/build_integrated_design.py --pdf` from any working directory. It resolves the project root from its own path; `/mnt/data/...` is never assumed. Install `tools/audit/requirements-docs.txt` and a local Korean font. Output is docs/design_v0.8.1.md, its HTML and the completePDF.

`python3 tools/check_integrated_release.py` verifies actualch0..ch23 and fullcardsheets, not bare numbered titles. `python3 tools/audit/test_document_contract.py` must reject the real brokenv0.8 and deletion fixtures. `tools/build_docs.mjs` and `gen_docs.mjs` are legacyv0.7 tools, not currentPDF entrypoints.
