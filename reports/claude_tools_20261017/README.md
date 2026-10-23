# Claude 자율 개발·무료 MCP·아트 도구 수용 — 2026-10-17

## 결과와 역할

Claude Opus5.5 공식 CLI가 별도 worktree에서 서버 노드 UI, 실행기/MCP/공통 정책, 아트 도구 보강을 병렬 작성했다. Codex는 경계 설정·지시·독립 소스 검토·실행 검증·통합을 맡았다. 캐릭터를 포함한 의견은 Claude에게 열고, 승인된 비성적 캐릭터 설정을 막연한 약관 추정만으로 삭제·축소하지 않는다. 채택 판단·캐릭터 외형 최종 디자인과 이미지 생성/내용 수정은 Codex다. 기존 v0.8.1·수치·가격·확률·승인 경계·Private 정책은 유지했다.

저장 기본값은 GPT-6.1 Sol High / Opus5.5 High다([설정](project_model_settings.json)). 실제 Claude 호출은 High·xhigh·Max를 구분한다([원래 종료 상태·모델 기록](actual_cli_metadata.json)). 첫 MCP Max 호출의 30분 TIMED_OUT은 보존했고 같은 Opus 세션 xhigh로 작성본을 회수했다. 모든 완료 응답의 modelUsage는 Opus5.5뿐이며 고정 계정의 claude.ai Max/firstParty 인증·일치와 24시간 이내 credits OFF/자동 충전 OFF UI 증거를 통과했다. 원시 인증·계정 정보·스트림은 공개 기록에 복사하지 않았다. 이는 로컬 확인이며 서버 과금 차단 장치가 아니다.

## 실제 도구 시험

| 경로 | 실제 결과 | 한계 |
|---|---|---|
| 지정 Bash 시험 | Claude가 handoff31/31, Python57/57을 직접 실행 | CLI는 일부 읽기 셸 명령을 별도 자동 허용한다. 정확한 허용 규칙이 OS sandbox라는 뜻은 아니다. |
| Unity 읽기 | CLI 연결, non-batch6000.0.46f1, 실제 state·Console 읽기 | 최초 연결은 미배정 도구17개의 노출도 발견했다. 실제 호출은 read_console뿐이었다. |
| Unity 쓰기 | 실제 Play→Pause→Resume→Stop, C# 상태 재조회, 원래 씬/정지/dirty=false 복구, Console0오류 | [실제 도구 응답](editor_control_tool_results.json), [Codex 독립 후속 조회](root_editor_after.json). 게임 진행/빌드/기기 수용으로 확대하지 않는다. |
| 무료 Peekaboo4.9.5 | 실제 로컬 stdio 연결·권한3종 확인·정확한 Unity 창 캡처·AX 조회 | [원본 캡처](native_unity_capture.png). analyze/agent 및 다른 모델은 호출하지 않았다. |
| native 좌표 입력 | fresh exact-window 좌표로 2회 시도 | `returned without valid canonical result semantics ... update the runtime host` 오류. 클릭 효과 미확인, 입력/복구 NOT_RUN. 두 번 실패 후 이 경로만 보류했다. |

Unity의 실제 `generate_image/generate_model/generate_audio/import_model/...` 이름을 정확히 차단하고 같은 prefix의 미허용 도구도 노출되면 실패하도록 고쳤다. 수정 후 실제 unity-write init에는 미허용 도구0개, apiKeySource none, 모든 관찰/조작 도구 응답 오류0이었다. Desktop 실제26개 중 무료 native21개만 노출했고 미허용0개였다. 임의 MCP URL/명령·사용자/클라우드 전체 MCP·유료 asset 공급자는 열지 않았다. worktree별 작업 잠금과 전역 Editor/화면 단독 제어 잠금을 사용한다.

### 셸 감사의 원래 FAILED와 채택 판단

