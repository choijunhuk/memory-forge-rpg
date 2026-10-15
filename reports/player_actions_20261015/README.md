# C# 실제 카드·필살기 실행 — A1

원본 v0.8.1 §5.4–6/5.10–18·§6·§16.2/20.9의 현재 저작 Battle 실행을 공유 Core에 연결했다. 전체173카드·378기본모드·39효과·10필살기와 예약/부채/파워/성장/복제/소멸/균열의 후속 턴을 실제 실행한다. 게임 데이터·수치·원문·아트·PDF·입력 매니페스트와 기존4개 corpus를 포함한47파일은 기준3d33c4c와 바이트가 같다.

독립 JS는992사례·3870요청(3837커밋/1재생/32거부),1430순수Check·214witness/550literal facts를 생성했다. C#은 시작 설정에서 실제 Create/명령을 호출해 전체 상태·이벤트·로그·RNG·공개정보·응답과5645영수증 재시도를 대조했고87991/87991이 통과했다. 기대 스냅샷은 비교에만 쓴다. 원본173/378 양성 분모는 실제 준비 명령 뒤 fizzle 없이 해결되며295저작 효과 경로/39op/12counter/20조건결과를 검사한다.

기존 Core202/canonical87/snapshot2000/fixture13/opening2221/turn29232/A0query128244, Node257·제안14·Python48·문서9·인계13도 통과했다. 실행 로그와 `verification.json`, `oracle_sources.json`, `review_sources.json`을 함께 확인한다. 기존1000Run 스냅샷은 실제 C# Run 명령 재생0과 구별한다.

## 발견·수정과 교차 검토

- P1: 소멸 중 표식 처치 보상이 더미를 재섞으면 이전 버린 더미 참조가 새 뽑을 더미가 됐다. 그 참조를 재사용해 추가 카드를 소멸시키고 잘못 승리하던 것을 매 반복의 현재 더미 조회로 고쳤다. 원본 설정/5명령의 C# RED→GREEN 전체 상태·응답과 Unity 실제 wrongVICTORY RED→GREEN을 보존했다.
- P2: picks를 너무 일찍 해석해 phase/mode/actor/gauge 오류와 사용되지 않는 인자의 동작을 바꾸던 것을 실제 소비 시점으로 늦췄다.8사례/21요청은9불일치→21/21전체일치다.
- 하네스 초기 출처 목록의 fuzzPolicy 누락은0명령에서 엄격히 거부됐다. 고정 출처를 추가했으며 검사를 제거하지 않았다.
- 별도 Sol Max의 초기/수정 리뷰를 보존했고 공식 Opus(`claude-opus-5-5`) Max는 현재 저작 A1 통합을 승인했다. 실제 CLI modelUsage/108개 effort·perTurnEffort 메타데이터, 포함 구독 인증/account binding과16개 소스 해시를 Root가 확인했다. Opus는 시험·gzip·해시를 직접 실행하지 않았으며 부모 증거와 소스 검토를 구분했다.

## 실제 Unity 및 한계

Unity6000.0.46f1/무료 고정 MCP에서 공유 Core의 PlayMode12/EditMode3이 통과했다. 불굴keep10, 예약 시 비용/해결 시 게이지, 필살기와 소멸 재섞기를 원본 수치로 실행한다. 시작·턴·질의 기존 시험도 유지한다. 기술씬 macOS빌드는0오류/0경고이며 Metal 시작/씬 로드의 runtime오류0을 확인한 뒤 **해당 테스트 프로세스만 강제종료(-9)**했다. 정상 종료 수용은 아니다. 첫 nographics/자동종료 가정의60초 timeout과 NullGPU shader 오류는 미수용 시도로 보존했다.

선별 typed fixture는 전체 콘텐츠 Unity 임포트·게임 UI·Android/IL2CPP·실기기·사람 플레이나 서버/결제의 완료 증거가 아니다. SourceInt32/JS-safe 숫자 경계, sourceNativeTypeError의 명시적 거부 범위와 원본base0 충돌(현행기본5유지)을 유지한다. 미래 음수지속버프/저작되지 않은 필살기 선택 경로는 Opus의 비차단 메모다. 다음은 독립 M0 기억 규칙의 전역 source-pin 갱신과 실제 Run 생성/지도, 이후 Run/저장 명령 재생이다.

최종 문서9·인계13검사를 새 운영 지침 반영 후 다시 통과했다. 실제 Play12/Edit3/빌드 이후 Editor MCP 브리지가 끊겨 인스턴스0을 확인했다. 같은 Editor의 화면 Console은 오류0·브리지 경고1이었으며 현재 재연결은 별도 후속 작업으로 기록한다(`unity_connection_followup.json`). 완료된 실제 시험과 현재 접속 상태를 구분한다.
