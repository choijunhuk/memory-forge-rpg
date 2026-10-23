# C# 실제 적 행동·반복 턴

원문 §5.2–3·5.7–9·5.13–16·6.3·10.2·16.2/20.9에 따라 실제 `Create → begin → end → 다음 턴`을 구현했다. 카드·필살기를 사용하지 않고 플레이어 턴을 넘기는 도달 가능한 상태에서, 라이브20종·12행동·반격·화상·표식 수첩 보상·소환·저주·더미 재순환·회복·봉투/회로·종료 순서를 모두 실행한다. 원본 데이터·설계·수치와 엔진은 이번 단위에서 바뀌지 않았다.

[실제 Root C#검사](root_checks.txt)는 **29,232/29,232**,356사례·1,740요청(정상1,673·재시도14·거부53), 추가 영수증136회다. 267개 시작 조합,20종 전체 패턴 두 바퀴,21개 실제 조우,34경계,14프로토콜을 검증한다. 상태·이벤트·로그·RNG·공개 정보를 전체 비교하고 별도 해시도 확인한다. 기대 상태는 입력으로 주지 않는다. 기존 Core202·canonical87·스냅샷2,000·fixture13·opening2,221/267명령도 통과했다.

- [JS222개](node_tests.txt), [새 생성기 현재성](oracle_freshness.txt), 제안14·Python48·문서9·인계13 PASS.
- [공식 Opus Max 리뷰](../../ops/ai/tasks/P1-CSHARP-TURNS-CROSS-REVIEW.result.json)와 [별도 Sol Max 소스 리뷰](native_review.json)는 APPROVE다. [실제 Max 메타데이터](opus_effort_metadata.json)·[정확한12개 검토 소스](review_sources.json)를 보관한다. 리뷰어는 직접 시험하거나 gzip을 열지 않았으며 실행 증거는 Root 로그다.
- [Unity 실제 PlayMode7개](unity_playmode.json): 기존5개와 새 적 행동/2·3턴·2턴 회로 사망2개. [EditMode3개](unity_editmode.json)도 PASS. 새 시험의 실제 로딩을 먼저 reflection으로 확인했다. [최종 Console](unity_console.json)은 오류0·경고0 조회이며 이전 단위의 도메인 리로드 전송 경고 기록은 별도 보존한다. Root만 Editor를 썼다.
- 회복 대상의 거의 같은 비율이 JS double에서 동률이 되는 실제 입력을 재현했다. 단순 정수 분수 비교 대신, Core 정수 연산이 53비트 반올림과 등장 순서를 보존한다. 실제 end6에서 나방을 회복하는 사례가 C#에서도 통과했다. 별도 Sol 검토의380,935회 비율 계산 비교는 수학 보조 검증이며 실제 C# 명령 증거와 구별한다.
- [첫 WT 실행 기록](worktree_checks.txt)의 SDK9 오류는 컴파일 전 환경 실패다. 같은 기록의 SDK10 성공과 [Root SDK10 성공](root_checks.txt)을 구별한다. 전역 SDK/PATH는 바꾸지 않았다.
- [보존표](preservation.json)의40개 원본 파일과 입력 매니페스트는 전부 유지했다. 앞 요청ID 커밋9dc5c77의 [CI 두 개](receipt_commit_ci.json)는 그 head의 성공이며 다음 head에 재사용하지 않는다.

`StartTurn(first)`·적 생성·의도 설정·카드 생성·뽑기·RNG·원자적 명령 처리기를 재사용했다. 기존 전체 그래프 비교 함수는 의미를 바꾸지 않고 두 검사기가 공유하도록 추출했다. 소환된 적은 같은 seq를 쓰되 그 턴의 행동 스냅샷에는 들어가지 않으며, 얼룩 정리는 중간에 끊지 않는다. 같은 치명 타격의 반격·표식 보상을 보존한 후 DEFEAT 우선순위를 판정한다.

**남은 범위:** `play`·`ult`·예약/부채/파워/균열의 생산·복원·Run·기억 저장/서버, 원문§5.16의 실제 보루 카드 경계, 후보후반23종은 별도다. 도달하지 않는 미이식 상태·태그·패시브·phase 메타데이터는 명시 거부한다. 전체1,000 Run의 C# 명령 재생은0개다. Unity 시험은 typed runtime fixture이고 전체 게임 UI·콘텐츠 임포트·새 macOS 게임 빌드·Android/IL2CPP·실기기·사람 수용은 아니다.

```sh
node tools/gen_battle_turn_vectors.mjs --check
node --test engine/tests/battle_turn_vectors.test.mjs
dotnet run --project core/MemoryForge.CoreChecks -- .
```
