# P1-LICENSED-UNITY-FONT-GUARD — Claude (Opus, High) report

Status: IMPLEMENTED, all execution NOT_RUN (no Bash in this task; Root executes).

## Requirement
- Design ch.14 (`docs/source_v0.8.1/14_chapter.md:24`): IBM Plex Sans KR (OFL) in game builds with a license copy.
- `test_handoff` banned every font suffix, so the approved Unity font failed the required handoff test.

## Change (allowed files only)
- `tools/ai/delivery_files.py`: `APPROVED_FONTS` holds one fixed entry
  (`client/MemoryForgeUnity/Assets/Resources/Fonts/IBMPlexSansKR-Regular.ttf` → font SHA `53750379…71c0`,
  sibling `IBMPlexSansKR-OFL.txt`, license SHA `7e6b2818…07da`, both from `UNITY-FONT-PROVENANCE.json`).
  `font_errors(root, paths)` checks every delivered `.ttf/.otf/.ttc/.woff/.woff2` (suffix case-insensitive, path exact):
  unapproved path → error; approved font must be byte-exact; license must be **in the same delivered set** (not merely on disk)
  and match the recorded hash. Errors are capped at 20 plus a `... N more` line.
  `private_path`, `handoff_files`, `delivery_files` are unchanged; fonts are not hidden from any inventory.
- License normalization: the working-tree OFL file is all-CRLF (93 lines, 93 CR, no bare-LF lines; checked with Grep only),
  so the provenance hash is of the CRLF bytes, while `.gitattributes` `* text=auto eol=lf` will check it out as LF.
  `license_sha256` does CRLF→LF and then LF→CRLF before hashing. This mapping is injective on normalized
  text, so it equals "normalized content == normalized upstream content", checked against the recorded upstream hash.
  No constant needs to be computed by an AI. A stray CR or any text change still fails.
  If Root prefers an LF-canonical SHA constant, replace the third tuple value and drop the re-expansion.
- `tools/ai/test_handoff.py`: `test_no_font_binaries_or_credentials_shipped` → `test_only_approved_licensed_font_and_no_credentials_shipped`.
  It asserts `font_errors(ROOT, payload) == []` and keeps the unconditional `private_path` check for every payload file, including the font.
- `tools/ai/build_manifest.py`: inside `files()`, before any manifest write, the payload is materialized once and the existing
  private refusal runs first. A non-empty `font_errors` then raises `ValueError('Font files cannot be delivered: …')`.
- `tests/test_delivery_files.py`: existing inventory/private/manifest tests untouched. New tests use real repo bytes and temp roots only:
  approved font + CRLF and LF license passes; moved copy/`.OTF`/`.woff2` rejected; font +1 byte rejected;
  license missing / altered / gitignored (undelivered) rejected; error bound; manifest success records exact font SHA;
  manifest refuses unapproved font and keeps previous manifests; private refusal still wins next to the approved font.

## Not done / for Root
- NOT_RUN: `python3 tests/test_delivery_files.py`, `npm run test:handoff`, `npm run test:docs`, export safety, clean-checkout check.
- Real `MANIFEST_AIO.json`/`MANIFEST.sha256` not run or modified.
- The font and license are currently untracked. Confirm after commit that Git keeps the `.ttf` binary (NUL-byte autodetect under `text=auto`).
  An explicit `*.ttf binary` in `.gitattributes` would be safer but is outside this task's files.
  The new success tests read these files from the repo and fail (not skip) if they are absent.
- Unity will create `IBMPlexSansKR-Regular.ttf.meta`. It is not a font suffix and is unaffected.
- Hahmlet/Orbit are not allowed. Each needs its own provenance entry and review.
