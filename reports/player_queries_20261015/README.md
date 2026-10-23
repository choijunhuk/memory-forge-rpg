# 전체 카드 정의와 순수 질의 기반 — A0

원문 §5.4–6·5.10–18·§6·§16.2/20.9의 플레이어 행동 이식 중 데이터·선택 UI 기반을 구현했다. 실제 `Create/begin`의 카드 개체에서 강화·공명을 적용한 사본, 사용 가능 모드, 비용, 대상 필요 여부를 조회한다. 실제 `play/ult`와 그 생산 상태를 실행하는 A1은 별도이며 이 단위로 완료 판정하지 않는다.

- [Root 실제 C#128,244/128,244](root_checks.txt): 702개 실제 시작·8,340질의·702영수증 탐침, 전체 정의와 상태/이벤트/로그/RNG 불변·반환 사본 분리를 검증했다. 기존202+87+2000+13·opening2221/267·turn29232/1740요청+136탐침도 유지했다.
- [JS234개](node_tests.txt), [현재성 검사](oracle_freshness.txt), 새 집중12개와 문법 검사가 통과했다. 173카드·378기본모드, 10인 공명0–6·22무기 제련1–5·32유물·16부품 정의를 포함한다.
- 실제 JSON의 false/0/부재를 구별하고, 카드·무기·부품·유물은 전체 원시 정의를, 캐릭터는 명시한 전투 정의(id/name/attr/passive/ult/resonance fx)를 비교한다. 원문 캐릭터 서사·외형은 삭제하거나 이 전투 투영으로 대체하지 않았다.
- [Unity 실제 PlayMode8개](unity_playmode.json): 기존7개와 새 카드 사본/모드/비용/대상 질의 시험. 반환된 사본과 목록을 수정한 뒤 상태·로그·이벤트 해시가 같고 원래 효과값도 유지된다. [EditMode3개](unity_editmode.json)도 통과했다. 이는 typed fixture이며 전체 콘텐츠 임포트·게임UI·실기기 수용은 아니다.
- [별도 Sol Max 소스 리뷰](native_review.json)는 APPROVE다. 공식 Opus Max 교차 리뷰는 작업별 정제 수신에서 별도 판정한다. [검토할10개 파일](review_sources.json)과 [보존40개 파일](preservation.json)의 체크섬을 확인했다.

원본 강화 배율1.3은 host에서 정확한13/10으로 읽고, 자동 강화→공명 순서로 사본에 적용한다. 명령 실행·RNG·버전·영수증 처리나 원본 정의를 조회 중 수정하지 않는다. 기존 Growth의 부재/false는 nullable로 보존하되 기존 전투의 성장 조건은 true일 때만 적용해 결과를 유지했다. Core에 JSON 패키지를 추가하지 않았다.

16부품 중14개 합법 영구 슬롯과 실제 시작 튜닝으로 생성되는 amp는 질의 경로에서 확인했다. 과열 임시 부품·침묵·다음 무료·비용 변조·무료 echo는 현재 fresh Create/begin 생산 경로가 없으므로 A1에서 실제 카드 명령으로 검증한다. 이를 영구 슬롯에 임의로 꽂거나 상태 주입으로 통과시키지 않았다.

원문 §6.3 base0 설명과 §9 시온 ‘망각’ 카드 표는 서로 충돌한다. 현행 JS·카드 표의 기본 방어5와 base0 메타데이터를 모두 유지하고 일반 DSL 조항만 미결로 기록했다. 카드·명세를 삭제하거나 숫자를 임의 조정하지 않았다.

**남은 범위:** A1의 모든173카드/기본378모드 실제 사용·필살기·예약/부채/파워/복제/소멸/균열 생산과 후속 턴, 전체 Run/저장/복원·서버·기기 수용. 전체1,000 Run C# 명령 재생은0개다.

```sh
node tools/gen_battle_player_query_vectors.mjs --check
node --test engine/tests/battle_player_query_vectors.test.mjs
dotnet run --project core/MemoryForge.CoreChecks -- .
```

## 교차 리뷰·통합 판정

공식 [Opus Max 리뷰](../../ops/ai/tasks/P1-CSHARP-PLAYER-QUERIES-CROSS-REVIEW.result.json)는 **A0 통합 APPROVE**다. [실제 Max 메타데이터](opus_effort_metadata.json)와 [최종 범위 판정](verification.json)을 보관한다. 리뷰어는 실행·gzip 열기·SHA 재계산을 하지 않았으며, Root가 실제 실행과10개 소스 해시를 확인했다. [최종 Console](unity_console.json)은 오류0·도메인 리로드 MCP 전송 경고1로 구별한다.

비차단 주의: `CostOf(uid)`의 새 C# 편의 기본값은 now이고 기존 JS 호출부는 모드를 명시한다. A1 내부 호출도 모드를 명시한다. 검증 범위는 원본 저작 데이터와 실제 도달 가능한 질의 상태이며, 임의로 변조한 대형 수치·잘못된 draw·미지 조건의 JS 동작 전체를 수용했다고 보고하지 않는다.
