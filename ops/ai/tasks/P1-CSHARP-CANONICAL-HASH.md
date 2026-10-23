# P1-CSHARP-CANONICAL-HASH

- 원문: `docs/source_v0.8.1/16_chapter.md` §16.2, `20_chapter.md` §20.9 P1-1.
- 역할: Battle/Run의 명령별 교차 재생을 준비하는 공통 JSON·SHA-256 계약. 전체 전투 포팅의 대체물이 아니다.
- 담당: Codex GPT-6.1 Sol Max. 독립 교차 검토: 공식 Claude Code Opus Max.
- 허용 변경: Core의 새 canonical serializer, CoreChecks의 관련 새 검사와 연결, 필요한 최소 JS serializer 경계 벡터. 기존 계산·예약·성장·가챠·게임 데이터·매니페스트는 유지한다.
- 완료 기준: netstandard2.1/C#8, Core 외부 패키지 없음; JS의 UTF-16 키 정렬·문자열 escape·정수·배열 순서·재귀 log/ev 제외와 일치; 실제 1,000개 JS 기록의 2,000개 시작/종료 스냅샷 해시 대조; 기존 202개 검사 유지; 오류 입력은 명확히 실패.
- 진행: IMPLEMENTING. C# Battle/Run 명령 실행과 273,748개 중간 상태 재생은 별도 미구현으로 유지한다. 검사 수를 전체 전투 완료로 확대하지 않는다.
- 환경: 무료 공식 .NET10 SDK는 `ops/ai/local/dotnet-sdk10` 격리 설치. 기존 전역 SDK/PATH와 Unity 편집권은 유지한다.
