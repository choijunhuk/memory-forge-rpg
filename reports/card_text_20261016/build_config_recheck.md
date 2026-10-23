# P1-EXPEDITION-BUILD-CONFIG-RECHECK — narrow read-only recheck

Reviewer: Claude Code Opus 5.5 (High), read-only source inspection in worktree B. No Bash, MCP, agents or images were used.
Execution: **NOT_RUN** by this reviewer. The 86 Play / 3 Edit results and the queued native build are Root's.

## 1. Persisted scene list — APPROVE

`client/MemoryForgeUnity/ProjectSettings/EditorBuildSettings.asset`:

| # | enabled | path | guid | `.meta` guid in B |
|---|---|---|---|---|
| 0 | 1 | Assets/Scenes/MemoryForgeExpedition.unity | ef192f5c231004a7e908bfc69d6f8cca | match (`MemoryForgeExpedition.unity.meta:2`) |
| 1 | 1 | Assets/Scenes/MemoryForgeBattle.unity | e174e7014430b4b28a6d66f1c7ee77d5 | match (`MemoryForgeBattle.unity.meta:2`) |
| 2 | 1 | Assets/Scenes/SampleScene.unity | 8c9cfa26abfee488c85f1582747f6a02 | match (`SampleScene.unity.meta:2`) |

- **New entry:** the Expedition scene is first and enabled, and its GUID matches the scene's `.meta`.
- **Existing entries:** Battle and Sample keep the same path, GUID and enabled value as in my previous report, in the same order.
- **Rest of the file:** `m_configObjects` and `m_UseUCBPForAssetBundles` are unchanged.
- **Assets present:** all three referenced scenes and their `.meta` files exist in B.

## 2. Remaining defect — the script referenced by the first build scene has no `.meta` in B

- **What's missing:**
  - `MemoryForgeExpedition.unity:231` attaches its only MonoBehaviour by `m_Script: {fileID: 11500000, guid: 2a83fee16df664238ba42f0f064aab5a}`, with serialized `Seed: 1234` and `Length: short`.
  - No file in B carries that GUID. `Assets/Scripts/MemoryForgeExpeditionClient.cs` exists, but `MemoryForgeExpeditionClient.cs.meta`, `ClientRunContent.cs.meta`, `ClientBattleContent.cs.meta` and the folder's `Assets/Scripts.meta` do not.
- **When it breaks:** a clean checkout or fresh import of these bytes, including a fresh native build machine.
  - Unity assigns a new random GUID to `MemoryForgeExpeditionClient.cs`, so the scene component turns into *Missing (Mono Script)*.
  - The first build scene would then open as an empty camera, not the implemented expedition, and nothing errors at build time.
  - Root's own Editor already holds the generated `.meta`, which explains why Root's tests and live list work there.
- **Smallest repair:** copy/commit Root's existing Unity-generated `Assets/Scripts.meta` and `Assets/Scripts/*.cs.meta`, without regenerating them, so `MemoryForgeExpeditionClient.cs.meta` carries guid `2a83fee16df664238ba42f0f064aab5a`.
  - The new PlayMode test `.cs` files (`ExpeditionClient`, `RunClientContent`, `RunJsonNumber`, `RunMemoryFlow`, `RunProgression`) also lack `.meta` in B. They are harmless for scene binding, but should travel with them so GUIDs stay stable.
- **Same gap in the Battle scene:** `MemoryForgeBattle.unity:148` references script guid `4bbb54eb1681a4f589c29d3b3840dfb3`. Neither that script's `.cs` nor its `.meta` exists anywhere in B. If the Battle scene stays in the build list, its MonoBehaviour source and `.meta` must be copied too. Otherwise the second build scene also has a missing script. This doesn't affect the first scene's launch.

## Verdict
- **The persisted EditorBuildSettings bytes: APPROVE.** Order, enabled values, GUIDs and the preserved entries are all correct.
- **"A fresh native build opens the implemented expedition" from B's copied files: not yet satisfied.** The Expedition client's script `.meta` (guid `2a83fee…`) must be included, and for the Battle scene, its script source and `.meta` (guid `4bbb54eb…`). Re-check once these files are in B.
