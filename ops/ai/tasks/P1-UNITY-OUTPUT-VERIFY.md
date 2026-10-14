# P1-UNITY-OUTPUT-VERIFY

직전 P1-INTEGRATION-DELTA-REVIEW의 유일 차단 4번 출력경로만 두 번째 확인한다. 이전 승인 1~3과 다른 파일을 다시 감사하지 않는다. 읽기 검토만, Bash/MCP/다른 AI/계정 읽기는 없다.

core/Directory.Build.props에 SDK공식 UseArtifactsOutput/ArtifactsPath를 패키지밖 ops/ai/local/dotnet-artifacts로 고정했다. csproj와기존C#계산코드는변경하지않았다. Codex가 실제로 문서 그대로 `dotnet run --project core/MemoryForge.CoreChecks -- .`를 --artifacts-path 없이 실행해 exit0,202+87+2000+13 PASS를 회수했다. Core/CoreChecks의bin/obj4경로가생기지않고출력은고정경로에만있음을검사했다. 직후실제MCPresolve_packages+refreshcompile을수행하고Console오류/경고0을확인했다. 해당6개입력만갱신했다. CoreREADME의과거미컴파일설명은이력으로보존하고새실제UnityPlay근거를추가, AI환경문서빌드링크를final로고쳤다.

공식SDK문서 https://learn.microsoft.com/en-us/dotnet/core/sdk/artifacts-output 와 설치SDK10.0.402의 DefaultArtifactsPath/DefaultOutputPaths targets로속성지원확인. root전역SDK/PATH는변경하지않음.

기존차단해제가능/불가와한줄근거만답하고, 실행시험은본인NOT_RUN으로구분한다. 새로운전역개선제안은하지않는다. 두번째에도쟁점이면근거를남겨해당Unity연결만보류하고다른개발을계속한다.

검토입력SHA256:
```json
{
  "core/Directory.Build.props": "80ac522ae808a9ae1c19088df1ece385047354a207279f374f49c30a4925e2bb",
  "core/README.md": "cf40f08f3caadc6722a42f4b20dbbf72793f1dfb3d4e0a5e4396ad857775488a",
  "docs/AI_ENVIRONMENT.md": "d946cf6abf75da0fcf23ce5ea80ce52fc1525ccc655321b4454e9c339d4298d4",
  "reports/ai_environment_20261015/shared_core_layout.json": "b4eedfc88ba40e8b623e038aba6441f7a91ae1439f38774d3db626388df92a6a",
  "reports/ai_environment_20261015/default_dotnet_run.txt": "4db6fb20c1038d94be46cb13ceda1630f0e59e5883c11e721f43c9d98776b39c",
  "reports/ai_environment_20261015/shared_core_console.json": "162edadc505ade1558136f302fa68296addf29d8ef50077886ad285c350a9437"
}
```

## 실행 증거 정정

검토 요청을 송신할 때 합산 Console에 MCP WebSocket 리로드 경고1개가 있다는 점을 0으로 잘못 요약했다. 원본 shared_core_console.json에 그 경고를 그대로 보존한다. 별도의 오류 전용 조회는 shared_core_errors_only.json을 따른다. C# 컴파일 완료와 연결 도구의 실제 응답을 확인했으며, 전송 재초기화 경고를 C# 컴파일 오류로 합산하거나 삭제해 숨기지 않는다.
