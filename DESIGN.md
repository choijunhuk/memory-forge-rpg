# Design

This is the current UI implementation brief. Game rules and approved character content remain governed by `docs/design_v0.8.1.md`, `docs/RULES_NOW_PLAY.md` and the existing creative-original contracts.

## Source of truth
- Status: Active. Last refreshed: 2026-10-21.
- Primary product surfaces: Unity landscape home, companion catalogue/details, expedition preparation.
- User direction: replace the website-like deckbuilder shell with a decorated, orderly subculture mobile-game hub, inspired by Kazena's navigation and character presence.
- Evidence reviewed: `docs/design/14_ui_ux.md`, `docs/RULES_NOW_PLAY.md`, `MemoryForgeLobbyClient.cs`, existing workshop/zone/character art, phone screenshots in the followup report.

## Brand
- A lived-in clockwork workshop: midnight ink, brass instruments, cool memory light, a prominent companion.
- Avoid dashboard grids, long stacks of equally important buttons, invented currencies or ownership, and borrowed game artwork.

## Product goals
- Home → companions → character details → representative companion → home works with actual content.
- Home → expedition preparation → existing expedition remains available.
- Keep complete moment scenes and authored character appearance intact.
- Actual recruitment, owned collection and recoverable reveal are now authorized; see `docs/plans/recruitment-20261021.md`. Production authentication/payments and growth transactions remain separate.

## Personas and jobs
- A landscape phone player who wants to meet companions, inspect their abilities and enter a run.
- A returning player should find the main action and companion catalogue in stable positions.

## Information architecture
- Home: workshop and companion, compact identity bar, expedition destination at the right, companion/scene navigation along the bottom.
- Companions: portrait grid, all/owned/active-practice-party filters, character detail view with skills and starting cards.
- Details: actual element, weapon type, passive, ultimate and Core-generated card descriptions; choose an available figure as home representative.
- Preparation: existing length selection and fixed practice party; no pretend editable loadout.

## Design principles
- Character and setting carry atmosphere; controls frame the scene.
- One primary action; quieter navigation; align controls to common edges.
- Complete real routes before adding menu destinations. Missing art and unavailable features have honest states.

## Visual language
- Palette: workshop ink #0d1322, layered slate #141c31, ivory #e9eef8, muted silver #98a5c0, brass #e2b45f, memory light #62d6c6.
- Typography: bundled IBM Plex Sans KR; large names, compact headings, readable body text.
- Layout: fixed 1600×900 stage fitted inside the device safe area, 24/32-unit spacing rhythm.
- Components: existing uGUI helpers, kit frames/glyphs and gradients, no new dependency or generic design-system layer.
- Motion: short transitions only, existing reduced-motion setting respected; scenes keep original framing.

## Components
- Reuse backdrop, moment player, sliced kit, buttons, text and scroll views.
- Change home hierarchy; add catalogue, detail/skills/cards and representative selection.
- Selected, disabled, missing-art and empty-filter states must remain legible.

## Accessibility
- Existing 48dp touch target conversion; labels accompany icons and element colours.
- Maintain keyboard navigation through uGUI controls and visible selection.
- Modal panels block interaction with the home; Escape returns one level.
- Reduced motion uses the existing global preference.

## Responsive behavior
- Landscape Android and PC, including 16:9 and wide phones. Respect Screen.safeArea and fit the entire 1600×900 frame without cropping.
- Collection scrolls when touch targets require more room; character art preserves aspect.

## Interaction states
- Loading/error: actual content failures remain explicit; no sample records.
- Empty: filter explains no matching companions; absent artwork uses a labelled fallback.
- Selection updates details without mutating a run or claiming ownership.
- Scene playback closes overlays and returns to the selected representative.

## Content voice
- Korean player-facing labels: 홈, 동료, 스킬, 기본 카드, 출격.
- Use 전체 동료 and 연습 편성 until account ownership is connected. No fabricated levels, balances or reward badges. Account-owned filters use only the authenticated server snapshot; recruitment waits for committed receipts.

## Implementation constraints
- Unity 6 uGUI and existing assets/Core data only. Original artwork and moment JSON remain unchanged.
- Own files: lobby implementation and its PlayMode/capture tests. Claude owns the independently reviewed recruitment Core/server worktree.
- Verify navigation, data labels, modal blocking, representative choice, reduced motion, safe-area layout, screenshots, a fresh macOS development app and actual loopback recruitment UI. The user excludes phone work for this unit.

## Open questions
- Actual recruitment and account-owned collection are in progress through the authenticated Development server. Editable expedition party and production identity remain separate.
- Phone-player assessment of the final composition remains separate from automated checks.
