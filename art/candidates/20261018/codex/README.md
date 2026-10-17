# Memory Forge · Zone 1 visual first pass

**Start with [preview.html](preview.html), then [design-spec.md](design-spec.md) and [layout.json](layout.json).** This is a visual implementation handoff for the Claude development root. It contains no game-source changes or Unity edits.

Made and selected: **3 background paintings,13 transparent enemy sprites (all current zone1 definitions including3 bosses),12 original UI marks in SVG+128px PNG.** Reused6 approved starter images unchanged. Rendered14 static screen compositions plus a2340×1080 aspect-fit proof and an overview. All deliverables are in this folder; explicit project writes stayed here. The image service's managed source outputs were copied here, with originals preserved in its cache.

The first visible gain is the repeated run experience: clocktower city map, painted battle stage, identifiable foes, party portraits and illustrated card-header bands. Dark opaque information panels preserve legibility. A shared workshop painting makes rewards, services and the memory ledger feel part of the same world without requiring separate art for every menu. Existing rules, data, character ages and game semantics are unchanged.

- [manifest.json](manifest.json): dimensions, byte size, real-alpha statistics, SHA256, purposes, selected/rejected status and per-file self-review.
- [provenance.csv](provenance.csv) and [prompts/](prompts/): routes, exact prompts and source/edit relationships. The root project's provenance file was intentionally not edited.
- [qa/self-review.json](qa/self-review.json): §15.5 checklist, candid production limits and unexecuted checks.
- [qa/verification.json](qa/verification.json): asset/name/geometry/text-fit/contrast/hash checks.
- [previews/overview.jpg](previews/overview.jpg): one-page review sheet. Every preview is a static design composition, **not a Unity capture**. Values from source data/screenshots illustrate space; service/memory placeholder descriptions mark binding regions rather than fabricate live records.

## Validation

All13 zone1 sprite ids match current data. All PNG/JPEG files decode. Enemy sprites have real0–255 alpha. All6 approved-art copies match source SHA256. The14 tested composition text-fit logs contain no overflows after fixing the seed's four-weakness case. Main battle bands do not overlap for the specified geometry. Body/muted text on panels have14.55:1/6.84:1 contrast; primary brass8.81:1 against the panel on the dark ground. Exact ratios are in the verification JSON.

Selected artwork is **first-pass candidate**, not final production art. Native backgrounds are1672×941 and native enemies1254², below the final2560×1440 background/2048² boss targets. They have not been upscaled. Flat paintings are not3-layer parallax or PSD originals. Some tightly packed alpha edges and fine lines need the later human-retouch pass. Use contained sprites and an8% display inset, never colour-key black.

## Next visual pass

After implementation screenshots: tune actual phone readability first, then a second battle setting/true parallax, authored per-card cutins, enemy hit/idle frames and restrained crack/reservation VFX. Keep the current complete information hierarchy while polishing effects.

Not run: Unity import/Play, gameplay regressions, actual phone48dp/sp/safeArea, runtime115/130% fonts, independent/human review. Accessible overflow behavior is specified, not implemented here. No Claude, other agents, paid APIs, ImagineArt credits, SSH, commit, push or deployment was invoked.

## Rebuild this handoff locally

The helper scripts write only below their own directory. They read existing project data/font/art. Python3+Pillow are already available; no dependency was added.

```sh
python3 build_layout.py
python3 build_icons.py
python3 render_previews.py
python3 finalize_handoff.py
python3 verify_handoff.py
```

The image generator is not rerun by these commands. The built-in tool's backend model was not exposed; no guessed model name appears in provenance.
