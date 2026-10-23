# Recheck only the now-persisted scene-list change

Resume same official Opus5.5 High review, included subscription only. No API/credits/extra/fallback/Bash/MCP/agents/images. All source readonly; own ONLY reports/card_text_20261016/build_config_recheck.md. This addresses the exact missing-file evidence in your last report; do not re-review card text or tests.

You correctly found B had only the original two scene entries. Root's first Unity API EditorBuildSettings.scenes assignment had changed the live effective list but had not persisted the global disk list. Root verified the installed Unity API by reflection, then used EditorBuildSettings.globalScenes and AssetDatabase.SaveAssets outside Play Mode. Root now actually sees a three-line disk diff: Expedition enabled1/path/itsGUID prepended; Battle and Sample byte-for-byte original entries remain. This persisted file plus new Expedition scene/meta and original Battle scene/meta have NOW been copied to B.

Verify new entry first and enabled; GUID matches Expedition.meta; existing two entries/order/enabled unchanged; referenced three scene assets and metas exist. Return APPROVE or concrete remaining defect for this exact persisted config. Existing tests86Play/3Edit are Root results. New native build queued by Root; NOT_RUN by you. No requirement to wait for a commit before reviewing these exact bytes.
