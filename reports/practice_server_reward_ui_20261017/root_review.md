# Opposite-provider reward review

Official Opus5.5 High authored the bounded reward extension; Codex independently reviewed every changed method and the public server/Core contract, then ran the actual Editor checks. The existing Ready/CanCommand, mutation, lifecycle and presentation paths are retained.

Published card/relic/part/evolution IDs are checked against the session's current REWARD snapshot before one existing command is staged. The UI never removes offers optimistically. Duplicate busy clicks and already-claimed/absent choices refuse before another request; proceed remains an explicit Core command. Content definitions supply names/text only, not price, probability or reward selection.

Root found a concrete reachability limit in the initial fixed-height view: approved content/characters.mjs permits growthCap4, Battle applies it, and Run.PendingEvolutions returns every qualifying deck entry. A fourth evolution row could push rewards below the viewport. Opus reused the original uGUI ScrollRect/RectMask2D construction with content height from the actual offer count. Root reviewed that small correction and verified the actual current viewport and pointer targets. A synthetic four-evolution state was not injected or reported as runtime acceptance.

Exact final source SHA is in `source_hashes.json`. Root first earned a real reward through8 actual public UI/CoreHTTP commands, then verified11 card-reward/duplicate/proceed assertions on that reward. Positive relic/part/evolution paths remain source-reviewed but not earned in this runtime case. No blocking source finding remains within the stated card/proceed acceptance scope.
