# 다음 구현 — 서버 연습 사건·기록소 화면

네가 작성한 휴식/공방/상점 단위는 Codex 독립 소스 검토 후 실제 Unity Editor PlayMode253/253(skip0), EditMode3/3, 같은 전송 NUnit .NET83/83을 통과했다. 작성본은 root에 통합해 검증 중이며 원본 source/수치·서버 명령은 바꾸지 않았다. 다음 의존성이 해결된 사건·기록소 화면을 실제 구현한다. 기존 네 작업은 보존한다.

Opus5.5, 구독 포함 사용량만. Codex는 이미지 생성·내용 수정·독립 검증·통합을 맡는다. 시스템/UI/스토리에 대한 의견과 합리적인 구현 판단은 자유롭게 제시하고, 기존 승인 범위 안의 일반 구현은 자율적으로 진행한다. 다른 Claude가 MCP/공통 정책 및 아트 도구를 수정 중이므로 그 파일은 수정하지 않는다. 커밋·푸시·배포·다른 AI 호출 금지.

## 담당과 완료 기준

- `client/MemoryForgeUnity/Assets/Scripts/MemoryForgeServerPracticeClient.cs`
- `client/MemoryForgeUnity/Assets/Tests/PlayMode/PracticeServerSessionPlayModeTests.cs`

v0.8.1 §7 노드/이벤트·기록소와 기존 서버 권위/재전송 계약에 따른다. 역할은 이미 공개된 server Run을 플레이어가 실제로 진행하게 하는 뷰이며 새 엔진이나 정책이 아니다. `MemoryForgeExpeditionClient`의 Event/Archive 뷰·선택 및 `Run.Services.cs`, `Run.Progression.cs`, `PublicView.cs`, `PracticeRunStore`의 실제 명령을 비교해 아래 빈칸을 구현한다.

1. 현재 LaterView인 EVENT/ARCHIVE를 공개 phase/pending 및 이미 있는 일치 content로 그린다. 사건 제목·본문·선택지·완료 상태와 기록소의 실제 선택지를 표시한다. 엔진/데이터에 없는 효과·확률·가격·나이·서사 변경은 만들지 않는다. 실제 content에 필요한 카드 제거/복제의 키 선택이 있으면 원래 규칙/개수대로 지원한다. 기존 덱 선택과 패턴을 활용하되 기존 휴식/공방/상점 선택의 의미를 바꾸지 않는다.
2. 사건은 기존 `event {index, keys?}`, 기록소는 기존 `archive {choice}`, 이후의 진행은 기존 `proceed`만 보낸다. 실제 Core가 수용하는 선택·조건을 확인한다. 버튼 1회=명령1개, 서버 승인 전 gain 표시 금지, uncertain/outbox 잠금·정확한 바이트 재시도·컨텍스트 변경 및 복귀 시 미전송 선택 폐기를 그대로 쓴다. 서버의 숨은 RNG·원정 seed·패턴을 클라이언트에서 계산/복구하지 않는다.
3. meaningful Unity-only protocol fixture 시험을 기존 파일에 묶는다. 사건 선택의 정확한 args/필요 키 개수·안전한 취소/오래된 선택 폐기, 처리 중 잠금/승인 전 상태, 기록소 기존 choice 및 이미 처리된 항목 재선택 거절을 실제 뷰로 검증한다. 각 UI용 synthetic fixture는 실제 content/서버 shape에 맞추고 원본 수치를 조작하지 않는다. 기존 .NET BCL-only 검사도 계속 컴파일되게 한다.
4. VICTORY/DEFEAT/DONE의 기억 저장 화면은 이번 단위에서 미구현임을 유지한다. 해당 UI까지 새 범위를 확장하거나 서버/엔진/고정 데이터를 수정하지 않는다.

전체 문서 재독·장시간 기획보다 필요한 기존 함수 확인→구현→작은 시험 작성에 집중한다. 실행 도구가 없는 이번 호출에서 Unity/셸 검증했다고 주장하지 말고 NOT_RUN을 남긴다. 정확한 변경과 Codex가 실제 Editor/서버로 확인할 흐름을 짧게 보고한다.
