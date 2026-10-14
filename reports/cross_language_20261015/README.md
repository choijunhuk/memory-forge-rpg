# C# 이식 기반 — 실제 검증과 남은 범위

원문 §16.2·§20.9 P1-1에 따른 준비 작업이다. JS는 실제1,000탐험의273,748명령·275,748해시를 재생했다. C#은 동일 직렬화와2,000시작/종료 스냅샷만 검증하며 **Battle/Run 명령 실행은0개, 전체 동등성은 미구현**이다.

- [JS 출력](export.json), [전체 재생](replay.json), [최신 전체 시험의 트레이스9개](../balance_audit_20261015/engine_final.txt).
- [C# 실행 결과](csharp_snapshot_checks.txt): 기존202/202, canonical87/87, 스냅샷2000/2000, fixture13/13.
- 실제 corpus: `engine/tests/vectors/traces.jsonl.gz`, SHA256 `051e48ce77115239b62e1f661a921de342cf6eb632210b82d335a3d4f9e575b0`.

원문의 기본 `traces.json` 출력 인터페이스는 보존한다. 현재 CoreChecks가 소비하는 커밋된 검증 corpus는 손실 없는gzipJSONL이며, 재생성할 때는 아래 명령으로 **그 파일을 직접 갱신**한다. 기본 JSON을 새로 만들었다는 사실만으로 기존 압축 corpus가 갱신되지는 않는다.

```sh
node tools/gen_traces.mjs --output engine/tests/vectors/traces.jsonl.gz
node tools/gen_hash_vectors.mjs
dotnet run --project core/MemoryForge.CoreChecks -- .
```

새 엔진/데이터/생성기 소스에서는 corpus 출처 해시도 달라진다. 이전 corpus로 현재 소스를 검증했다고 보고하지 않는다. 모델 호출·API·유료 서비스는 이 명령에 없다. 전체 명령의 C# 재생이 구현되어야 원래 P1-1 인수 조건을 충족한다.

기존 Core의 실제 Unity 컴파일·PlayMode 검사는 [별도 환경 증거](../../docs/AI_ENVIRONMENT.md)를 따른다. 이 출력의 기본 memory policy는 legacy이고, v2 옵션 회귀 시험과 전체 v2 C# 이식은 구분한다. 독립 검토·통합 상태는 작업별 결과와 현재 진행 장부를 따른다.
