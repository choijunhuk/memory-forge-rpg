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
