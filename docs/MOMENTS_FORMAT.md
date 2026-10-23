# 캐릭터 모먼트 데이터 형식 — `memory-forge-moments/1`

담당: 이 **형식**과 읽는 코드는 Claude(`OWNERSHIP.md` §3), 파일 **내용**(장면·대사·표정·모션·타이밍·카메라 의도)은 Codex다. 원본은 `art/moments/<캐릭터id>/moments.json`이다. Claude는 `tools/import_moments.py`로 원본과 그 파일이 가리키는 그림을 Unity `Assets/Resources/`에 바이트 그대로 복사하고, Unity의 `MemoryForgeMomentPlayer`가 재생한다. 형식에 없는 표현이 필요하면 Codex가 `art/requests/`에 형식 확장을 요청한다. Claude는 표현을 형식에 맞춰 줄이지 않고 형식을 넓힌다.

## 파일

```json
{
  "format": "memory-forge-moments/1",
  "character": "sera",
  "status": "draft",
  "images": {
    "fig_default": "moments/sera/images/fig_default.png",
    "fig_smile": "moments/sera/images/fig_smile.png",
    "blush": "moments/sera/images/overlay_blush.png"
  },
  "anchors": { "fig_default": { "face": [0.5, 0.17], "hands": [0.46, 0.55] } },
  "motions": {
    "check_scope": { "fps": 12, "frames": ["moments/sera/frames/check_scope/0001.png"], "loop": false },
    "turn_to_player": { "tracks": { "figure": [ { "t": 0, "x": 0, "y": 0, "rot": 0, "scale": 1, "alpha": 1 },
                                                { "t": 0.45, "x": -18, "y": 4, "rot": -2, "scale": 1.03 } ] }, "loop": false, "ease": "inOut" }
  },
  "scenes": [ { "id": "sera_lobby_01", "kind": "lobby", "...": "아래 표" } ]
}
```

- **경로**는 `art/` 기준 상대 경로다. 그림은 PNG(투명 가능)다.
- **`status`**: `draft`(Codex 초안) 또는 `approved`(사용자 승인 확정본). `approved`는 사용자가 승인했을 때만 쓴다.
- **`idle_figure`** (선택): 아무 장면도 재생하지 않을 때 로비에 보일 그림 키. 없으면 첫 `lobby` 장면의 첫 `figure`.
- **`images`**: 장면이 보여 주는 모든 그림. 전신(포즈·표정 조합), 얼굴·홍조·손 같은 덧그림, 소품, 배경.
- **`anchors`**: 그림 안의 기준점(0–1, 왼쪽 위 원점). 카메라 `focus`가 이름으로 가리킨다.
- **`motions`**: 두 가지 중 하나 또는 둘 다.
  - `frames` + `fps`: 프레임 시퀀스.
  - `tracks`: 슬롯(`figure` 또는 덧그림 슬롯 이름)별 키프레임. `x`·`y`는 기준 해상도 1920×1080의 픽셀, `rot`는 도, `scale`·`alpha`는 배수.
  - `ease`: `linear`·`in`·`out`·`inOut`. `loop`: 반복 여부. `slot`: `frames`를 바꿀 슬롯(기본 `figure`).
  - **슬롯 규칙:** `tracks`는 슬롯의 위치·회전·크기·투명도를, `frames`는 슬롯의 그림을 움직인다. 박자가 슬롯의 그림(`figure`·덧그림)을 바꾸면 그 슬롯의 `frames`는 끝나지만 `tracks`는 이어진다(다가서기 뒤 표정만 바꿔도 제자리로 튀지 않는다). 같은 슬롯에 새 모션을 시작하면 이전 모션을 대신한다. 덧그림의 `x`·`y`를 바꾸면 이어지는 `tracks`는 새 위치를 기준으로 움직인다. 반복(`loop`)하는 모션은 길이가 0보다 커야 한다.
  - Unity AnimationClip이나 2D Animation 리그로 만들 때는 `"clip": "motion/sera/xxx.anim"`처럼 원본 경로를 적는다(가져오기와 재생은 다음 단계에서 붙인다).

## 장면

