# P1-FINAL-CROSS-REVIEW

Opus Max 독립 검토. 두 번째 검토에서는 이전에 지적한 항목의 수정과 명시한 새 기능 단위만 검토한다. 끝없는 전역 재감사를 시작하지 않는다. 파일 편집/다른 AI/이미지/MCP/Bash 호출은 없다. 실제 실행 시험은 Codex가 별도 로그로 수행하며, 당신은 코드와 원문을 독립 대조한다.

## 재검토 대상
1. 앞선 P1-CORE-AND-RUNNER-REVIEW의 #1: 만기 예약 소멸 후 처치 즉시 종료, 대기 카드 보존, end 이벤트 1개. #2: 원문 §5.9에 따라 HP0 일반 회복 금지, memory_envelope 예외와 기존 동일타격 처치/보상 순서 유지. post_resolution_end8개 실재경로 회귀시험.
2. #3 rootHTML은 이미 빌드됐지만 이전 검토 worktree에 복사되지 않아 오탐이었다. 이번에는 root현재HTML의 정확한 바이트를 복사했다. 최신 엔진 포함 여부를 소스와 직접 대조한다.
3. #4 공식 balance_causes CLI가 기존 runProbe로 전체 정식 시나리오/기본시드/제한/원본해시를 재생성해 입력과 대조한다. 작은조우 analyze API는 유지. 요청크기 위조도 선차단. 실제34관련시험PASS.
4. #5 --restricted가 설정계층과 기본파일도구를 제한한다는 설치도움말/공식명세를 root가 확인했다. 이번에는 --setting-sources 빈값도 명시, MAX_THINKING_TOKENS 제거, implicit title모델 호출 비활성, Read스코프와 자격파일거부, UI-CLI계정일치의 로컬 해시 대조 추가. 이것을 OS샌드박스나 과금서버차단이라고 주장하지 않는다.

## 새 기능 단위
- 원문 §16.2/§20.9: gen_traces 700short+300full=1000, 실제 명령과 난수포함 상태 해시, 알려진거부의 원자성, 치명예외중단, 원자적gzipJSONL출력. 기본legacy임을명시, v2옵션은실제기존migrate/commit. 원본 요구를줄이지 않았는가?
- C#CanonicalState netstandard2.1/C#8, 외부패키지없음. UTF16키정렬/JSON.stringify문자열/정수범위/배열/log·ev제외 일치. CoreChecks의17경계벡터/2000스냅샷/출처·압축검증은 전체 Battle/Run 재생과 구분. 실제202기존+87canonical+2000hash+13fixture검사PASS. 명령273748개는C#에서실행하지않았음을출력한다.
- Unity6.0.46f1 무료MCP 연결, 실제씬/Canvas/Prefab/Play/3EditMode/macOS빌드/앱실행을root가확인. EditorBridgeTests를검토하되전체게임구현이라고확대하지말것. defaulttemplate전체를읽을필요없다.
- 출고파일검사: Git출고목록을재사용하고ignoredUnityLibrary/로컬인증을제외. force-added폰트/인증은검사대상유지. 비GitZIP은운영캐시만제외. 매니페스트는재생성하지않음. 검사약화나인증유출경로가생겼는가?

## 응답
각 단위 통합 가능/차단/보류를 분리하고, 실제 결함은 파일·행·재현·원문 근거를 적는다. 근거 없는 스타일 제안은 구분한다. 새로운 중요한 결함이 있으면 통합을 보류하지만 같은 쟁점을 무한히 리뷰하지 않는다. NOT_RUN 실행/인간플레이/Android/C#명령재생/서버 범위를 명시한다. 캐릭터·아트·밸런스 수치나 원문은 변경하지 않는다.

## 검토 입력 정확한 SHA256

