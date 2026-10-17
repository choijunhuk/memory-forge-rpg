# Android 개발용 APK 실기기 점검 — 2026-10-17

결과: **빌드 성공, 실기기 실행 미확인(설치 중 기기 연결 `offline`).** 사용 한도 도달로 재시도 전에 멈췄다.

## 브랜치 기준
- 지시된 worktree는 `main`(f4077c8)에서 시작해 루트 브랜치 `codex/late-actions-balance`(b93d928)보다 55커밋 뒤였고 `tools/ai/audit_root_session.py`가 없었다.
- 작업 전 `git merge --ff-only codex/late-actions-balance`로 이 브랜치를 루트 최신으로 맞췄다(로컬 변경 없음).
- `--start-check`: `pass: true` (auth, provider_env_clean, project_settings_pinned, root_denied_tools_enforced, codex_chatgpt_login 모두 true).

## 1. 모듈 설치 (Unity Hub 3.22.3 CLI, headless, 로그인/GUI 불필요)
```
"/Applications/Unity Hub.app/Contents/MacOS/Unity Hub" -- --headless install-modules --version 6000.0.46f1 \
  -m android android-sdk-ndk-tools android-open-jdk-17.0.9+9 --childModules
```
- exit 0, 235초. 설치: Android Build Support, NDK r27c, CMake 3.22.1, SDK Build Tools 34.0.0, Platform Tools 34.0.5, Platforms 33/34/35, Command Line Tools 16.0, OpenJDK 17.0.9+9.
- 위치: `/Applications/Unity/Hub/Editor/6000.0.46f1/PlaybackEngines/AndroidPlayer` (5.6 GB). `~/Library/Android/sdk`는 건드리지 않았다. EditorPrefs에 Android 경로 지정이 없어 내장 SDK/NDK/JDK를 쓴다.
- 로그의 `IndexedDB ... LOCK` 오류는 Hub GUI가 열려 있어서 나는 Electron 경고이며 설치에 영향 없음.

## 2. 빌드 (Editor 미접촉, worktree 별도 batchmode 프로세스)
- 스크립트: `client/MemoryForgeUnity/Assets/Editor/MemoryForgeAndroidBuild.cs` (`MemoryForgeAndroidBuild.BuildDevApk`).
- 명령: `Unity -batchmode -quit -nographics -buildTarget Android -projectPath <worktree>/client/MemoryForgeUnity -executeMethod MemoryForgeAndroidBuild.BuildDevApk -logFile <scratch>` (`MF_APK_OUT`로 출력 경로 지정).
- MCPForUnity는 batchmode에서 브리지·종료 정리를 건너뛴다(`UNITY_MCP_ALLOW_BATCH` 미설정) — 루트 Editor MCP와 충돌 없음.
- 첫 임포트(Library 재생성): 57초, exit 0. 이때 `com.unity.2d.pixel-perfect` 변환기 `CS0234` 16줄은 API Updater가 새 PackageCache를 패치하는 과정의 일시 오류다(2개 파일 수정 후 재컴파일; 루트 Editor 로그에는 0건).
- 빌드: **Succeeded**, 232초(BuildPipeline 216초), errors 0, warnings 1(IL2CPP 큰 메서드 경고류).

| 항목 | 값 |
|---|---|
| 구성 | Development Build, 씬 1개 `Assets/Scenes/MemoryForgeExpedition.unity` (전투는 같은 씬 안에서 그려짐, `LoadScene` 없음) |
| 패키지 | `com.memoryforge.rpg.dev` (빌드 스크립트 메모리 안에서만 지정) |
| 백엔드 | IL2CPP, ARM64 (`native-code: 'arm64-v8a'`) |
| 방향 | LandscapeLeft 고정(빌드 스크립트 안에서만) |
| SDK | compile/target 35, min 23 |
| 진입 액티비티 | `com.unity3d.player.UnityPlayerActivity` |
| APK 크기 | 47,140,650 bytes (45 MB) |
| 표시 이름 | `2D_URP` (ProjectSettings 기본값 그대로) |

- 기기 ABI가 `arm64-v8a`만이라 Mono(ARMv7 전용) 대체는 이 기기에서 실행 불가 — IL2CPP가 필수다.
- 가로 고정 이유: 클라이언트는 1600×900 프레임 하나를 안전 영역에 맞춰 축소(`FitView`)하는데, 프로젝트는 세로 회전을 허용하고 기기는 세로(ROTATION_0, 자동 회전 켜짐)로 놓여 있었다. 세로면 글자가 약 2/3 크기로 줄어든다. 프로젝트 방향 설정은 루트 결정 사항.
- 빌드/임포트가 디스크에 쓴 `ProjectSettings/ProjectSettings.asset`, `ShaderGraphSettings.asset`, `Assets/Settings/UniversalRP.asset`, `Assets/UniversalRenderPipelineGlobalSettings.asset` 변경(재직렬화·Android 블록 추가)은 이 브랜치에서 되돌렸다.

## 3. 실기기 (SM-S928N, Android 16, BP4A.251205.006.S928NKSS6DZH2, 1080×2340 450dpi)
- `adb -s R3CX10PCWCW install -r MemoryForge-dev.apk` 중 `adb: device offline` → `- waiting for device -`. 이후 `adb devices`도 `R3CX10PCWCW offline`.
- 따라서 **설치·기동 시간·첫 화면 스크린샷·1층 칸 탭·logcat은 아직 없음.** 앱이 기기에 설치됐는지도 미확인.
- 추정 원인: USB 재협상/USB 디버깅 재승인 대기(같은 시간 배터리 로그에 USB/AC 전환 기록이 반복됨). 기기 설정은 바꾸지 않았다.

## 남은 일 (재현 절차)
1. 기기에서 USB 디버깅 승인 상태 확인 → `adb devices`가 `device`인지 확인.
2. APK 재빌드(약 4분, Library 있으면 더 짧음) 또는 scratch의 APK 재사용:
   `MF_APK_OUT=<path> Unity -batchmode -quit -nographics -buildTarget Android -projectPath <worktree>/client/MemoryForgeUnity -executeMethod MemoryForgeAndroidBuild.BuildDevApk -logFile <log>`
3. `adb -s R3CX10PCWCW install -r <apk>` → `adb shell am start -W -n com.memoryforge.rpg.dev/com.unity3d.player.UnityPlayerActivity` → `adb exec-out screencap -p` → 밝은 1층 칸 탭 → 스크린샷 → `adb logcat -d | grep -E " E Unity|Exception"`.
