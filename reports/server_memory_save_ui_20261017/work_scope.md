# 서버 기억 저장 UI — 작업 범위

요구사항: 원본 v0.8.1 §8.9–8.14의 before 보존·변화 선택·패배 파티 전체 0~1변화·전체 결과 덱 용량, §14.7/§17의 서버 권위·원자적 저장·동일 요청 복구.

게임 내 역할: 서버 원정 종료 → 미전송 변화 선택 → 서버 미리보기 → 원자적 기억 확정 → 다음 서버 원정 시작. 로컬 원정/기억 엔진을 교체하지 않는다.

완료 기준: 현재 문맥/선택에 대한 서버 미리보기만 저장 허용; 오래된 응답·중복·미확정·앱 복귀 차단/복구; 공용 카드 배정; 완료 후 서버 확정 기억 표시; 현 소스 .NET 및 실제 Unity Play/Editor 검증; Codex 독립 리뷰; PR #2 통합.

병렬 작성 소유권: Opus xhigh 전송 담당은 PracticeServerSession.cs, PracticePublicSnapshot.cs, 기존 PracticeServerSessionPlayModeTests.cs. Opus xhigh UI 담당은 MemoryForgeServerPracticeClient.cs, 새 부분 클래스 PracticeServerSavePlayModeTests.cs. 루트 Codex는 읽기 독립 검토·범위별 통합·검증/증거를 담당하고 Editor 쓰기권을 독점한다.

Core·서버·원본 수치·저장 의미·입력 매니페스트·기존 장면을 변경하지 않는다. 신규 의존성/운영 배포/실계정 변경 없음. 자동/프로토콜 fixture 시험과 실제 Development HTTP·Unity uGUI 조작·물리 입력/실기기는 구분한다.
