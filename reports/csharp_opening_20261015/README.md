# C# 실제 전투 생성·첫 턴 시작

원본 v0.8.1 §5.1–3/5.9/5.13–18, §6.3, §16.2와 §20.9의 첫 공유 전투 실행이다. `Battle.Create`와 실제 원자적 `begin`을 구현했다. 원본 JS·설계·데이터·밸런스 수치는 유지했다.

완료 기준은 JS가 독립 생성한 전체 상태에 대한 C# 실제 실행 동치다. 10명×공명0–6, 22무기×제련1–5, 32유물, 20적 의도, 경계35개로 **267사례**를 대조한다. 생성 상태59키와 시작 후 전체 상태·이벤트 순서·공개 정보·canonical hash를 각각 비교하며 기대 상태를 입력으로 주입하지 않는다. [2,220개 검사](csharp_root_checks.txt), 기존 Core202·canonical87·시작/종료스냅샷2,000·fixture13 검사가 모두 통과했다.

- [현재 소스 체크섬](review_sources.json), [JS178개](node_tests.txt), [제안14개](proposal_tests.txt), [Python48개](python_tests.txt), [문서9개](docs_tests.txt), [인계13개](handoff_tests.txt).
- [UTF-16 어댑터 RED](csharp-opening-host-red.txt) → [키·값·rid 손실 없는 GREEN](csharp-opening-host-green.txt). JS가 허용하는 단독 surrogate의 속성 이름도 대체 문자로 바꾸지 않는다.
- Sol Max 독립 검토의 두 숫자 결함을 수정했다. 공명 +2로 `int.MaxValue` 스킬/필살기 레벨이 음수가 되던 것을 `long`으로 보존하고, 음수 난도 적 HP는 BigInteger 반올림→최소1→정수 변환 순서로 처리한다. [추가 숫자 경계 검사](csharp-opening-numeric-green.txt).
- [Unity 실제 PlayMode5개](unity_playmode.json): 새 전투 시작·요청 재시도·회로 사망·봉투 생존3개와 기존 Core2개. [EditMode3개](unity_editmode.json)도 통과했다. 처음에는 기존2개만 발견되어 [그 결과](unity_first_discovery.json)는 새 기능 성공으로 세지 않았다. 전체 자산 갱신·타입 로딩 확인 후 새3개가 포함된5개 결과를 확보했다.
- [Console](unity_console.json)은 오류0·MCP 도메인 리로드 전송 경고1이다. [시험 설정 복원](unity_settings_restored.json) 후 기존 serialized Editor 설정의 의미상 차이는 없다. root Codex만 Editor 쓰기를 수행했다.

수치 계약: 현재 HP·방어·AP 등 전투 정량 출력은 Int32이며 공명으로 증가하는 스킬·필살기 상태 레벨은 `long`이다. RNG는 기존 `uint32`, 플래그 그래프는 JS-safe 정수를 보존한다. 범위를 넘는 결과는 `BATTLE_NUMBER_OUT_OF_RANGE`로 명시 거부하고 명령의 상태·RNG·로그·영수증을 보존한다. 요청 인수는 JS-safe 정수만 지원하며 분수는 `UNSUPPORTED_REQUEST_NUMBER`다. 임의 JS-safe 입력 전체에 대한 이식 완료를 뜻하지 않는다.

알려진 별도 JS 결함: 기존 `receipts={}`는 `__proto__`·`constructor`·`toString` 같은 상속 속성을 저장된 요청으로 오인한다. C#은 의도된 own receipt 의미를 따르는 안전한 Dictionary를 사용한다. 이 결함은 다음 독립 수정 단위로 재현·수정하며, 여기서 잘못된 JS 동작을 복제하지 않는다.

**남은 범위:** `play`·`ult`·`end`, 다음 턴·예약 해결·전체 효과/적 행동·Run·기억 저장/복원은 미이식이다. 전체1,000 Run 트레이스에서 C#이 재생한 명령은 여전히0개다. Unity 시험은 typed runtime fixture이며 완전한 콘텐츠 임포트·게임 화면·사람 플레이·Android/IL2CPP·실기기·새 macOS 게임 빌드 수용이 아니다.

재현:

```sh
node tools/gen_battle_opening_vectors.mjs --check
node --test engine/tests/battle_opening_vectors.test.mjs
dotnet run --project core/MemoryForge.CoreChecks -- .
```

의미 있는 단위의 공식 Claude Opus Max 검토 결과는 `ops/ai/tasks/P1-CSHARP-OPENING-CROSS-REVIEW.result.json`에 남긴다. 로컬 검사 통과와 상대 리뷰 승인·Git 통합은 따로 판정한다. 이 첫 실행 단위가 끝나도 의존성이 해결된 다음 전투 동작 개발을 이어간다.

## 교차 리뷰·통합 판정

공식 Claude Opus Max의 읽기 전용 독립 검토는 **APPROVE**다. [CLI 수신](../../ops/ai/tasks/P1-CSHARP-OPENING-CROSS-REVIEW.result.json)·[실제 Max 메타데이터](opus_effort_metadata.json)·[전체 단위 판정](verification.json)을 함께 보관한다. 리뷰어는 시험을 직접 실행하지 않았고, 수신의 Unity NOT_RUN은 검토 당시 제공된 자료의 경계다. root가 이후 같은12개 소스 체크섬을 검증하고 위 PlayMode5/EditMode3 결과를 직접 확보했다. 오류를 숨기거나 과거 PASS를 새 실행으로 바꾸지 않았다.

비차단 향후 주의: 실제 실행은 항상 원자 후보 Battle에서만 수행한다. 현재 root Battle의 private RNG는 직접 실행용이 아니므로 새 명령도 후보 경로를 우회하지 않는다. 전체 적 행동의 나머지 패턴 항목은 다음 실제 end 명령 검사에서 비교한다.