| 필드 | 뜻 |
|---|---|
| `id` | 고유 ID (`<캐릭터>_<kind>_<번호>`) |
| `kind` | `lobby`(A 로비 반응) · `bond`(B 인연 장면) · `entry` · `victory` · `skin`(C 연출) |
| `title` | 제목 |
| `trigger` | 발생 조건. `on`: `tap`·`idle`·`enter`·`bond_level`·`time`·`event`. 필요하면 `bond_level_min`·`time`(`morning`·`day`·`evening`·`night`)·`note` |
| `why` | 캐릭터가 이 행동을 하는 이유 |
| `intent` | 반드시 보존할 창작 의도. 구현 리뷰의 기준이다 |
| `assets` | 필요 자산 목록. 항목마다 `{ "key", "type": "image\|motion\|voice\|music\|sfx\|vfx", "status": "ready\|todo" }` |
| `input` | `skippable`(건너뛰기), `advance`(`tap` 탭으로 다음, `auto` 자동), `repeatable`, `cooldown_s` |
| `stage` | 선택. `background`(그림 키), `music`(곡 이름 또는 `silence`) |
| `beats` | 순서대로 재생하는 박자 목록 (아래) |

### 박자 (`beats[]`)

모든 필드가 선택이다. 적지 않은 슬롯·카메라는 이전 박자의 상태를 이어 간다.

| 필드 | 뜻 |
|---|---|
| `figure` | 전신 슬롯에 놓을 그림 키 (포즈·표정 전환) |
| `overlays` | `{ "슬롯": "그림 키" \| null }`. 덧그림을 놓거나 지운다. 위치는 `{ "image", "x", "y" }`로 줄 수 있다 |
| `motion` | 재생할 모션 키 |
| `gaze` | 시선 의도(`player`·`away`·`down`·`side` 등, 연기 지시 겸 리뷰 기준) |
| `camera` | `shot`: `full`·`medium`·`bust`·`close`, `focus`: 앵커 이름, `move`: `none`·`push_in`·`pull_out`·`pan`, `duration` |
| `line` | `{ "speaker": "sera" 또는 null(지문), "text": "…", "tone": "음성 톤·연기 지시" }`. 긴 대사는 재생기가 줄바꿈·페이지로 나눈다. 내용은 줄이지 않는다 |
| `voice` | 음성 파일 경로(없으면 생략) |
| `music` · `sfx` | 곡·효과음 이름. `"silence"`는 짧은 정적 |
| `hold` | 이 박자 뒤 머무는 초. `advance: tap`이면 탭을 기다린 뒤 이어 간다 |

## 재생 규칙 (Claude 재생기)

- **박자 순서:** 그림·덧그림·모션·카메라·소리를 적용 → 대사 표시(글자가 나오는 중 탭하면 그 페이지 전체 표시) → `hold`만큼 정지(탭으로 줄이지 않는다) → `advance: tap`이면 탭 대기(▼ 표시), `auto`이면 읽는 시간 뒤 다음 박자. `hold` 중의 탭은 기억했다가 정지가 끝나면 바로 넘긴다. 대사가 없는 박자도 `tap` 모드에서는 탭을 기다린다.
- **긴 대사:** 대화창 크기에 맞춰 페이지로 나눈다. 페이지를 이어 붙이면 원문과 정확히 같다(공백·줄바꿈 보존). 마지막이 아닌 페이지는 `tap`이면 탭, `auto`이면 읽는 시간 뒤 넘긴다.
- **건너뛰기:** `skippable`이면 건너뛰기 버튼을 보인다. 장면이 끝나거나 건너뛰거나 중단되면 무대는 `idle_figure`(없으면 첫 로비 그림)로 돌아가고, 장면이 놓은 덧그림·배경·소리는 지운다.
- **없는 그림:** 아직 없는 그림(`status: todo`)은 그 자리를 비워 두고 대사·타이밍은 그대로 재생한다. 다른 그림으로 대신하지 않는다. `stage.background`가 아직 없으면 로비 배경 대신 빈 어두운 무대를 보인다.
- **카메라:** 캐릭터 무대의 확대·이동으로 구현한다. UI가 캐릭터를 가리면 UI 배치를 먼저 바꾼다. 로비 메뉴는 장면 재생 중 숨는다.
- **움직임 줄이기(접근성):** 켜면 카메라 이동은 즉시 전환, 키프레임은 마지막 자세, 프레임 시퀀스는 마지막 프레임만 보인다. 대사와 `hold`는 그대로다.
- **로비 반응:** `lobby` 장면은 `trigger.on`(`tap`·`enter`·`idle`·`time`)과 `repeatable`·`cooldown_s`를 지킨다. `bond`·`entry`·`victory`·`skin` 장면은 인연 단계·전투 이벤트가 아직 연결되지 않아 목록의 **미리보기**로만 열며, 이때는 조건을 건너뛰고 자산 준비 수를 함께 보인다.
