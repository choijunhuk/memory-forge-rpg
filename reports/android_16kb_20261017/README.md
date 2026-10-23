# Android 16KB 페이지 경고 제거 — 2026-10-17

결과: **해결.** Burst를 1.8.19에서 1.8.29로 올렸다. 새 개발 APK의 네이티브 라이브러리 6개가 모두 16KB(0x4000)로 정렬된다. Galaxy S24 Ultra(Android 16)에서 「Android 앱 호환성」 경고 없이 바로 지도가 열린다. 같은 기기에 이전 APK를 다시 깔면 경고가 그대로 뜨는 것도 확인했다(대조군).

## 1. 원인

- 이전 APK에서 정렬이 4KB(0x1000)인 파일은 `lib_burst_generated.so` 하나뿐이었다. 나머지 5개(libunity·libil2cpp·libmain·libc++_shared·libswappywrapper)는 0x4000이다.
- 기기 경고 목록에서 구체적인 사유가 붙은 항목도 이 파일 하나다: `lib_burst_generated.so: LOAD 세그먼트가 정렬되지 않음`. 다른 5개는 「알 수 없는 오류」로만 표시된다(`02_before_dialog_libs.png`). 수정 후에는 목록 자체가 나오지 않는다.
- 프로젝트는 Burst를 직접 쓰지 않는다. Burst는 2D·URP·Collections 패키지를 통해 간접 의존(depth 2)으로 들어와 1.8.19로 해석돼 있었다. `BurstAotSettings_Android.json`은 없어서 기본 설정으로 빌드됐다.
- Burst 1.8.19는 Android용 `.so`를 Unity 내장 NDK r27c(27.2.12479018)의 clang으로 링크한다. 이때 넘기는 링커 인자는 `--shared -Wl,--no-undefined -Wl,--gc-sections -Wl,--build-id -lm -latomic`뿐이다(`Burst.Compiler.IL.dll` 문자열). 페이지 크기 지정이 없으므로 r27c 기본값인 4KB가 적용된다. Unity가 직접 링크하는 libunity·libil2cpp는 16KB를 명시하므로 정렬이 맞다.
- 1.8.19의 CHANGELOG에도 「[Android] Support for 16Kb page sizes」 줄이 있다. 그러나 그 버전의 링크 인자에는 관련 플래그가 없다. 공식 문서 변경 이력은 이 항목을 **1.8.21**(2025-03-19)에 둔다: "Added support for 16 KB page sizes in Android".
- 1.8.29의 같은 DLL에는 `-Wl,-z,max-page-size=16384`와 `-Wl,-z,common-page-size=16384`가 들어 있다.

## 2. 수정

`client/MemoryForgeUnity/Packages/manifest.json`에 `"com.unity.burst": "1.8.29"`를 직접 의존으로 추가했다. `packages-lock.json`에서는 burst가 1.8.19(depth 2)에서 1.8.29(depth 0)로 바뀌었다. burst의 의존인 mathematics는 1.3.2 그대로이고 depth만 2에서 1로 바뀌었다. 새 패키지는 없다.

- **설정 파일 대안을 쓰지 않은 이유:** Burst AOT 설정에는 링커 인자 항목이 없다. 링크 인자는 Burst 내부 코드가 정한다.
- **NDK 교체를 쓰지 않은 이유:** 기본값이 16KB인 NDK r28로 바꾸려면 Unity 내장 NDK를 교체해야 한다. 이는 새 도구 설치이고, Unity 6000.0.46f1이 지원하는 NDK 버전도 벗어난다.
- **Burst 끄기를 쓰지 않은 이유:** Android에서 Burst를 끄면 URP·2D 패키지의 Burst 작업이 관리 코드로 돌아간다. 정렬 문제는 사라지지만 성능을 버리는 방식이다. 버전 갱신으로 해결되므로 쓰지 않았다.
- **1.8.29를 고른 이유:** 16KB 지원은 1.8.21부터다. 이후 수정 중 이 프로젝트와 관련된 것은 다음과 같다.
  - 1.8.22: 1.8.21의 alias 성능 저하 수정
  - 1.8.25: Android ARMv9 보안 기능 크래시 수정
  - 1.8.27: `Span<T>.Fill` 범위 밖 쓰기 수정
  - 1.8.28: 특정 산술 오컴파일 수정

  1.8.30(기본 LLVM 21, LLVM 10·15·16·17 제거)과 1.8.31(2026-10-07, 기본 LLVM 23)도 이 Editor에서 쓸 수 있다. 하지만 둘 다 컴파일러 기본값을 크게 바꾼다. 16KB 수정에 필요한 범위보다 변경 폭이 넓으므로, 그 직전 버전인 1.8.29를 골랐다. 1.8.29의 요구 Editor는 2022.3 이상이다.