```json
{
  "engine/rules-core.js": "b1ba35f433197072200825ffa85cac49cf3d4812c4bf02032793fef8491d63eb",
  "engine/tests/reflect_ultimate.test.mjs": "648faebdd901b99713dd91c749bc4c322d38c16136e51bfc4aaf4e00381d7eeb",
  "engine/tests/enemy_lethal_multihit.test.mjs": "7becd72845b61961de378fe83e2fb93d8a72b03e59c36dffb76614822dc796b4",
  "engine/tests/player_lethal_reflect.test.mjs": "ca5ec6214b205f32064d8123714d60ad6ad097f5cf167f32fc481bcf34c3465f",
  "engine/tests/post_resolution_end.test.mjs": "f6232bd32a1f1d8d1e19a033a240ebb5c7fa6b13554aafdbff8de6e5c1b37fba",
  "tools/review/balance_causes.mjs": "54189384e106ef7de25cc6dc43fb9e65335ed1c39963c6650ce7be52ba569d32",
  "engine/tests/balance_causes.test.mjs": "852e3b2b266aec9d3b88d924cef9922aa71204c01a10438745403d039c17d26c",
  "tools/ai/run_claude.py": "dd2f78b29ea80f10fd64ded69b29c5dc37d04f9a559c7b7fcf83bccb997499ce",
  "tests/test_ai_runner.py": "e486b66a5a017b523c09d92df21a3f4d2c9f341d47728894a6085490554aa511",
  "tools/ai/delivery_files.py": "befd42cc83978f251f1d79f0bb1d42225b7448e472aca388156f6c55fea7216e",
  "tools/ai/build_manifest.py": "f14f89f2aa8f0fcc70d8f92259fab4a748522058eed192342e861513e5525d04",
  "tools/ai/test_handoff.py": "0fe5e49caca346a56b8f34333cef04350ba7b96f40c5a346edf0d026c526ebdd",
  "tests/test_delivery_files.py": "804e5e156ae714cd88e8b58aec22129d5168b4dbf1833f203a3115105e48b102",
  "tools/gen_traces.mjs": "f071d6d815e7fe3f41d602824d6437fa0e2c9d9b043f33c929e7b7b600a05963",
  "engine/tests/trace_export.test.mjs": "e2ec09e5f8133f93080cac5364139e23f292f16a9a48c3178847a06cd35b8cd2",
  "tools/gen_hash_vectors.mjs": "22dd5d418a84cd13ea750beaa1d6ce439ccfeac35489b4e68e6e843b9931c96b",
  "engine/tests/canonical_hash_vectors.test.mjs": "e1421407f1932c6bceb15de2fa7f0e192bb25e42152bbb7f39a38939e0e4f480",
  "engine/tests/vectors/canonical_hash_vectors.json": "429076667030a9d3ed27175cad9106628e3aa3aff4519935acf95fa791bbba53",
  "core/MemoryForge.Core/CanonicalState.cs": "d9b8ecd832c3f07aec057828e302c02c80f3eec4fb980dab9ff22ed0a9585f8f",
  "core/MemoryForge.CoreChecks/TraceHashChecks.cs": "51c7cb4cfbfe3cd93fbce596189d34ec62093fe15d53f3f85be04700bfc5a958",
  "core/MemoryForge.CoreChecks/Program.cs": "871db3355ddaa6aaa8549f473abeff180db2f43c6a712b34003b083c868d5340",
  "prototype/index.html": "54c0244ac12182915321b2a645c46d2f531ecdd8593c8d567cbd014c92713dbc",
  "docs/AI_ENVIRONMENT.md": "2c29330f538976e1f54210c886710bf84b8278ae59e0afed70e7c97b61fe6cc0",
  "docs/IMPLEMENTATION_STATUS.md": "96f0141f2bc041c19ac61b9284b54a05ed13b3544784228db699fbd5e9a7321b",
  "reports/cross_language_20261015/export.json": "4abde454db3b5aeaa30b89c6d2f3847bca056918229af4c39d8dd6e444679311",
  "reports/cross_language_20261015/replay.json": "c617715a7c29c81c2e6118c9f6b3774b43e12bcdbdfbb84b6ec6c48bf2c46982",
  "reports/ai_environment_20261015/claude_unity_mcp.json": "3cd9dbaa7052ef1fb3505754f58d40860a08eb07866efd53c1c271ba04772982",
  "client/MemoryForgeUnity/Assets/Tests/Editor/EditorBridgeTests.cs": "68e70bd465a52660d12cf9897cbd0a41259ae26f9ed8b3f195b442b888081499",
  "client/MemoryForgeUnity/Assets/Tests/Editor/MemoryForge.EditorTests.asmdef": "92ee7071db2078fb1c571e8ca8ac9c45f99d98f0f052379f901a7a5bc525cbdb",
  "client/MemoryForgeUnity/Packages/manifest.json": "bdc2cb9b748168d9212a32b912a7e599dcab3ff6fb16fb2564dd1fc05d5a49fa"
}
```
