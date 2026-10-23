# Android APK data integrity — 2026-10-21

The original `3bb8129` development APK stopped at the Unity logo with `InvalidCastException: Material -> Texture` in URP's `SetupPerCameraShaderConstants`, where `ReflectionProbe.defaultTexture` is read. Fresh `83a654a` loaded the lobby with zero such exceptions in the parent agent's device test. No gameplay rollback was needed.

## Artifact diagnosis

The original APK's active ZIP entries contained both `assets/bin/Data/sharedassets0.assets` (524828 bytes) and `sharedassets0.assets.split0` through `.split3`. Known-good `0b408e0` and fresh `83a654a` contained only the whole file. This was conflicting packaged data, not merely unused ZIP padding.

Unity version-22 metadata and object bytes explain the precise type mismatch:

| Serialized location | Value |
| --- | --- |
| `level0`, RenderSettings object path ID 7, body offset 292 | Pointer: external file 3, path ID 2 |
| `level0`, external file 3 | `sharedassets0.assets` |
| Whole `sharedassets0.assets`, path ID 2 | Class 89, Cubemap `Default-Skybox-Cubemap` |
| Split representation, path ID 2 | Class 21, Material `Font Material` |
| Split representation, path ID 4 | Class 89, Cubemap `Default-Skybox-Cubemap` |

Selecting the stale split representation resolves the scene's reflection pointer to exactly the wrong type reported by URP. The bad original and both good APKs had identical `level0` and whole `sharedassets0.assets` bytes:

- `level0` SHA256: `22f1419c329cbcf6b7b6e983eb932b6902d1d98530cfccf711817709649edf0f`.
- Whole `sharedassets0.assets` SHA256: `a26483eca0c5ccd15ae26b16d03dc913658f07e2504f1b41eef83efccb2ab4ab`.

## Build boundary

`MemoryForgeAndroidBuild.BuildDevApk` retains its existing deletion of generated Gradle APK outputs. After Unity reports build success, `ValidateApk` reads the final ZIP and rejects:

- Any Unity Data file present as both a whole file and `.splitN` chunks.
- Any split sequence missing `.split0` or an intermediate chunk.

Validation throws before the `MF_ANDROID_BUILD result=Succeeded` message. A failed integrity check therefore fails the batch invocation instead of reporting the stale artifact as successful. No wider cache deletion, dependency, pipeline workaround, or gameplay change was added.

The existing Editor test asmdef cannot directly reference the predefined `Assembly-CSharp-Editor` assembly. `AndroidApkIntegrityEditorTests` reaches the actual validator by reflection and creates temporary ZIP fixtures, preserving assembly structure.

## Verification and limits

- **PASS:** seven NUnit cases cover whole-only, unordered split-only including multi-digit indices, different files using different representations, non-Unity paths, the original conflicting entry names, missing zero, and an intermediate gap.
- **PASS:** the actual C# validator rejects the original bad APK with `InvalidDataException` and accepts known-good `0b408e0` and fresh `83a654a` APKs.
- These checks used a temporary .NET 10 harness compiling the actual builder and Editor test file against installed Unity managed DLLs. The initial tests failed because the validation boundary was absent; all seven passed after implementation.
- **NOT_RUN by this subagent:** Unity Editor test runner, a new Android build containing the guard, device alternation, and full repository suites. Parent owns those checks; no Unity/device process was launched here.

The guard verifies ZIP entry representations and chunk continuity. It does not validate asset payloads or infer an expected final chunk when the archive contains only a shorter contiguous sequence.

Parent verification: actual Unity EditMode10/10 passes; guarded combined development APK builds successfully and installs/starts on GalaxyS24Ultra. The parent owns the final device navigation receipt under reports/home_hub_20261021.