- **다른 패키지의 요구 버전과의 관계:** 2d.common은 burst 1.8.4, collections 2.5.1은 1.8.17, render-pipelines.core는 1.8.14 이상을 요구한다. 모두 1.8.29로 충족되며, 직접 의존이 간접 의존보다 우선한다.
- **빌드가 다시 쓴 파일:** `ProjectSettings.asset`(빌드 스크립트의 메모리 설정과 Android 블록), `UnityConnectSettings.asset`, `ShaderGraphSettings.asset`, `Assets/Settings/UniversalRP.asset`, `Assets/UniversalRenderPipelineGlobalSettings.asset`이 바뀌었다. 모두 되돌렸다. `BurstAotSettings_*.json`과 빌드 스크립트는 바꾸지 않았다.

## 3. 빌드 (worktree 별도 batchmode, 루트 Editor 미접촉)

- 명령: `MF_APK_OUT=<scratch>/MemoryForge-dev-16kb.apk Unity -batchmode -quit -nographics -buildTarget Android -projectPath <worktree>/client/MemoryForgeUnity -executeMethod MemoryForgeAndroidBuild.BuildDevApk -logFile <log>`
- 결과: `MF_ANDROID_BUILD result=Succeeded backend=IL2CPP arch=ARM64`, 159초, errors 0, warnings 1(이전 빌드와 같은 IL2CPP 경고류).
- APK: 47,141,412 bytes, SHA-256 `8d5959f1556b74ea008b0da9974e45a83a72e32a1e22505f0b4c9d6a92fe48c5`. 사본은 `ops/ai/local/builds/MemoryForge-dev-16kb-20261018.apk`(Git 제외, 이 worktree)에 있다.
- Library는 `android-device-smoke` worktree의 것을 APFS 복제(`cp -c`)해 썼다. 패키지 해석과 Burst 컴파일은 새로 했다.
- Unity 잠금(`exclusive-control.lock`)은 잡지 않았다. 공유 Editor가 아니라 이 worktree 경로의 별도 batchmode 프로세스이기 때문이다. 이전 Android 단위와 같은 방식이다.

## 4. 정렬 표 (`llvm-readelf -lW`, NDK r27c, LOAD 세그먼트 Align)

| 라이브러리 (arm64-v8a) | 이전 APK (Burst 1.8.19) | 새 APK (Burst 1.8.29) |
|---|---|---|
| `lib_burst_generated.so` | **0x1000** | 0x4000 |
| `libc++_shared.so` | 0x4000 | 0x4000 |
| `libil2cpp.so` | 0x4000 | 0x4000 |
| `libmain.so` | 0x4000 | 0x4000 |
| `libswappywrapper.so` | 0x4000 | 0x4000 |
| `libunity.so` | 0x4000 | 0x4000 |

- `lib_burst_generated.so`의 크기는 602,616 bytes에서 549,880 bytes로 줄었다. 새 Burst의 LLVM 버전 변경 때문으로 보인다.
- zip 정렬: `zipalign -c -P 16 -v 4`(build-tools 36.0.0) 결과 Verification successful. `.so`는 압축 저장(Defl:N)이며 설치 때 추출된다(`extractNativeLibs`). Unity 내장 build-tools 34.0.0의 zipalign에는 `-P` 옵션이 없다.

## 5. 실기기 (SM-S928N, Android 16, adb R3CX10PCWCW)

