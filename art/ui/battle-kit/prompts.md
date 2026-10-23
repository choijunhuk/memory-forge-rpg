# Generation and production provenance

2026-10-18 · Codex image-owner pass. All deliverables are local to `art/ui/battle-kit`.

The built-in subscription `image_gen` tool was called four times: two new images and two edits. `transparent_background=true` on every call. The tool does not expose its backend model selector. No API key, paid API, external paid service, ImagineArt credits, ComfyUI SSH, Claude call, game-code edit, Unity write or git operation was used.

Tool output size was **1254×1254**, although the prompts requested 1024×1024. The original PNGs are preserved byte-for-byte in `originals/`. Delivery sizes are mechanical downsampling with a symmetric 3.5% canvas inset; alpha is preserved, with no colour-key background removal or retouching. The disabled and pressed images are separately generated reference edits, so tiny engraved-edge differences remain.

The other 101 PNGs are original Codex-authored SVG UI artwork, exported by the already-installed Sharp/librsvg rasterizer. The editable SVGs and complete production script are included. This method was chosen for exact dimensions, predictable transparent holes, declared slice caps and 24px silhouette readability. It is not presented as AI-painted output. No package was installed. No art was fetched from other games.

## 1. End-turn dial — new image

Output: `originals/ui_button_endturn_generated.png`

```text
Use case: stylized-concept. Production game UI sprite, one single circular end-turn clock dial for Memory Forge, midnight clockwork city. Genuine transparent RGBA background. Straight-on perfectly symmetric round brass clock bezel, elegant restrained artisanal brushed antique gold #e2b45f with engraved concentric rings, 12 small geometric tick marks only (no numerals), four delicate clockwork filigree anchors, navy enamel center #141c31. Center is LARGE EMPTY quiet dark blank disc for game-rendered words, no pointer or clock hands in the center. Compact rim ornaments only. Teal #62d6c6 hairline glints, small soft rim glow, cutout safe margin at least 5% on every side, no floating dust. 1024x1024 square canvas, object centered diameter 900px. Premium painted 2.5D Japanese mobile RPG inventory interface quality, crisp silhouette, subtle metal depth, low visual noise. No text, letters, numbers, symbols resembling writing, logos, watermark, characters, backdrop, ground, checkered background, cast shadow rectangle. One object only.
```

## 2. Action-point gem — new image

Output: `originals/ui_gem_ap_generated.png`

```text
Use case: stylized-concept. Production game UI sprite, one single luminous MEMORY GEM for action points in Memory Forge, abandoned clockwork city at midnight. Genuine transparent RGBA background. Straight-on symmetric softly faceted hexagonal crystal set in a thin elegant antique brass #e2b45f clockwork cradle. Crystal cool teal #62d6c6, central face dark deep teal nearly navy with spacious blank region for a white game-rendered number; bright cyan facets at sides and top. Premium 2.5D painted Japanese mobile RPG UI quality. Very legible silhouette, polished beveled edges and sparse crisp glints, no large blurry glow or floating particles, no pedestal. 1024x1024 square canvas, jewel centered occupying 86% of canvas, 7% transparent safety margin all sides. No text, letters, numbers, logos, watermark, scenery, checkerboard, faces. One object only.
```

## 3. Pressed dial — reference edit

Edit target: image 1, `originals/ui_button_endturn_generated.png`.
Output: `originals/ui_button_endturn_pressed_generated.png`

```text
Precise game UI state variant. Use this image as the edit target. Keep its exact round silhouette, four cardinal ornaments, framing, navy blank center, tick shapes, size, camera and transparent background unchanged. Create its PRESSED button state: brass rim visibly darker brown-gold, center slightly darker ink navy, cool teal inlay remains, reduce all specular highlights so it feels mechanically pressed. No added writing, hands, numbers, new symbols, sparkles or outer shadows. One transparent sprite only, preserve every original ornament position.
```

## 4. Disabled dial — reference edit

Edit target: image 1, `originals/ui_button_endturn_generated.png`.
Output: `originals/ui_button_endturn_disabled_generated.png`

```text
Precise game UI state variant. Use this image as edit target. Preserve its exact round silhouette, four cardinal ornaments, framing, blank navy center, tick shapes and transparency. Create its DISABLED unpowered state: all brass becomes desaturated dark gunmetal, all cyan inlays dark slate grey, no glow. Keep a legible rim and spacious blank center. Do not add any lock, X, text, numbers, or symbols. Do not move or redesign any shape. One transparent sprite only.
```

## References read

- `art/requests/battle-ui-kit-20261018.md`
- `reports/battle_ui_research_20261018/README.md` and `mockup_battle.png`
- `art/requests/ui-skin-kit-20261018.md`
- `art/requests/expedition-restyle-needs-20261018.md`
- `art/candidates/20261018/codex/design-spec.md` and its existing four `ico_attribute_*` SVG colours
- Current role and ownership instructions, including `docs/AI_WORKFLOW.md` §13, §17–18

These were provided project references. This task did not perform new web research, copy competitor artwork, or validate the research report's external claims.
