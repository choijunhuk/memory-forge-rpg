# Memory Forge · Battle UI kit

2026-10-18 · Codex image owner · stage-first handoff candidate

**105 transparent PNG assets: 58 cover every slot in the battle request; 47 extend the earlier skin/restyle requests.** All art has no baked text or numbers. This folder contains art and offline production/checking tools only. No game code, Unity edit/import, Claude call, commit, push or paid API was performed.

[Open the labelled asset browser](preview.html) · [Contact sheet](qa/contact-sheet.png) · [24/32px intent proof](qa/intents-24-32.png) · [Slice proof](qa/slice-proof.png) · [Alpha proof](qa/alpha-proof.png)

## Direction and priority

Midnight navy (#0d1322 / #141c31), restrained brass (#e2b45f), memory teal (#62d6c6), pale ink (#e9eef8), danger coral (#ff6f6f). Ornaments stay at caps and corners so the stage remains open. The large AP crystal and turn dial carry painted metal depth; small icons use quiet, clean silhouettes. Attribute colours match the earlier icon originals exactly: volt #ffd84d, flame #ff7a3d, frost #7cc8ff, void #b68cff. No new lore, character, mechanic, damage threshold or status policy is established.

First import priority: **ui_intent_*, ui_hp_frame_*, ui_shield_block, ui_portrait_ring*, ui_rail_*.** Then card/cost/AP/turn controls, arrows, transitions and generic hit frames. Map/service embellishments are the optional extras.

## Battle request coverage

| Requested slot | Delivered files | Count |
|---|---|---:|
| Seven attack threat shapes | ui_intent_attack_01 through ui_intent_attack_07 | 7 |
| Other intents | defend, strength, debuff, attack_debuff, charge, summon, heal, curse, reflect, expose, defend_all, unknown | 12 |
| Enemy / party HP frames | ui_hp_frame_enemy, ui_hp_frame_party | 2 |
| Block shield | ui_shield_block | 1 |
| Circular portrait / ready ring | ui_portrait_ring, ui_portrait_ring_ready | 2 |
| Reservation rail | now/next/after × empty/reserved/sealed | 9 |
| Turn transition strips | ui_banner_turn_player, ui_banner_turn_enemy | 2 |
| Hit effects | slash/blunt/pierce × frames 01–06 | 18 |
| Drag arrow | head/body × normal/target | 4 |
| Ultimate cutin backing | ui_cutin_plate | 1 |
| **Current battle request total** | | **58** |

The same intent PNG is reused smaller for next-turn intent. Attack tiers change from small dagger → sword → broad cleaver → crossed blades → triple claw → impact star/blade → jagged overload burst. These are visual ordinals; the game keeps authority over damage, multipliers and tier bucketing. The unknown icon uses a veiled eye rather than a question-mark character.

## Applying the art

- Import only **assets/*.png** as runtime sprites. originals/, sources/ and qa/ are source/review materials. Keep byte copies; the manifest lists SHA-256 per exported PNG.
- Every dimension and border below is in **source pixels**. The table uses **left / top / right / bottom (LTRB)**. Unity Sprite.border uses **left / bottom / right / top (LBRT)**; both forms are in manifest.json. 0/0/0/0 means **Simple**, with no slicing.
- For rail, HP, ribbon and pill sprites, only width stretches: keep source-height proportions and uniformly scale the finished component. Their T/B borders are zero. Caps must remain at least L+R wide before applying uniform scale. Panels and separate card borders support both axes. Use bilinear/clamp, no mipmaps, Full Rect mesh and alpha transparency; do not colour-key dark pixels.
- **Cards have two choices.** ui_card_frame_* is a fixed-aspect 360×520 composite: transparent upper art aperture [28,30,304,186], quiet lower name/effect face. Use Simple at about 184×266 for the fan; do not force it into a 184×192 box. For wide or variable-height cards, use ui_card_border_* as 9-slice plus ui_card_face_plate placed beneath the independently sized art aperture. The composite divider is deliberately not claimed to be stretch-safe.
- Portrait rings have a fully transparent center. At 160px, place the portrait in a circular mask of radius about 60, and draw the actual ultimate gauge on a separate ring around radius 68. Ready art adds four static brass chevrons. The frame has no baked gauge fill.
- HP frames are art only. Preserve the actual shared-party HP semantics; do not infer individual party HP pools from the mockup. Runtime fill and ghost-damage bars are separate. Native frame fill rect is [20,14,216,12] on 256×40; scale its coordinates with the component.
- Rail time is also encoded by 1/2/3 small diamonds; reservation has a check, sealed has a lock. Labels, live capacity and effects remain game-rendered. Text/content safe rect is [48,14,224,68] before horizontal slicing.
- AP gem: keep the bright facets outside the number; use its dark central face. Suggested numeric region [76,69,48,66] at 200px. Cost gem: [28,27,40,42] at 96px. End-turn label: centered at (150,150), within radius 83 on the 300px export. These are art safety suggestions, not text-size acceptance.
- Arrow front points **up (-Y)**. Head pivot (.5,.5), body attachment near (.5,.68). Body tiles vertically and intentionally reaches its top/bottom boundaries. The target state is brass; the normal state is teal. The game computes and places the curve.
- Hit frames share 256×256 registration and center pivot; order is 01→06. Suggested 60ms/frame is an art timing proposal only. Reduced motion omits these frames and forced moving banners/cutins; static intent, lock/check and ready shapes remain available.
- Turn strips leave [440,56,720,88] clear for game-rendered labels. The cutin plate contains no character; layer the existing approved cutin above it. Do not bake character art or new motion into this backing.
- End-turn normal/pressed/disabled images are separate imagegen edits from one reference. Their engravings differ slightly; the PNG state change is suitable for a sprite swap, not an exact geometric morph.

## Export verification

**PASS: 105/105 exported PNGs** match declared dimensions, include alpha and have transparent exterior regions. All exterior edges are zero-alpha except the two deliberately continuous arrow-body tiles. Transparent centers of both rings, all node medals, the panel and the common separate card border were sampled at alpha zero. All 101 editable SVGs contain no text element; all four generated images were visually checked for writing. No OCR guarantee is claimed.

**PASS: 23/23 sliced assets** keep cap/corner alpha exactly unchanged when extended. Straight-alpha compositing rounds RGB by less than one channel unit (maximum 0.957 in premultiplied RGB; tolerance 1). [Raw per-file checks](qa/export-checks.json). [Final visual self-review](qa/verdict-03.json): 94/100, pass; this is a local art proof, not independent human approval.

The [intent proof](qa/intents-24-32.png) uses four rows: 24px dark, 32px dark, 24px light, 32px light. Columns are the 19 intents in the table below, beginning with all seven attack tiers. The [contact sheet](qa/contact-sheet.png) has nine columns in the same alphabetical order as the full asset inventory, read left to right and top to bottom. It contains art only, without caption pixels; preview.html provides the labels.

**NOT_RUN:** game import, real localized text overlays, final 1600×900 and 2340×1080 Unity compositions, phone touch/safe-area checks, live masks/gauge rendering, SpriteAtlas filtering, and actual effect playback. Claude owns those integration checks. No game test, build or device PASS is implied.

## Still missing from the optional earlier requests

| Earlier request | Status / boundary |
|---|---|
| One icon per individual relic | NOT_MADE. No anonymous relic pictures were substituted for the actual relic catalogue. |
| Six party-buff icons | NOT_MADE. Their exact six gameplay identities are not listed in the supplied request; this pass does not invent bindings. |
| Rest / forge / shop / event / archive greeters | NOT_MADE. No new NPC identity, costume, dialogue or portrait was introduced. |
| Effect-specific card pictures | NOT_MADE. Card frames include transparent art apertures; no owner cutin is represented as effect art. |
| Common / curse / weapon card-window paintings | NOT_MADE. ui_intent_curse is a small intent glyph, not curse-card illustration. |
| Bold / Semibold typeface | NOT_ADDED. No font or dependency was downloaded; typeface selection remains separate from image art. |

The core battle slot set is present. The earlier general UI skin slots, ten specifically named status glyphs and the two part-slot types are present; bespoke content illustration remains the list above. Assets are first-pass implementation candidates (production_final=false), with source and review evidence retained.

## Every runtime PNG and its source

All PNG files below are RGBA and have no text/numbers. Every SVG source shares its PNG's native canvas dimensions. The four generated source PNG dimensions are listed separately. Each row lists two physical files where an editable SVG exists.

| Export PNG | Slot | Size | 9-slice L/T/R/B | Source file | Notes |
|---|---|---:|---|---|---|
| [ui_badge_intent_attack.png](assets/ui_badge_intent_attack.png) | 의도 받침 · attack | 128×128 | 0 / 0 / 0 / 0 | [ui_badge_intent_attack.svg](sources/ui_badge_intent_attack.svg) | 숫자 안전 rect [38,80,52,24]. stage-first에서는 받침 없이 ui_intent_* 추천. |
| [ui_badge_intent_defend.png](assets/ui_badge_intent_defend.png) | 의도 받침 · defend | 128×128 | 0 / 0 / 0 / 0 | [ui_badge_intent_defend.svg](sources/ui_badge_intent_defend.svg) | 숫자 안전 rect [38,80,52,24]. stage-first에서는 받침 없이 ui_intent_* 추천. |
| [ui_badge_intent_special.png](assets/ui_badge_intent_special.png) | 의도 받침 · special | 128×128 | 0 / 0 / 0 / 0 | [ui_badge_intent_special.svg](sources/ui_badge_intent_special.svg) | 숫자 안전 rect [38,80,52,24]. stage-first에서는 받침 없이 ui_intent_* 추천. |
| [ui_banner_turn_enemy.png](assets/ui_banner_turn_enemy.png) | 턴 전환 띠 · enemy | 1600×200 | 0 / 0 / 0 / 0 | [ui_banner_turn_enemy.svg](sources/ui_banner_turn_enemy.svg) | 게임 글자 안전 rect [440,56,720,88]. 이동/번쩍임은 아트 수용 범위 밖. |
| [ui_banner_turn_player.png](assets/ui_banner_turn_player.png) | 턴 전환 띠 · player | 1600×200 | 0 / 0 / 0 / 0 | [ui_banner_turn_player.svg](sources/ui_banner_turn_player.svg) | 게임 글자 안전 rect [440,56,720,88]. 이동/번쩍임은 아트 수용 범위 밖. |
| [ui_button_endturn.png](assets/ui_button_endturn.png) | 턴 종료 버튼 · 기본 | 300×300 | 0 / 0 / 0 / 0 | [ui_button_endturn_generated.png](originals/ui_button_endturn_generated.png) | 이미지 안 숫자/바늘 없음; 글자 안전 원 중심(150,150), r83. |
| [ui_button_endturn_disabled.png](assets/ui_button_endturn_disabled.png) | 턴 종료 버튼 · 비활성 | 300×300 | 0 / 0 / 0 / 0 | [ui_button_endturn_disabled_generated.png](originals/ui_button_endturn_disabled_generated.png) | 게임의 활성 조건/비활성 이유가 결정. 그림만으로 판정하지 않음. |
| [ui_button_endturn_pressed.png](assets/ui_button_endturn_pressed.png) | 턴 종료 버튼 · 눌림 | 300×300 | 0 / 0 / 0 / 0 | [ui_button_endturn_pressed_generated.png](originals/ui_button_endturn_pressed_generated.png) | 동일 참조로 생성한 상태 변형. 생성형 미세 윤곽 차이는 존재. |
| [ui_button_pill_brass.png](assets/ui_button_pill_brass.png) | 가로 버튼 · brass/normal | 512×128 | 68 / 0 / 68 / 0 | [ui_button_pill_brass.svg](sources/ui_button_pill_brass.svg) | 높이 고정 비율. 글자 영역 [76,28,360,72]. |
| [ui_button_pill_brass_pressed.png](assets/ui_button_pill_brass_pressed.png) | 가로 버튼 · brass/pressed | 512×128 | 68 / 0 / 68 / 0 | [ui_button_pill_brass_pressed.svg](sources/ui_button_pill_brass_pressed.svg) | 높이 고정 비율. 글자 영역 [76,28,360,72]. |
| [ui_button_pill_ink.png](assets/ui_button_pill_ink.png) | 가로 버튼 · ink/normal | 512×128 | 68 / 0 / 68 / 0 | [ui_button_pill_ink.svg](sources/ui_button_pill_ink.svg) | 높이 고정 비율. 글자 영역 [76,28,360,72]. |
| [ui_button_pill_ink_pressed.png](assets/ui_button_pill_ink_pressed.png) | 가로 버튼 · ink/pressed | 512×128 | 68 / 0 / 68 / 0 | [ui_button_pill_ink_pressed.svg](sources/ui_button_pill_ink_pressed.svg) | 높이 고정 비율. 글자 영역 [76,28,360,72]. |
| [ui_card_border_common.png](assets/ui_card_border_common.png) | 가변 비율 카드 테두리 · common | 360×520 | 36 / 36 / 36 / 36 | [ui_card_border_common.svg](sources/ui_card_border_common.svg) | 중앙 완전 투명; 별도 ui_card_face_plate 아래 영역 배치. 최소 72×72. |
| [ui_card_border_flame.png](assets/ui_card_border_flame.png) | 가변 비율 카드 테두리 · flame | 360×520 | 36 / 36 / 36 / 36 | [ui_card_border_flame.svg](sources/ui_card_border_flame.svg) | 중앙 완전 투명; 별도 ui_card_face_plate 아래 영역 배치. 최소 72×72. |
| [ui_card_border_frost.png](assets/ui_card_border_frost.png) | 가변 비율 카드 테두리 · frost | 360×520 | 36 / 36 / 36 / 36 | [ui_card_border_frost.svg](sources/ui_card_border_frost.svg) | 중앙 완전 투명; 별도 ui_card_face_plate 아래 영역 배치. 최소 72×72. |
| [ui_card_border_void.png](assets/ui_card_border_void.png) | 가변 비율 카드 테두리 · void | 360×520 | 36 / 36 / 36 / 36 | [ui_card_border_void.svg](sources/ui_card_border_void.svg) | 중앙 완전 투명; 별도 ui_card_face_plate 아래 영역 배치. 최소 72×72. |
| [ui_card_border_volt.png](assets/ui_card_border_volt.png) | 가변 비율 카드 테두리 · volt | 360×520 | 36 / 36 / 36 / 36 | [ui_card_border_volt.svg](sources/ui_card_border_volt.svg) | 중앙 완전 투명; 별도 ui_card_face_plate 아래 영역 배치. 최소 72×72. |
| [ui_card_face_plate.png](assets/ui_card_face_plate.png) | 넓거나 긴 카드의 독립 이름/효과 판 | 256×256 | 12 / 12 / 12 / 12 | [ui_card_face_plate.svg](sources/ui_card_face_plate.svg) | 중앙 반투명 표현은 게임 alpha로 조절; 프레임과 분리해서 art aperture 비율 유지. |
| [ui_card_frame_common.png](assets/ui_card_frame_common.png) | 손패·보상 카드 완성 틀 · common | 360×520 | 0 / 0 / 0 / 0 | [ui_card_frame_common.svg](sources/ui_card_frame_common.svg) | 고정 비율 Simple; 그림 alpha 창 [28,30,304,186], 이름 [40,242,280,42], 효과 [40,315,280,160]. 184×266 권장. |
| [ui_card_frame_flame.png](assets/ui_card_frame_flame.png) | 손패·보상 카드 완성 틀 · flame | 360×520 | 0 / 0 / 0 / 0 | [ui_card_frame_flame.svg](sources/ui_card_frame_flame.svg) | 고정 비율 Simple; 그림 alpha 창 [28,30,304,186], 이름 [40,242,280,42], 효과 [40,315,280,160]. 184×266 권장. |
| [ui_card_frame_frost.png](assets/ui_card_frame_frost.png) | 손패·보상 카드 완성 틀 · frost | 360×520 | 0 / 0 / 0 / 0 | [ui_card_frame_frost.svg](sources/ui_card_frame_frost.svg) | 고정 비율 Simple; 그림 alpha 창 [28,30,304,186], 이름 [40,242,280,42], 효과 [40,315,280,160]. 184×266 권장. |
| [ui_card_frame_void.png](assets/ui_card_frame_void.png) | 손패·보상 카드 완성 틀 · void | 360×520 | 0 / 0 / 0 / 0 | [ui_card_frame_void.svg](sources/ui_card_frame_void.svg) | 고정 비율 Simple; 그림 alpha 창 [28,30,304,186], 이름 [40,242,280,42], 효과 [40,315,280,160]. 184×266 권장. |
| [ui_card_frame_volt.png](assets/ui_card_frame_volt.png) | 손패·보상 카드 완성 틀 · volt | 360×520 | 0 / 0 / 0 / 0 | [ui_card_frame_volt.svg](sources/ui_card_frame_volt.svg) | 고정 비율 Simple; 그림 alpha 창 [28,30,304,186], 이름 [40,242,280,42], 효과 [40,315,280,160]. 184×266 권장. |
| [ui_cutin_plate.png](assets/ui_cutin_plate.png) | 필살기 공통 대각선 컷인 판 | 1600×420 | 0 / 0 / 0 / 0 | [ui_cutin_plate.svg](sources/ui_cutin_plate.svg) | 캐릭터는 별도 기존 원본을 얹음. 투명 가장자리, 중앙 navy 반투명; 개인 모션/캐릭터 내용 없음. |
| [ui_drag_arrow_body_normal.png](assets/ui_drag_arrow_body_normal.png) | 카드 끌기 몸통 · normal | 32×64 | 0 / 0 / 0 / 0 | [ui_drag_arrow_body_normal.svg](sources/ui_drag_arrow_body_normal.svg) | 세로 tile. 상하 가장자리의 연속 alpha는 의도적. 굴곡 경로는 게임이 배치. |
| [ui_drag_arrow_body_target.png](assets/ui_drag_arrow_body_target.png) | 카드 끌기 몸통 · target | 32×64 | 0 / 0 / 0 / 0 | [ui_drag_arrow_body_target.svg](sources/ui_drag_arrow_body_target.svg) | 세로 tile. 상하 가장자리의 연속 alpha는 의도적. 굴곡 경로는 게임이 배치. |
| [ui_drag_arrow_head_normal.png](assets/ui_drag_arrow_head_normal.png) | 카드 끌기 화살촉 · normal | 96×96 | 0 / 0 / 0 / 0 | [ui_drag_arrow_head_normal.svg](sources/ui_drag_arrow_head_normal.svg) | 전방 -Y; pivot(.5,.5), 연결 anchor(.5,.68). |
| [ui_drag_arrow_head_target.png](assets/ui_drag_arrow_head_target.png) | 카드 끌기 화살촉 · target | 96×96 | 0 / 0 / 0 / 0 | [ui_drag_arrow_head_target.svg](sources/ui_drag_arrow_head_target.svg) | 전방 -Y; pivot(.5,.5), 연결 anchor(.5,.68). |
| [ui_gem_ap.png](assets/ui_gem_ap.png) | 행동력 보석 | 200×200 | 0 / 0 / 0 / 0 | [ui_gem_ap_generated.png](originals/ui_gem_ap_generated.png) | 1254px 생성 원본을 200px로 축소; 숫자 안전 rect [76,69,48,66]. |
| [ui_gem_cost.png](assets/ui_gem_cost.png) | 카드 비용 보석 | 96×96 | 0 / 0 / 0 / 0 | [ui_gem_cost.svg](sources/ui_gem_cost.svg) | 숫자 안전 rect [28,27,40,42]. |
| [ui_hit_blunt_01.png](assets/ui_hit_blunt_01.png) | 타격 blunt · 프레임 1/6 | 256×256 | 0 / 0 / 0 / 0 | [ui_hit_blunt_01.svg](sources/ui_hit_blunt_01.svg) | 중심 pivot(.5,.5); 순서 01→06, 60ms/장 제안(콘티 제안만). reduced-motion에서는 생략. |
| [ui_hit_blunt_02.png](assets/ui_hit_blunt_02.png) | 타격 blunt · 프레임 2/6 | 256×256 | 0 / 0 / 0 / 0 | [ui_hit_blunt_02.svg](sources/ui_hit_blunt_02.svg) | 중심 pivot(.5,.5); 순서 01→06, 60ms/장 제안(콘티 제안만). reduced-motion에서는 생략. |
| [ui_hit_blunt_03.png](assets/ui_hit_blunt_03.png) | 타격 blunt · 프레임 3/6 | 256×256 | 0 / 0 / 0 / 0 | [ui_hit_blunt_03.svg](sources/ui_hit_blunt_03.svg) | 중심 pivot(.5,.5); 순서 01→06, 60ms/장 제안(콘티 제안만). reduced-motion에서는 생략. |
| [ui_hit_blunt_04.png](assets/ui_hit_blunt_04.png) | 타격 blunt · 프레임 4/6 | 256×256 | 0 / 0 / 0 / 0 | [ui_hit_blunt_04.svg](sources/ui_hit_blunt_04.svg) | 중심 pivot(.5,.5); 순서 01→06, 60ms/장 제안(콘티 제안만). reduced-motion에서는 생략. |
| [ui_hit_blunt_05.png](assets/ui_hit_blunt_05.png) | 타격 blunt · 프레임 5/6 | 256×256 | 0 / 0 / 0 / 0 | [ui_hit_blunt_05.svg](sources/ui_hit_blunt_05.svg) | 중심 pivot(.5,.5); 순서 01→06, 60ms/장 제안(콘티 제안만). reduced-motion에서는 생략. |
| [ui_hit_blunt_06.png](assets/ui_hit_blunt_06.png) | 타격 blunt · 프레임 6/6 | 256×256 | 0 / 0 / 0 / 0 | [ui_hit_blunt_06.svg](sources/ui_hit_blunt_06.svg) | 중심 pivot(.5,.5); 순서 01→06, 60ms/장 제안(콘티 제안만). reduced-motion에서는 생략. |
| [ui_hit_pierce_01.png](assets/ui_hit_pierce_01.png) | 타격 pierce · 프레임 1/6 | 256×256 | 0 / 0 / 0 / 0 | [ui_hit_pierce_01.svg](sources/ui_hit_pierce_01.svg) | 중심 pivot(.5,.5); 순서 01→06, 60ms/장 제안(콘티 제안만). reduced-motion에서는 생략. |
| [ui_hit_pierce_02.png](assets/ui_hit_pierce_02.png) | 타격 pierce · 프레임 2/6 | 256×256 | 0 / 0 / 0 / 0 | [ui_hit_pierce_02.svg](sources/ui_hit_pierce_02.svg) | 중심 pivot(.5,.5); 순서 01→06, 60ms/장 제안(콘티 제안만). reduced-motion에서는 생략. |
| [ui_hit_pierce_03.png](assets/ui_hit_pierce_03.png) | 타격 pierce · 프레임 3/6 | 256×256 | 0 / 0 / 0 / 0 | [ui_hit_pierce_03.svg](sources/ui_hit_pierce_03.svg) | 중심 pivot(.5,.5); 순서 01→06, 60ms/장 제안(콘티 제안만). reduced-motion에서는 생략. |
| [ui_hit_pierce_04.png](assets/ui_hit_pierce_04.png) | 타격 pierce · 프레임 4/6 | 256×256 | 0 / 0 / 0 / 0 | [ui_hit_pierce_04.svg](sources/ui_hit_pierce_04.svg) | 중심 pivot(.5,.5); 순서 01→06, 60ms/장 제안(콘티 제안만). reduced-motion에서는 생략. |
| [ui_hit_pierce_05.png](assets/ui_hit_pierce_05.png) | 타격 pierce · 프레임 5/6 | 256×256 | 0 / 0 / 0 / 0 | [ui_hit_pierce_05.svg](sources/ui_hit_pierce_05.svg) | 중심 pivot(.5,.5); 순서 01→06, 60ms/장 제안(콘티 제안만). reduced-motion에서는 생략. |
| [ui_hit_pierce_06.png](assets/ui_hit_pierce_06.png) | 타격 pierce · 프레임 6/6 | 256×256 | 0 / 0 / 0 / 0 | [ui_hit_pierce_06.svg](sources/ui_hit_pierce_06.svg) | 중심 pivot(.5,.5); 순서 01→06, 60ms/장 제안(콘티 제안만). reduced-motion에서는 생략. |
| [ui_hit_slash_01.png](assets/ui_hit_slash_01.png) | 타격 slash · 프레임 1/6 | 256×256 | 0 / 0 / 0 / 0 | [ui_hit_slash_01.svg](sources/ui_hit_slash_01.svg) | 중심 pivot(.5,.5); 순서 01→06, 60ms/장 제안(콘티 제안만). reduced-motion에서는 생략. |
| [ui_hit_slash_02.png](assets/ui_hit_slash_02.png) | 타격 slash · 프레임 2/6 | 256×256 | 0 / 0 / 0 / 0 | [ui_hit_slash_02.svg](sources/ui_hit_slash_02.svg) | 중심 pivot(.5,.5); 순서 01→06, 60ms/장 제안(콘티 제안만). reduced-motion에서는 생략. |
| [ui_hit_slash_03.png](assets/ui_hit_slash_03.png) | 타격 slash · 프레임 3/6 | 256×256 | 0 / 0 / 0 / 0 | [ui_hit_slash_03.svg](sources/ui_hit_slash_03.svg) | 중심 pivot(.5,.5); 순서 01→06, 60ms/장 제안(콘티 제안만). reduced-motion에서는 생략. |
| [ui_hit_slash_04.png](assets/ui_hit_slash_04.png) | 타격 slash · 프레임 4/6 | 256×256 | 0 / 0 / 0 / 0 | [ui_hit_slash_04.svg](sources/ui_hit_slash_04.svg) | 중심 pivot(.5,.5); 순서 01→06, 60ms/장 제안(콘티 제안만). reduced-motion에서는 생략. |
| [ui_hit_slash_05.png](assets/ui_hit_slash_05.png) | 타격 slash · 프레임 5/6 | 256×256 | 0 / 0 / 0 / 0 | [ui_hit_slash_05.svg](sources/ui_hit_slash_05.svg) | 중심 pivot(.5,.5); 순서 01→06, 60ms/장 제안(콘티 제안만). reduced-motion에서는 생략. |
| [ui_hit_slash_06.png](assets/ui_hit_slash_06.png) | 타격 slash · 프레임 6/6 | 256×256 | 0 / 0 / 0 / 0 | [ui_hit_slash_06.svg](sources/ui_hit_slash_06.svg) | 중심 pivot(.5,.5); 순서 01→06, 60ms/장 제안(콘티 제안만). reduced-motion에서는 생략. |
| [ui_hp_frame_enemy.png](assets/ui_hp_frame_enemy.png) | enemy 체력 막대 가장자리 | 256×40 | 20 / 0 / 20 / 0 | [ui_hp_frame_enemy.svg](sources/ui_hp_frame_enemy.svg) | 가로 slice만; 높이 40→20 균등 축소. fill rect [20,14,216,12]. |
| [ui_hp_frame_party.png](assets/ui_hp_frame_party.png) | party 체력 막대 가장자리 | 256×40 | 20 / 0 / 20 / 0 | [ui_hp_frame_party.svg](sources/ui_hp_frame_party.svg) | 가로 slice만; 높이 40→20 균등 축소. fill rect [20,14,216,12]. |
| [ui_intent_attack_01.png](assets/ui_intent_attack_01.png) | 현재/다음 공격 의도 · 시각 단계 1 | 128×128 | 0 / 0 / 0 / 0 | [ui_intent_attack_01.svg](sources/ui_intent_attack_01.svg) | 피해량→단계 매핑은 게임의 기존 정책을 사용. 새 수치 규칙 없음. |
| [ui_intent_attack_02.png](assets/ui_intent_attack_02.png) | 현재/다음 공격 의도 · 시각 단계 2 | 128×128 | 0 / 0 / 0 / 0 | [ui_intent_attack_02.svg](sources/ui_intent_attack_02.svg) | 피해량→단계 매핑은 게임의 기존 정책을 사용. 새 수치 규칙 없음. |
| [ui_intent_attack_03.png](assets/ui_intent_attack_03.png) | 현재/다음 공격 의도 · 시각 단계 3 | 128×128 | 0 / 0 / 0 / 0 | [ui_intent_attack_03.svg](sources/ui_intent_attack_03.svg) | 피해량→단계 매핑은 게임의 기존 정책을 사용. 새 수치 규칙 없음. |
| [ui_intent_attack_04.png](assets/ui_intent_attack_04.png) | 현재/다음 공격 의도 · 시각 단계 4 | 128×128 | 0 / 0 / 0 / 0 | [ui_intent_attack_04.svg](sources/ui_intent_attack_04.svg) | 피해량→단계 매핑은 게임의 기존 정책을 사용. 새 수치 규칙 없음. |
| [ui_intent_attack_05.png](assets/ui_intent_attack_05.png) | 현재/다음 공격 의도 · 시각 단계 5 | 128×128 | 0 / 0 / 0 / 0 | [ui_intent_attack_05.svg](sources/ui_intent_attack_05.svg) | 피해량→단계 매핑은 게임의 기존 정책을 사용. 새 수치 규칙 없음. |
| [ui_intent_attack_06.png](assets/ui_intent_attack_06.png) | 현재/다음 공격 의도 · 시각 단계 6 | 128×128 | 0 / 0 / 0 / 0 | [ui_intent_attack_06.svg](sources/ui_intent_attack_06.svg) | 피해량→단계 매핑은 게임의 기존 정책을 사용. 새 수치 규칙 없음. |
| [ui_intent_attack_07.png](assets/ui_intent_attack_07.png) | 현재/다음 공격 의도 · 시각 단계 7 | 128×128 | 0 / 0 / 0 / 0 | [ui_intent_attack_07.svg](sources/ui_intent_attack_07.svg) | 피해량→단계 매핑은 게임의 기존 정책을 사용. 새 수치 규칙 없음. |
| [ui_intent_attack_debuff.png](assets/ui_intent_attack_debuff.png) | 적 머리 위 의도 · attack_debuff | 128×128 | 0 / 0 / 0 / 0 | [ui_intent_attack_debuff.svg](sources/ui_intent_attack_debuff.svg) | — |
| [ui_intent_charge.png](assets/ui_intent_charge.png) | 적 머리 위 의도 · charge | 128×128 | 0 / 0 / 0 / 0 | [ui_intent_charge.svg](sources/ui_intent_charge.svg) | — |
| [ui_intent_curse.png](assets/ui_intent_curse.png) | 적 머리 위 의도 · curse | 128×128 | 0 / 0 / 0 / 0 | [ui_intent_curse.svg](sources/ui_intent_curse.svg) | — |
| [ui_intent_debuff.png](assets/ui_intent_debuff.png) | 적 머리 위 의도 · debuff | 128×128 | 0 / 0 / 0 / 0 | [ui_intent_debuff.svg](sources/ui_intent_debuff.svg) | — |
| [ui_intent_defend.png](assets/ui_intent_defend.png) | 적 머리 위 의도 · defend | 128×128 | 0 / 0 / 0 / 0 | [ui_intent_defend.svg](sources/ui_intent_defend.svg) | — |
| [ui_intent_defend_all.png](assets/ui_intent_defend_all.png) | 적 머리 위 의도 · defend_all | 128×128 | 0 / 0 / 0 / 0 | [ui_intent_defend_all.svg](sources/ui_intent_defend_all.svg) | — |
| [ui_intent_expose.png](assets/ui_intent_expose.png) | 적 머리 위 의도 · expose | 128×128 | 0 / 0 / 0 / 0 | [ui_intent_expose.svg](sources/ui_intent_expose.svg) | — |
| [ui_intent_heal.png](assets/ui_intent_heal.png) | 적 머리 위 의도 · heal | 128×128 | 0 / 0 / 0 / 0 | [ui_intent_heal.svg](sources/ui_intent_heal.svg) | — |
| [ui_intent_reflect.png](assets/ui_intent_reflect.png) | 적 머리 위 의도 · reflect | 128×128 | 0 / 0 / 0 / 0 | [ui_intent_reflect.svg](sources/ui_intent_reflect.svg) | — |
| [ui_intent_strength.png](assets/ui_intent_strength.png) | 적 머리 위 의도 · strength | 128×128 | 0 / 0 / 0 / 0 | [ui_intent_strength.svg](sources/ui_intent_strength.svg) | — |
| [ui_intent_summon.png](assets/ui_intent_summon.png) | 적 머리 위 의도 · summon | 128×128 | 0 / 0 / 0 / 0 | [ui_intent_summon.svg](sources/ui_intent_summon.svg) | — |
| [ui_intent_unknown.png](assets/ui_intent_unknown.png) | 적 머리 위 의도 · unknown | 128×128 | 0 / 0 / 0 / 0 | [ui_intent_unknown.svg](sources/ui_intent_unknown.svg) | — |
| [ui_node_medal_current.png](assets/ui_node_medal_current.png) | 지도 노드 테두리 · current | 256×256 | 0 / 0 / 0 / 0 | [ui_node_medal_current.svg](sources/ui_node_medal_current.svg) | 중앙 r88 완전 alpha. 현재=4마름모, 가능=3갈매기, 지나감=체크. |
| [ui_node_medal_reachable.png](assets/ui_node_medal_reachable.png) | 지도 노드 테두리 · reachable | 256×256 | 0 / 0 / 0 / 0 | [ui_node_medal_reachable.svg](sources/ui_node_medal_reachable.svg) | 중앙 r88 완전 alpha. 현재=4마름모, 가능=3갈매기, 지나감=체크. |
| [ui_node_medal_visited.png](assets/ui_node_medal_visited.png) | 지도 노드 테두리 · visited | 256×256 | 0 / 0 / 0 / 0 | [ui_node_medal_visited.svg](sources/ui_node_medal_visited.svg) | 중앙 r88 완전 alpha. 현재=4마름모, 가능=3갈매기, 지나감=체크. |
| [ui_panel_frame.png](assets/ui_panel_frame.png) | 반투명 패널의 별도 장식 경계 | 256×256 | 40 / 40 / 40 / 40 | [ui_panel_frame.svg](sources/ui_panel_frame.svg) | 중앙 완전 투명. 판 배경은 ui_card_face_plate 또는 게임의 #141c31. |
| [ui_part_circuit.png](assets/ui_part_circuit.png) | 부품 슬롯 종류 · circuit | 128×128 | 0 / 0 / 0 / 0 | [ui_part_circuit.svg](sources/ui_part_circuit.svg) | 슬롯 종류 그림이며 개별 부품의 효과/등급을 뜻하지 않음. |
| [ui_part_engraving.png](assets/ui_part_engraving.png) | 부품 슬롯 종류 · engraving | 128×128 | 0 / 0 / 0 / 0 | [ui_part_engraving.svg](sources/ui_part_engraving.svg) | 슬롯 종류 그림이며 개별 부품의 효과/등급을 뜻하지 않음. |
| [ui_pile_discard.png](assets/ui_pile_discard.png) | 카드 더미 · discard | 160×160 | 0 / 0 / 0 / 0 | [ui_pile_discard.svg](sources/ui_pile_discard.svg) | 숫자는 아이콘 바깥 별도 배지. |
| [ui_pile_draw.png](assets/ui_pile_draw.png) | 카드 더미 · draw | 160×160 | 0 / 0 / 0 / 0 | [ui_pile_draw.svg](sources/ui_pile_draw.svg) | 숫자는 아이콘 바깥 별도 배지. |
| [ui_portrait_frame.png](assets/ui_portrait_frame.png) | 저장·일반 파티 세로 초상 · normal | 200×240 | 0 / 0 / 0 / 0 | [ui_portrait_frame.svg](sources/ui_portrait_frame.svg) | 투명 중앙; 초상 crop mask는 게임에서 제공. battle은 원형 링 사용. |
| [ui_portrait_frame_ready.png](assets/ui_portrait_frame_ready.png) | 저장·일반 파티 세로 초상 · ready | 200×240 | 0 / 0 / 0 / 0 | [ui_portrait_frame_ready.svg](sources/ui_portrait_frame_ready.svg) | 투명 중앙; 초상 crop mask는 게임에서 제공. battle은 원형 링 사용. |
| [ui_portrait_ring.png](assets/ui_portrait_ring.png) | 파티 초상 원형 틀 · 기본 | 160×160 | 0 / 0 / 0 / 0 | [ui_portrait_ring.svg](sources/ui_portrait_ring.svg) | 중앙 alpha 구멍 r60. 게이지는 r68의 별도 게임 렌더; 이 PNG에는 채움 없음. |
| [ui_portrait_ring_ready.png](assets/ui_portrait_ring_ready.png) | 파티 초상 원형 틀 · 필살기 준비 | 160×160 | 0 / 0 / 0 / 0 | [ui_portrait_ring_ready.svg](sources/ui_portrait_ring_ready.svg) | 정지 상태에도 4개 추가 금빛 갈매기 표시로 구분. |
| [ui_rail_after_empty.png](assets/ui_rail_after_empty.png) | 예약 레일 · after/empty | 320×96 | 44 / 0 / 44 / 0 | [ui_rail_after_empty.svg](sources/ui_rail_after_empty.svg) | 내용 안전 rect [48,14,224,68]. 가로만 stretch, 원본 높이는 균등 축소. now=1, next=2, after=3 마름모. |
| [ui_rail_after_reserved.png](assets/ui_rail_after_reserved.png) | 예약 레일 · after/reserved | 320×96 | 44 / 0 / 44 / 0 | [ui_rail_after_reserved.svg](sources/ui_rail_after_reserved.svg) | 내용 안전 rect [48,14,224,68]. 가로만 stretch, 원본 높이는 균등 축소. now=1, next=2, after=3 마름모. |
| [ui_rail_after_sealed.png](assets/ui_rail_after_sealed.png) | 예약 레일 · after/sealed | 320×96 | 44 / 0 / 44 / 0 | [ui_rail_after_sealed.svg](sources/ui_rail_after_sealed.svg) | 내용 안전 rect [48,14,224,68]. 가로만 stretch, 원본 높이는 균등 축소. now=1, next=2, after=3 마름모. |
| [ui_rail_next_empty.png](assets/ui_rail_next_empty.png) | 예약 레일 · next/empty | 320×96 | 44 / 0 / 44 / 0 | [ui_rail_next_empty.svg](sources/ui_rail_next_empty.svg) | 내용 안전 rect [48,14,224,68]. 가로만 stretch, 원본 높이는 균등 축소. now=1, next=2, after=3 마름모. |
| [ui_rail_next_reserved.png](assets/ui_rail_next_reserved.png) | 예약 레일 · next/reserved | 320×96 | 44 / 0 / 44 / 0 | [ui_rail_next_reserved.svg](sources/ui_rail_next_reserved.svg) | 내용 안전 rect [48,14,224,68]. 가로만 stretch, 원본 높이는 균등 축소. now=1, next=2, after=3 마름모. |
| [ui_rail_next_sealed.png](assets/ui_rail_next_sealed.png) | 예약 레일 · next/sealed | 320×96 | 44 / 0 / 44 / 0 | [ui_rail_next_sealed.svg](sources/ui_rail_next_sealed.svg) | 내용 안전 rect [48,14,224,68]. 가로만 stretch, 원본 높이는 균등 축소. now=1, next=2, after=3 마름모. |
| [ui_rail_now_empty.png](assets/ui_rail_now_empty.png) | 예약 레일 · now/empty | 320×96 | 44 / 0 / 44 / 0 | [ui_rail_now_empty.svg](sources/ui_rail_now_empty.svg) | 내용 안전 rect [48,14,224,68]. 가로만 stretch, 원본 높이는 균등 축소. now=1, next=2, after=3 마름모. |
| [ui_rail_now_reserved.png](assets/ui_rail_now_reserved.png) | 예약 레일 · now/reserved | 320×96 | 44 / 0 / 44 / 0 | [ui_rail_now_reserved.svg](sources/ui_rail_now_reserved.svg) | 내용 안전 rect [48,14,224,68]. 가로만 stretch, 원본 높이는 균등 축소. now=1, next=2, after=3 마름모. |
| [ui_rail_now_sealed.png](assets/ui_rail_now_sealed.png) | 예약 레일 · now/sealed | 320×96 | 44 / 0 / 44 / 0 | [ui_rail_now_sealed.svg](sources/ui_rail_now_sealed.svg) | 내용 안전 rect [48,14,224,68]. 가로만 stretch, 원본 높이는 균등 축소. now=1, next=2, after=3 마름모. |
| [ui_ribbon_title.png](assets/ui_ribbon_title.png) | 지도/적/장소 이름 리본 | 512×96 | 48 / 0 / 48 / 0 | [ui_ribbon_title.svg](sources/ui_ribbon_title.svg) | 글자 안전 rect [56,22,400,52]. |
| [ui_shield_block.png](assets/ui_shield_block.png) | HP 왼쪽 방어 수치 받침 | 64×64 | 0 / 0 / 0 / 0 | [ui_shield_block.svg](sources/ui_shield_block.svg) | 숫자 안전 영역 x24..40, y18..34; 초상/파티별 HP 의미를 새로 만들지 않음. |
| [ui_status_block.png](assets/ui_status_block.png) | 상태 칩 · block | 64×64 | 0 / 0 / 0 / 0 | [ui_status_block.svg](sources/ui_status_block.svg) | — |
| [ui_status_burn.png](assets/ui_status_burn.png) | 상태 칩 · burn | 64×64 | 0 / 0 / 0 / 0 | [ui_status_burn.svg](sources/ui_status_burn.svg) | — |
| [ui_status_counter.png](assets/ui_status_counter.png) | 상태 칩 · counter | 64×64 | 0 / 0 / 0 / 0 | [ui_status_counter.svg](sources/ui_status_counter.svg) | — |
| [ui_status_debt.png](assets/ui_status_debt.png) | 상태 칩 · debt | 64×64 | 0 / 0 / 0 / 0 | [ui_status_debt.svg](sources/ui_status_debt.svg) | — |
| [ui_status_echo.png](assets/ui_status_echo.png) | 상태 칩 · echo | 64×64 | 0 / 0 / 0 / 0 | [ui_status_echo.svg](sources/ui_status_echo.svg) | — |
| [ui_status_mark.png](assets/ui_status_mark.png) | 상태 칩 · mark | 64×64 | 0 / 0 / 0 / 0 | [ui_status_mark.svg](sources/ui_status_mark.svg) | — |
| [ui_status_reflect.png](assets/ui_status_reflect.png) | 상태 칩 · reflect | 64×64 | 0 / 0 / 0 / 0 | [ui_status_reflect.svg](sources/ui_status_reflect.svg) | — |
| [ui_status_strength.png](assets/ui_status_strength.png) | 상태 칩 · strength | 64×64 | 0 / 0 / 0 / 0 | [ui_status_strength.svg](sources/ui_status_strength.svg) | — |
| [ui_status_vulnerable.png](assets/ui_status_vulnerable.png) | 상태 칩 · vulnerable | 64×64 | 0 / 0 / 0 / 0 | [ui_status_vulnerable.svg](sources/ui_status_vulnerable.svg) | — |
| [ui_status_weak.png](assets/ui_status_weak.png) | 상태 칩 · weak | 64×64 | 0 / 0 / 0 / 0 | [ui_status_weak.svg](sources/ui_status_weak.svg) | — |
| [ui_timeline_slot_empty.png](assets/ui_timeline_slot_empty.png) | 구형 작은 시간표 칸 · empty | 128×128 | 0 / 0 / 0 / 0 | [ui_timeline_slot_empty.svg](sources/ui_timeline_slot_empty.svg) | stage-first 가로 레일은 ui_rail_* 사용. |
| [ui_timeline_slot_reserved.png](assets/ui_timeline_slot_reserved.png) | 구형 작은 시간표 칸 · reserved | 128×128 | 0 / 0 / 0 / 0 | [ui_timeline_slot_reserved.svg](sources/ui_timeline_slot_reserved.svg) | stage-first 가로 레일은 ui_rail_* 사용. |
| [ui_timeline_slot_sealed.png](assets/ui_timeline_slot_sealed.png) | 구형 작은 시간표 칸 · sealed | 128×128 | 0 / 0 / 0 / 0 | [ui_timeline_slot_sealed.svg](sources/ui_timeline_slot_sealed.svg) | stage-first 가로 레일은 ui_rail_* 사용. |

## Every additional authored delivery file

| File | Size / purpose |
|---|---|
| [originals/ui_button_endturn_disabled_generated.png](originals/ui_button_endturn_disabled_generated.png) | 1254×1254, original imagegen RGBA; source/review only |
| [originals/ui_button_endturn_generated.png](originals/ui_button_endturn_generated.png) | 1254×1254, original imagegen RGBA; source/review only |
| [originals/ui_button_endturn_pressed_generated.png](originals/ui_button_endturn_pressed_generated.png) | 1254×1254, original imagegen RGBA; source/review only |
| [originals/ui_gem_ap_generated.png](originals/ui_gem_ap_generated.png) | 1254×1254, original imagegen RGBA; source/review only |
| [qa/alpha-proof.png](qa/alpha-proof.png) | 1160×464, offline art proof, never runtime |
| [qa/contact-sheet.png](qa/contact-sheet.png) | 1296×1728, offline art proof, never runtime |
| [qa/intents-24-32.png](qa/intents-24-32.png) | 912×208, offline art proof, never runtime |
| [qa/slice-proof.png](qa/slice-proof.png) | 1220×900, offline art proof, never runtime |
| [manifest.json](manifest.json) | Machine inventory, slot/native size, both border orders and per-PNG SHA-256 |
| [preview.html](preview.html) | Offline labelled asset browser; no network or game logic |
| [prompts.md](prompts.md) | Exact generation/edit prompts, methods, references and limits |
| [sources/build-kit.mjs](sources/build-kit.mjs) | Reproducible original vector artwork and mechanical PNG exports |
| [sources/verify-kit.mjs](sources/verify-kit.mjs) | Offline PNG alpha/dimension/cap checks and proof-sheet generation |
| [qa/export-checks.json](qa/export-checks.json) | Fresh measured results for all 105 runtime PNGs |
| [qa/verdict-01.json](qa/verdict-01.json) | Initial revise verdict: export-edge safety margin |
| [qa/verdict-02.json](qa/verdict-02.json) | Palette revise verdict against existing attribute SVG colours |
| [qa/verdict-03.json](qa/verdict-03.json) | Final art-only self-review, pass 94/100 |
| [.omx/state/battle-kit/ralph-progress.json](.omx/state/battle-kit/ralph-progress.json) | Required local visual-verdict checkpoint; inactive/completed, no runtime mode launched |
| [SHA256SUMS.txt](SHA256SUMS.txt) | Checksums of authored delivery files, excluding itself and hook-managed session metadata |
| [README.md](README.md) | This inventory and handoff |

Existing .omx/.omc session files and hook-generated logs are not art deliverables or import inputs. Only the battle-kit visual checkpoint above was authored for this pass.

To re-export without adding dependencies, use an existing Sharp installation and Node: set BATTLE_KIT_SHARP to its resolved module directory, then run node sources/build-kit.mjs and node sources/verify-kit.mjs. The default path records the existing local npm cache used in this session; it is not a portable dependency declaration. Production and verification tools perform no network calls.