| 단계 | 결과 |
|---|---|
| 새 APK `install -r` | Success. `dumpsys package`의 `pageSizeCompat=0` |
| 콜드 실행 `am start -W` | Status ok, LaunchState COLD, TotalTime 389ms(재설치 후 2차 304ms) |
| 첫 화면 | 경고 대화상자 없이 지도가 나왔다. 포커스 창은 `UnityPlayerActivity`다(`03_after_launch.png`) |
| 1층 칸 탭 | 전투 화면으로 넘어갔다(`04_after_battle.png`) |
| logcat (앱 pid) | Unity 예외·치명 오류는 0이다. 오류 줄은 기존과 같은 기기 드라이버 탐색 로그뿐이다(Gralloc 4×4, Adreno QSPM, libpenguin) |
| **대조군: 이전 APK `install -r`** | `pageSizeCompat=4`. 실행하자 포커스 창이 시스템 `android` 대화상자였고 「16KB와 호환되지 않습니다. ELF 정렬 검사에 실패했습니다」가 떴다(`01_before_dialog.png`, `02_before_dialog_libs.png`). 「확인」만 눌렀다. 「다시 표시 안함」은 누르지 않았다 |
| 마무리 | 새 APK를 다시 `install -r`했다. 기기에는 새 APK가 설치된 상태다(`pageSizeCompat=0`) |

- 다른 앱 삭제나 기기 설정 변경은 하지 않았다.
- **확인하지 못한 것 (1): 16KB 커널 실행.** 이 기기의 커널 페이지 크기는 `getconf PAGE_SIZE` = 4096이다. 경고는 Android 16의 정적 검사(ELF·zip 정렬)이며, 실제 16KB 페이지 커널 위의 실행은 이 기기로 확인할 수 없다. 확인하려면 16KB 시스템 이미지의 Android 에뮬레이터나 16KB 개발자 옵션을 지원하는 기기가 필요하다.
- **확인하지 못한 것 (2): Burst 코드 실행.** 지도·전투 화면까지는 프로세스 maps에 `lib_burst_generated.so`가 올라오지 않았다. 해당 경로에서 Burst 작업이 호출되지 않은 것으로 보인다. logcat에 Burst 관련 dlopen 실패는 없다. Burst 코드의 실기기 실행은 이번에 확인되지 않았다.

## 6. PC(macOS)·기존 시험 영향 (Burst 1.8.29, batchmode)

같은 worktree에서 순서대로 실행했다(`-batchmode -runTests`, macOS는 `-buildTarget OSXUniversal -buildOSXUniversalPlayer`).

| 항목 | 결과 |
|---|---|
| EditMode | **PASS** 3/3 (exit 0) |
| PlayMode | 315/316. 실패 1건 (exit 2). 아래 대조 실행으로 이번 변경과 무관함을 확인했다 |
| macOS 빌드 | **PASS** `Build Finished, Result: Success`, 114MB. `Contents/PlugIns/lib_burst_generated.bundle` 포함 |
| macOS 실행 | **PASS** 1600×900 창 모드로 10초 실행했다. Player 로그 24줄에 오류·예외 0(Metal, Apple M3 Max) |

- **PlayMode 실패:** `PracticeServerSessionPlayModeTests.CardDetailsShowTheCurrentServerTextWhollyScrollableReadOnlyAndCloseWhenStale`(`PracticeServerBattleControlsPlayModeTests.cs:335`)이 `the label is as tall as its wrapped text — Expected: >= 1508.04907, But was: 1482.0`로 실패했다.
- **대조 실행:** manifest·lock을 원래대로(Burst 1.8.19) 되돌렸다. 해당 클래스만 같은 batchmode로 다시 돌렸다. 로그상 `burst@7a907cf5a459`(1.8.19)로 재해석·재컴파일됐다. 결과는 133/134이고, **같은 시험이 같은 숫자로 실패**했다(1482.0 vs 1508.04907).
- 따라서 이 실패는 Burst 변경 전부터 있던 것이다. batchmode에서 텍스트 줄바꿈 높이가 달라지는 환경 문제로 보이며, 원인 확인은 이 단위 범위 밖이다.
- 엔진·C# 검사(`tools/run_all_checks.sh`)는 Unity 패키지를 읽지 않아 다시 돌리지 않았다(NOT_RUN).