경계 보강 작업의 CLI process exit는0이고 두 지정 시험도 통과했지만 실행기 결과는 **FAILED 그대로**다. 공식 CLI가 `git status/log/grep` 두 묶음을 읽기 명령으로 자동 허용해 지정 시험 외 실행 감사에 걸렸다. Codex가 [실제 응답](readonly_shell_audit.json)을 검토했다. 두 묶음은 작업 저장소의 상태/코드 조회였고, 추가 JSON/py_compile 묶음은 권한 거절되어 실행되지 않았다. 과금·비밀·배포·모델·이미지 호출은 없었다. 실패 판정을 약화하거나 성공으로 고치지 않고, 소스 변경은 Codex의 독립 검토·fresh handoff31/31 및 후속 실제 MCP 시험으로 채택했다. 일반 개발 조회의 자율성은 허용하되 CLI 도구 권한을 강제 OS 격리라고 주장하지 않는다.

## 아트 도구와 CI

Claude가 Codex의 아트 단위를 독립 리뷰했고, Codex는 실제 결함만 채택했다. 자동 시작 작업을 켜라는 우려는 원래 wrapper의 필요시 시작/자신이 시작한 프로세스만 종료하는 경로와 사용자 지시를 근거로 채택하지 않았다. 선택적 캐릭터 일관성 의견은 [별도 기록](art_review_notes.md)에 남겼다. 이미지 바이트는 변경하지 않았다.

- aiimg는 원격 호출 전 Python을 확인하고 PNG chunk 길이/CRC/IHDR/IDAT/IEND·잘림·후행 바이트를 검사한다. 게시 실패한 완성 PNG는 보존 경로를 알리고, 실패/빈 전송은 정리한다. 기존 핀·stdin/stdout·exact os.link·충돌 보존·Preview를 유지했다. [11개 독립 mock/fault 검사](aiimg_checks.json) PASS. 이번 보강 후 실제 SSH 재생성은 하지 않았다(추가 이미지 생성 없음).
- 전체 builder method body를 실제 Editor에서 실행해 디스크 파일·디렉터리·dangling symlink·폰트 누락·이미지 누락 거절과 기존9개 texture meta 불변을 확인했다([결과](art_builder_checks.json)). 검사용 임시 dangling link를 다른 실패 시험의 Refresh까지 남겨 Console import-loop1건이 발생했다. 대상은 모두 검증 fixture임을 스택으로 확인하고 제거/Refresh 후 재발이 없었다. 제품 씬·텍스처·수치를 수정한 오류가 아니다. 이후 컴파일/Play·Claude 제어 Console은0오류다.
- 최초 CI의 engine/Core 실패는 저장된 Run-create oracle의 전체 저장소 감사 목록이 art/provenance.csv 추가 전 상태였기 때문이다. 기존 generator로만 갱신했고 [2,131개 행·count·source pin 전부 동일, 감사29→30만 변화](fixture_audit_comparison.json)를 독립 확인했다. 원본 manifest/generator/검사 기준은 바꾸지 않았다. 새 CI failure-only 진단3판정도 [PASS](ci_diagnostics_checks.json)다.

## 검증·보존

Codex fresh 검증: JS333/333, Python57/57, proposals14/14, docs9/9, handoff31/31, 전체 CoreChecks 성공(특히 RunCreate215677/215677, supported factories2121/2121, queries45130/45130). GUI 결과와 서버 UI 범위는 [별도 수용 기록](../server_node_services_20261017/README.md)을 따른다. 신규 generic engine/ORM/유료 SDK는 없다.

[보호47 비교](preservation.json)는46개 동일, 이전 아트 커밋의 provenance14행 추가1개만 승인 변경이다. 따라서47개 모두 동일이라고 하지 않는다. 원본 설계·실행 수치·Core/engine·원래 씬·기존 release manifest·보호 실행기는 유지했다. 32개 밸런스 목표 이탈의 수용/활성화는 이번 단위로 승인되지 않는다.

ImagineArt 무료 크레딧 전용 예외는 공통 지침에 반영했지만 현재 호출 도구가 노출되지 않아 무료 잔액/차감 경로를 확인하거나 사용하지 않았다. 자동 유료 대체는 없다. Native pointer 호스트 호환성, 새 Android/standalone build·운영 배포·최종 아트 연결은 미검증이다.
