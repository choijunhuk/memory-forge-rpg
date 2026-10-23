"""Rewrite source-art references from the frozen baseline and accepted PNG list."""
import copy
import json
from pathlib import Path

RUN = Path(__file__).resolve().parent
ROOT = RUN.parent
changes = json.loads((RUN / "bond-mapping-plan.json").read_text())
overrides = json.loads((RUN / "path-overrides.json").read_text()) if (RUN / "path-overrides.json").is_file() else {}
anchor_updates = json.loads((RUN / "anchor-updates.json").read_text()) if (RUN / "anchor-updates.json").is_file() else {}
accepted = {entry["path"] for entry in json.loads((RUN / "records/wave1.json").read_text())}
for job in json.loads((RUN / "lobby-jobs.json").read_text()):
    accepted.add("ravien/images/char_ravien_fig_stamp_styleA_v02.png" if job["character"] == "ravien" and job["key"] == "fig_stamp" else job["target"])
if (RUN / "additional-accepted.json").is_file():
    accepted.update(json.loads((RUN / "additional-accepted.json").read_text()))

rows = []
for character in ("sera", "ravien", "harin", "aila", "sion", "lumi"):
    document = json.loads((RUN / "before" / f"{character}-moments.json").read_text())
    replacements = {}
    for key, old in list(document["images"].items()):
        version = "v02" if character == "ravien" and key == "fig_stamp" else "v01"
        target = f"moments/{character}/images/char_{character}_{key}_styleA_{version}.png"
        if f"{character}/{key}" in overrides:
            target = "moments/" + overrides[f"{character}/{key}"]
        document["images"][key] = replacements.setdefault(old, target)
    for motion in document["motions"].values():
        if "frames" in motion:
            motion["frames"] = [replacements[frame] for frame in motion["frames"]]
    for key, change in changes.get(character, {}).items():
        target = overrides.get(f"{character}/{key}", f"{character}/images/char_{character}_{key}_styleA_v01.png")
        document["images"][key] = "moments/" + target
        document["anchors"][key] = copy.deepcopy(document["anchors"][change["source_key"]])
        for beat in change["beats"]:
            document["scenes"][1]["beats"][beat - 1]["figure"] = key
    for key, anchors in anchor_updates.get(character, {}).items():
        document["anchors"][key] = anchors
    for scene in document["scenes"]:
        listed = {asset["key"] for asset in scene["assets"] if asset["type"] == "image"}
        used = {beat["figure"] for beat in scene["beats"] if "figure" in beat}
        if scene.get("stage", {}).get("background"):
            used.add(scene["stage"]["background"])
        for key in sorted(used - listed):
            scene["assets"].append({"key": key, "type": "image", "status": "todo"})
        for asset in scene["assets"]:
            if asset["type"] == "image":
                path = document["images"][asset["key"]].removeprefix("moments/")
                asset["status"] = "ready" if path in accepted and (ROOT / path).is_file() else "todo"
    (ROOT / character / "moments.json").write_text(json.dumps(document, ensure_ascii=False, indent=2) + "\n")
    for key, path in document["images"].items():
        rows.append({"character": character, "key": key, "path": path, "status": "ready" if path.removeprefix("moments/") in accepted else "todo"})
(RUN / "accepted-assets.json").write_text(json.dumps(sorted(accepted), indent=2) + "\n")
(RUN / "planned-assets.json").write_text(json.dumps(rows, ensure_ascii=False, indent=2) + "\n")
print(f"6 JSON remapped; {len({row['path'] for row in rows})} unique new paths; {len(accepted)} accepted")