## 7. 스크린샷

| 파일 | 내용 |
|---|---|
| `01_before_dialog.png` | 이전 APK(Burst 1.8.19): 「Android 앱 호환성」 16KB 경고 |
| `02_before_dialog_libs.png` | 같은 경고의 라이브러리 목록. `lib_burst_generated.so: LOAD 세그먼트가 정렬되지 않음` |
| `03_after_launch.png` | 새 APK: 경고 없이 첫 화면(지도) |
| `04_after_battle.png` | 새 APK: 1층 칸 탭 후 전투 화면 |

`01`, `02`는 용량 때문에 1400px 폭으로 줄였다(비생성형 리사이즈).

## 8. Codex 읽기 전용 리뷰

- 호출: `command codex exec -s read-only -m gpt-6.1-sol -c model_reasoning_effort=high`
- 세션 `01a14a61-f908-74c2-ab9f-31d2e02ea670`, 모델 gpt-6.1-sol, 노력 high, 종료 0. 판정 **APPROVE**. Codex는 파일을 수정하지 않았다. 원문은 `codex_review.md`에 있다.
- `implementation_defect`: 없음. 직접 의존 1.8.29가 세 패키지의 요구 버전(1.8.4·1.8.17·1.8.14)을 모두 충족한다. mathematics의 depth 1도 맞다.
- `design_mismatch`: 없음. lock만 고치면 요구 버전이 드러나지 않고, 캐시 패치나 Editor 갱신은 범위가 넓다. 이번 방식이 가장 작은 올바른 수정이라고 봤다.
- `optional_opinion` (채택): 1.8.30을 제외한 이유를 「최소 Editor 6.0」으로 들면 안 된다는 지적이다. 이 Editor는 6.0 조건을 충족한다. §2의 근거를 「컴파일러 변경 폭」으로 고쳤다.
- `real_constraint` (기록):
  - 이 APK의 `.so`는 압축 저장이므로 비압축 라이브러리의 zip 정렬은 검증되지 않았다.
  - 출시 AAB와 분할 APK, 추가 라이브러리·ABI는 별도로 확인해야 한다.
  - PlayMode 1건 실패의 기준선이 필요하다. 이후 §6의 대조 실행으로 기준선을 확보했다.
  - 1.8.31은 x64 호스트에서 ARM64 Editor를 띄울 때의 서명 문제를 고친다. 이 Mac은 arm64이고 Editor·Hub 실행 파일도 모두 arm64(`file` 확인)이므로 해당하지 않는다.
- `optional_opinion`: 빌드가 다시 쓴 설정 파일은 이 변경에서 빼라는 의견이다. 이미 되돌린 상태다.

## 9. 루트 세션 감사

- `--start-check` 통과. 감사 `root_session_audit.json`도 통과했다.
  - session `937e5c46-6c08-45a4-8295-f578a2c1f405`, 모델 claude-opus-5-5만 사용, 권한 모드 bypassPermissions.
  - 위반 0, 증거 누락 0, Codex 호출 1건(규칙 형식).
- Unity 잠금 이벤트는 없다. 공유 Editor를 쓰지 않았기 때문이다.
- 경고는 허용 목록 밖 MCP 서버가 노출만 된 것뿐이다. 호출은 없었다.
- 감사 미검증 항목은 보고서 `unverified`에 있다. Usage credits OFF 여부는 계정 화면에서만 확인할 수 있다.

## 10. 남은 일

- **출시 AAB 확인:** 출시 빌드(AAB, 릴리스 서명) 단계에서 같은 정렬 검사를 다시 한다.
  - AAB는 보통 네이티브 라이브러리를 비압축으로 넣으므로 `.so`의 zip 16KB 정렬(`zipalign -c -P 16`, `bundletool` 분할 APK 기준)도 함께 본다.
  - Play Console도 업로드 때 16KB 호환성을 검사한다.
- **16KB 커널 실행 확인:** 에뮬레이터 16KB 이미지에서의 실행 확인은 별도 단위로 남긴다.
- **PlayMode 실패 확인:** batchmode에서 실패하는 시험 1건(§6)은 Editor 실행과 비교해 원인을 확인할 대상이다.
