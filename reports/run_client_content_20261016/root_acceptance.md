# Original Run content in the actual Unity runtime

Original v0.8.1 §7.5–7.7/§8.9–8.14/§14/§16.2/20.9. The existing Battle asset intentionally had no memory costs/capacities/events. This unit adds a small same-source run-content.json sidecar and a strict ClientRunContent loader, preserving the existing battle-content.json bytes and all game values. No new account data or save persistence is created.

Official Opus xhigh authored the9file data bridge/test unit in isolated B. Codex independently read all new code/diffs and proved the23line stat loader body is byte-identical to the old one. Program.LoadStats remains the same-signature delegate; RunDataLoader reuses the shared helper. Export links reviewed loaders, no divergent copy/parser/dependency. Existing fullBattle/character/encounter/startingCommon outputs are independently compared with the host RunData before publishing both individually atomic files. They are not a cross-file transaction.

Root executed .NET export (0errors/0warnings), independently compared all26stats,9costs,capacity/2ascensionrules and all8event names/text/choices/effects against unchanged raw game_data.json. The sidecar is15300bytes, sourceSHA6a09e6dd…; the oldBattle bytes remain exactly8eb29ad5…. Every embedded expected original input in the Unity tests was separately compared with the raw source.

Actual Unity forced import/positive Load saw8events/base6/upgrade1/addS3/2ascensionrules/levelcap60. Play73/73 and Edit3/3 PASS. This includes full original definition checks, actual memory-cost/capacity calculations, fresh legacy/v2 Create hashes, fresh-data ownership and malformed/missing/duplicate/unknown/type/extra-content failure controls. Missing individual costs or stats never become default0/free values. This is real Editor runtime validation, not full explorationUI/save/account/server/device acceptance.

## Independent compatibility with the accepted Core

Root also built a private content-only tree from acceptedbf9cb42 Core, excluding the pending Run.Memory/formatter/completion-host/numeric-tests changes. With only this data bridge, its fulloldC#suite and Run20865/272504/567checksPASS. That tree's exporter builds0warnings/0errors and produces battle/run assets byte-identical to Root. Thus this unit can be reviewed/integrated independently while the separate memory serializer defect is repaired. The73Play receipt was taken in Root with then-pending memory code present; these content tests do not call new Run memory methods, and the compatibility build separately proves the acceptedCore interface. No claim of a separate73Play run on the private tree.

Source, original data/design/prices/probabilities/assets and manifests preserved; no image generated or dependency added. Scene/prefab writes stayed with Codex. FullRunUI, durable local/account save, server/auth/ledger, Android/IL2CPP/devices and finalart remain separate.
