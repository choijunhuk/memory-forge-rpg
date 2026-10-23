# P1-UNITY-SHARED-CORE

- 요구사항: 원문 §16.1의 클라이언트/서버 동일 C# 코어, §16.2 netstandard2.1, §16.4 Unity6 클라이언트.
- 역할: 기존 Core를 복제 없이 Unity에서 컴파일하고 실제 Play Mode에서 난수·예약·canonical hash의 기본 계약을 검증한다. Battle/Run 전체 이식과 게임 전투 UI는 별도 미완성이다.
- 담당: Codex GPT-6.1 Sol Max, Editor 단일 쓰기 담당 Codex. Claude Opus 독립 검토 후 통합한다.
- 허용 파일: Core 로컬 UPM 패키지 메타/asmdef/nullable 응답 설정, Unity manifest/lock, Play Mode 검증 assembly와 시험. 기존 Core 계산·수치·규칙은 수정하지 않는다.
- 완료 기준: 원본 Core 소스 유지, 무료 로컬 UPM 연결, 실제 Editor 컴파일 오류 0, Play Mode 시험 PASS, Console 오류 점검. .NET 검사와 Unity 런타임 검사는 구분한다.
- 참고: Unity 공식 로컬 패키지 경로는 프로젝트 Packages 폴더 기준이다. `file:../../../core/MemoryForge.Core`로 저장소의 기존 Core를 참조한다.
- 상태: IMPLEMENTING. Android/IL2CPP 모듈·전체 게임·서버/실기기·결제 완료로 확대하지 않는다.
