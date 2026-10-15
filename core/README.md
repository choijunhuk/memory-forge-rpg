# core/ — C# 규칙 코어

- `MemoryForge.Core`(netstandard2.1, C# 8, 외부 패키지 없음, 경고=오류): `Rng`(xoshiro128**/splitmix32), `Gacha`(천장 상태 기계·중복·선택권), `Stats`(성장 스탯·스테이지 배율·경험치), `ReservationBook`(예약 칸·봉인·앞당김).
- `MemoryForge.CoreChecks`(net10.0): `data/game_data.json`과 `engine/tests/vectors/rng_gacha_vectors.json`(JS 엔진이 생성)을 읽어 같은 결과를 내는지 검사한다.

```sh
dotnet run --project core/MemoryForge.CoreChecks -c Release -- .
# → CoreChecks: 202/202 passed  (2026-10-14, .NET SDK 10.0.112)
```

전투·탐사·기억 저장 전체 이식은 P1-1(`docs/design_v0.8.1.md` §16.2, §20.3). Unity 실제 컴파일·IL2CPP 실행은 아직 하지 않았다.


현재 실행·구독·자동 호출·이미지 담당 지침은 `docs/AI_WORKFLOW.md`다. 위 과거 실행 예시는 새 실행 증거가 아니다.

## 2026-10-15 검증 갱신

원문·기존 실행 기록은 위에 보존한다. 현재 Core에는 canonical JSON/SHA-256도 추가됐으며 기존202개·canonical87개·시작/종료 스냅샷2000개·fixture13개 검사가 통과했다. **Battle/Run 명령 재생은 아직0개, 전체 이식은 미구현**이다. 현재 명령·출력은 `reports/csharp_checks.txt`와 `reports/cross_language_20261015/README.md`를 따른다.

Unity6가 로컬 UPM으로 동일 Core 소스를 직접 컴파일했고 실제 PlayMode2개·EditMode3개가 통과했다. Android/IL2CPP·전투 UI·실기기는 이 검증에 포함하지 않는다. `Directory.Build.props`는 기본 .NET 명령도 `ops/ai/local/dotnet-artifacts`에 출력을 모으게 하므로 Unity 패키지 안에 DLL/생성소스를 만들지 않는다. CLI의 임시 `--artifacts-path` 옵션에만 안전성을 의존하지 않는다.

## 실제 Battle 생성·시작 실행

`BattleDefinitions`·`BattleState`·`Battle`에 typed 입력/분리된 상태 사본·원자적 `begin`·독립 영수증을 추가했다. [새 검증 기록](../reports/csharp_opening_20261015/)에서 실제267개 시작 명령과2,220검사, 기존202+87+2,000+13 PASS를 확인한다. Unity PlayMode5개 중3개는 이 전투 API를 실제 실행한다. 위의 명령 실행0개는 과거 스냅샷 기반 기록이며, 전체1,000 Run 트레이스 재생 명령 수는 여전히0개다.

`play`/`ult`/`end`·다음 턴·Run/저장/복원은 명시적으로 미지원이다. 현재 전투의 HP·방어·AP 같은 정량 출력은 Int32, 스킬/필살기 상태 레벨은 `long`이며 표현 범위를 넘는 결과는 `BATTLE_NUMBER_OUT_OF_RANGE`로 거부한다. RNG는 기존 `uint32` 상태를 유지하고 플래그의 손실 없는 JSON 정수와 요청 인수는 JS-safe 정수다. 요청 분수는 `UNSUPPORTED_REQUEST_NUMBER`다. 이 범위를 전체 JS 입력·전투 이식 완료로 확대하지 않는다.

## 실제 적 행동·반복 턴 실행

`end`·라이브20종의12행동·다음 턴을 구현했고 [356사례/1740요청·29232검사](../reports/csharp_turns_20261015/)와 기존전체검사, Unity PlayMode7/EditMode3이 통과했다. 원문과 대조한 공식 Opus Max·별도 Sol Max는 APPROVE다. `StartTurn(first)`·적 생성/의도·RNG·원자적 후보 실행을 재사용하고 전체 그래프 비교기는 두 host검사가 공유한다.

현재 지원은 `Create/begin/end`다. `play`/`ult`, 예약·부채·파워·균열을 생산하는 경로, Run/저장/복원·전체1000Run명령 재생은 남아 있다. unknown상태/태그/phase를 조용히 생략하지 않는다. 위의 과거 미지원 end 설명은 이 절로 갱신하며 게임 데이터·수치를 변경하지 않는다.
