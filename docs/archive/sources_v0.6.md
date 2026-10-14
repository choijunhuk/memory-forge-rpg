# 출처

출처·검증 이력·계산의 성격

### 사용자 제공 자료

**I04:** memory_forge_design_v0.4.pdf(79쪽), memory_forge_handoff_v0.4.zip. 기존 설계·인계의 기준 자료다. 원문의 역할·수치·가격을 보존하되69절에 적은 변경을 반영했다.

**I05:** 붙여넣은 텍스트(1)(6).txt, 1,018행. 사용자가 가져온 v0.5 제안·C#·Python·ADR. 인용의 ‘L’은 이 첨부의 제공된 행 번호다. 제안된 구현이 실제로 완성·검증되었다는 증거는 제공되지 않았으며, 이번 정적 감사와 새 실행 시험을 분리했다.

**검산:** reports/economy_results.json, all_python_tests.txt, ui_results.json은 이번 작업의 실행 결과다. simulation은 가정을 계산한 값이지 관측 유저 데이터가 아니다.

### 이번 기술·정책 재확인 — 2026-10-14

#### R01 · Unity C# compiler and language version

C# 지원·init/record 제한. Unity 버전과 실제 컴파일은 별도.

[https://docs.unity3d.com/Manual/csharp-compiler.html](https://docs.unity3d.com/Manual/csharp-compiler.html)

#### R02 · Unity .NET profile support

.NET Standard2.1 프로필 근거. IL2CPP 실제 검증을 대체하지 않음.

[https://docs.unity3d.com/6000.1/Documentation/Manual/dotnet-profile-support.html](https://docs.unity3d.com/6000.1/Documentation/Manual/dotnet-profile-support.html)

#### R03 · Microsoft System.Random API

의사난수와 보안 목적 난수의 구분. 전투 RNG 설계는 본 프로젝트 제안.

[https://learn.microsoft.com/en-us/dotnet/api/system.random?view=net-10.0](https://learn.microsoft.com/en-us/dotnet/api/system.random?view=net-10.0)

#### R04 · Android accessibility

터치 목표·접근성. CSS 시제품 검사는 실제 dp/sp 검사가 아님.

[https://developer.android.com/guide/topics/ui/accessibility/apps](https://developer.android.com/guide/topics/ui/accessibility/apps)

#### R05 · Codex MCP official documentation

공식 MCP 연결 안내. 실제 사용자 환경에서 연결하지 않음.

[https://developers.openai.com/codex/mcp](https://developers.openai.com/codex/mcp)

#### R06 · Claude Code MCP official documentation

공식 MCP 연결·권한 안내. 단일 편집권은 프로젝트 가드레일.

[https://code.claude.com/docs/en/mcp](https://code.claude.com/docs/en/mcp)

#### R07 · Google Play Billing security

백엔드 구매 토큰 검증·구매 상태·중복 지급 방지·ack/consume.

[https://developer.android.com/google/play/billing/security](https://developer.android.com/google/play/billing/security)

#### R08 · CoplayDev unity-mcp

제3자 제작자 저장소. Unity 공식 제품으로 표현하지 않음.

[https://github.com/CoplayDev/unity-mcp](https://github.com/CoplayDev/unity-mcp)

#### R09 · Google Play service fees

지역·프로그램별 요율 구분. 모형의15/30%는 적용 확정 요율이 아님.

[https://support.google.com/googleplay/android-developer/answer/112622](https://support.google.com/googleplay/android-developer/answer/112622)

#### R10 · Google Play Billing testing

라이선스 테스터·테스트 결제 수단. 테스트 트랙에서 일반 구매는 실제 청구 가능.

[https://developer.android.com/google/play/billing/test](https://developer.android.com/google/play/billing/test)

#### R11 · Android target SDK requirements

2026 일반 신규·업데이트 API36. 제출일 콘솔·예외 재확인.

[https://developer.android.com/google/play/requirements/target-sdk](https://developer.android.com/google/play/requirements/target-sdk)


### 이전 참고 문헌의 지위

다음 S01~S28은 v0.4에 포함된 참고 문헌을 보존한 것이다. 이 개정에서 모든 게임 리뷰·현재 게임의 일정·가격을 다시 확인했다는 뜻은 아니다. 원문 자료와 새 추론·설계안을 혼합하지 않도록 I04/I05/R코드를 구분했다.


### S01 — Smilegate 뉴스룸 — 카제나 1막 개편·세이브 편집
공식 발표 / 2026.07

메인 스토리·풀더빙 개편과 세이브 데이터 편집 개선을 참고했다. 모든 모드의 현재 사망 규칙을 증명하는 자료는 아니다.

https://newsroom.smilegate.com/bbs/board.php?bo_table=news&wr_id=2292

### S02 — Chaos Zero Nightmare 공식 커뮤니티
개발사·퍼블리셔 공식

카드·탐사·세이브 관련 안내의 공식 진입점. 동적 게시판이며 개별 모드의 최신 규칙은 실제 버전 공지에서 재확인한다.

https://page.onstove.com/chaoszeronightmare/kr

### S03 — 트릭컬 리바이브 — Google Play 소개
퍼블리셔 작성 상점 소개

캐릭터 일상, 집 꾸미기·요리·연구 등 생활 요소의 존재를 참고했다. 우리 게임의 호감도 수치는 자체 제안이다.

https://play.google.com/store/apps/details?hl=ko&id=com.epidgames.trickcalrevive

### S04 — HoYoverse — How do Encounter Points work?
공식 고객지원

원신의 활동 기반 일일 보상 대체 방식을 참고했다. 제안된 일일 점수와 결정 수량은 원신 수치가 아니다.

https://support.hoyoverse.com/hc/en-us/articles/50333944750105-How-do-Encounter-Points-work

### S05 — HoYoverse — Spiral Abyss reset and rewards
공식 고객지원

나선 비경은 매월 16일, 환상극은 매월 1일 갱신이라는 안내를 확인했다. 우리 게임의 14/42일 구조는 별도 제안이다.

https://support.hoyoverse.com/hc/en-us/articles/50333950598553-When-does-the-Spiral-Abyss-reset-and-what-are-the-rewards

### S06 — Honkai: Star Rail — Version 4.5 공지
공식 버전 공지 / 2026.08.26

일부 종말·허구 회차의 이용 가능 기간이 5주로 조정된 안내. 동적 페이지의 검색 색인에 노출된 확인 범위도 포함한다.

https://hsr.hoyoverse.com/en-us/news/165767

### S07 — Honkai: Star Rail — Version 4.6 공지
공식 버전 공지 / 2026.09.28

혼돈의 기억 연속 회차 등 일정 예외를 참고했다. 신규 개방 간격과 각 회차 유지 기간을 구분해야 한다.

https://hsr.hoyoverse.com/en-us/news/166468

### S08 — NIKKE — 간편 상담 관련 업데이트
공식 업데이트 / 2024.03.27

처음 보는 관계 대화와 반복 대화의 처리를 나누는 참고. 최신 니케 전체 상담 규칙을 그대로 복제하지 않는다.

https://nikke-kr.com/newsdetail_m.html?col_name=NEWS&content_id=aa6f3a9dad40ea4890ab4dfa4b0bb7a3a0ed&father_content_id=aa6f3a9dad40ea4890ab4dfa4b0bb7a3a0ed&from=list&sid=496

### S09 — NIKKE — PC 버전 FAQ
공식 안내

모바일 외 PC 지원의 참고 사례. 우리 게임의 결제·계정 연동 설계는 별도 구현과 검증이 필요하다.

https://nikke-en.com/newsdetail_m.html?content_id=e1d7fc3aa68caa4d90ab1b9ac8249cc1d097

### S10 — Wuthering Waves — Version 2.6 preview
퍼블리셔 배포 보도자료 / GamesPress

반복 도전의 기록 계승 등 편의 개선 참고. 공식 뉴스 진입점: wutheringwaves.kurogames.com/en/main/news.

https://www.gamespress.com/WUTHERING-WAVES-PREVIEWS-VERSION-26-BY-SUNS-SCOURGE-BY-MOONS-REVELATIO

### S11 — Game8 — Tower of Adversity guide
2차 공략 / 2026.09 갱신

28일 주기 안내는 이 공략 기준이며 공식 원문 전체 확인과 구분했다. 명조의 모든 도전 모드가 28일 주기라는 뜻은 아니다.

https://game8.co/games/Wuthering-Waves/archives/453474

### S12 — PC Gamer — Slay the Spire review
전문 매체 리뷰 / 2019

강력한 카드 조합과 플레이 경험에 대한 평가를 참고했다. 점수를 다른 무료 게임과 합산 비교하지 않았다.

https://www.pcgamer.com/slay-the-spire-review/

### S13 — Slay the Spire — Google Play 이용자 리뷰
개별 이용자 경험 / 지역·시점 변동

모바일 카드 조작·가독성·진행 상태 관련 불편 사례. 모든 이용자·기기의 공통 결함이라고 단정하지 않는다.

https://play.google.com/store/apps/details?hl=en&id=com.humble.SlayTheSpire

### S14 — Android Developers — Test Play Billing
공식 기술 문서

라이선스 테스터·시험 결제 수단·패키지 일치 조건. 일반 테스트 트랙 구매가 실제 청구될 수 있다는 주의 사항.

https://developer.android.com/google/play/billing/test

### S15 — Android Developers — Billing security
공식 기술 문서

구매 토큰 확인과 백엔드 검증, 중복 지급 방지 참고. 환경별 지갑·권한 경계는 이 프로젝트의 설계안이다.

https://developer.android.com/google/play/billing/security

### S16 — Google Play — Target API requirements
공식 정책 / 제출일 재확인

2026년 8월 31일 기준 일반 신규·업데이트의 API 36 요구를 확인했다. 연장·기기 유형·향후 변경은 제출 시 콘솔에서 확인한다.

https://support.google.com/googleplay/android-developer/answer/11926878

### S17 — Google Play — New personal accounts testing
공식 정책 / 적용 대상 구분

해당 신규 개인 개발자 계정의 12명·연속 14일 비공개 테스트 등 접근 요건. 모든 개발자 계정의 공통 요건은 아니다.

https://support.google.com/googleplay/android-developer/answer/14151465

### S18 — Google Play — Inappropriate content
공식 콘텐츠 정책

연령 등급과 스토어 콘텐츠 허용 범위는 별도다. 최종 일러스트·영상·대사·상점 이미지를 함께 검토해야 한다.

https://support.google.com/googleplay/android-developer/answer/9878810

### S19 — 대한민국 정책브리핑 — 확률형 아이템 표시
정부 제도 안내 / 2024.01.12

2024년 시행 확률정보 공개 제도의 안내. 2026년 현재의 모든 법령 해석·예외를 대체하는 자료는 아니며 출시 전 현행 기준 확인이 필요하다.

https://www.korea.kr/multi/visualNewsView.do?newsId=148924522

### S20 — Google Play — Payments policy
공식 정책

디지털 상품 결제와 무작위 가상 아이템의 확률 고지 관련 정책. 국가·대체 결제 프로그램은 별도 검토한다.

https://support.google.com/googleplay/android-developer/answer/9858738

### S21 — CoplayDev / unity-mcp README
제작자 공식 저장소 / 제3자 도구

Unity Editor용 MCP 연결 도구. Claude·Codex 지원과 도구 범위는 설치 버전에서 확인한다. Unity 공식 제품으로 표시하지 않는다.

https://github.com/CoplayDev/unity-mcp

### S22 — OpenAI Codex — MCP documentation
OpenAI 공식 문서

Codex의 MCP 연결 방식 참고. 자동 리디렉션되는 공식 학습 문서도 확인했다. 실제 사용자 환경의 연결은 아직 수행하지 않았다.

https://developers.openai.com/codex/mcp

### S23 — Claude Code — MCP documentation
Anthropic 공식 문서

MCP 연결·권한 범위 참고. 작업 폴더 분리는 common-workflows 공식 문서도 참고하며 동시 편집 잠금은 프로젝트 규칙으로 제안했다.

https://code.claude.com/docs/en/mcp

### S24 — GitHub — Manage deployment environments
GitHub 공식 문서

환경 보호·승인 기능의 플랜·private 지원 범위를 구분했다. 지원하지 않는 보호 기능을 이미 적용했다고 표시하지 않는다.

https://docs.github.com/en/actions/how-tos/deploy/configure-and-manage-deployments/manage-environments

### S25 — OpenAI Codex — AGENTS.md guidance
OpenAI 공식 문서

저장소 공용 지침 파일의 역할을 참고했다. CLAUDE.md와 공통 결정 기록을 함께 두는 것은 이 프로젝트의 협업 제안이다.

https://developers.openai.com/codex/guides/agents-md

### S26 — GitHub CLI — gh repo create
GitHub 공식 CLI 매뉴얼

사용자 로컬에서 private 저장소를 생성할 스크립트의 명령 근거. 이 세션에서는 원격 저장소를 생성하지 못했다.

https://cli.github.com/manual/gh_repo_create

### S27 — Android Developers — Support 16 KB page sizes
공식 기술 문서

Unity·네이티브 SDK의 재빌드·정렬·실기기 검증 필요성을 참고했다. 상세 시행 시점은 변동 가능하므로 이 설계에는 검증 항목으로만 둔다.

https://developer.android.com/guide/practices/page-sizes

### S28 — Google Play — User data policy
공식 정책

개인정보 고지·수집 범위·계정 삭제 관련 준비. 실제 SDK 목록과 법적 보존 의무는 구현 이후 다시 대조한다.

https://support.google.com/googleplay/android-developer/answer/10144311
