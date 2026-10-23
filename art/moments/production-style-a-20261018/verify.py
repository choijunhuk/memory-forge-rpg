"""Read-only source-art checks. Does not edit PNGs or game/Unity files."""
import copy
import hashlib
import json
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
RUN = Path(__file__).resolve().parent
CHARACTERS = ("sera", "ravien", "harin", "aila", "sion", "lumi")


def protected(document):
    result = copy.deepcopy(document)
    for field in ("images", "anchors"):
        result.pop(field, None)
    for scene in result["scenes"]:
        scene.pop("assets", None)
        for beat in scene["beats"]:
            beat.pop("figure", None)
    for motion in result["motions"].values():
        if "frames" in motion:
            motion["frames"] = ["<redrawn-image-frame>" for _ in motion["frames"]]
    return result


def main():
    errors, pending, images, summaries = [], [], {}, []
    accepted = set(json.loads((RUN / "accepted-assets.json").read_text()))
    for character in CHARACTERS:
        baseline = json.loads((RUN / "before" / f"{character}-moments.json").read_text())
        current = json.loads((ROOT / character / "moments.json").read_text())
        if protected(current) != protected(baseline):
            errors.append(f"{character}: protected narrative/playback fields changed")
        if current["status"] != "draft":
            errors.append(f"{character}: generated art is not independently approved")
        ready_keys = set()
        existing_keys = set()
        for key, relative in current["images"].items():
            path = ROOT.parent / relative
            if "_styleA_" not in relative:
                errors.append(f"{character}/{key}: historical image still referenced")
            if not path.is_file():
                pending.append({"character": character, "key": key, "path": relative})
                continue
            existing_keys.add(key)
            if relative.removeprefix("moments/") in accepted:
                ready_keys.add(key)
            else:
                pending.append({"character": character, "key": key, "path": relative, "reason": "awaiting visual acceptance"})
            if relative in images:
                continue
            with Image.open(path) as image:
                image.load()
                row = {"size": list(image.size), "mode": image.mode,
                       "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}
                if key.startswith("expr_") and image.size != (512, 512):
                    errors.append(f"{relative}: expression must be 512x512")
                if not key.startswith("bg"):
                    if image.mode != "RGBA":
                        errors.append(f"{relative}: missing RGBA")
                    else:
                        alpha = image.getchannel("A")
                        width, height = image.size
                        corners = [(0, 0), (width - 1, 0), (0, height - 1), (width - 1, height - 1)]
                        row["alpha_extrema"] = list(alpha.getextrema())
                        row["corner_alpha"] = [alpha.getpixel(point) for point in corners]
                        row["empty_corner_alpha"] = row["corner_alpha"][:2] if key.startswith("expr_") else row["corner_alpha"]
                        row["transparent_fraction"] = alpha.histogram()[0] / (width * height)
                        if any(row["empty_corner_alpha"]) or row["alpha_extrema"][0] != 0:
                            errors.append(f"{relative}: nonempty alpha in nominally empty corners")
                        if row["transparent_fraction"] < 0.10:
                            errors.append(f"{relative}: inspect insufficient transparent area")
                images[relative] = row
        for key, values in current["anchors"].items():
            if key not in current["images"]:
                errors.append(f"{character}: anchor key {key} missing from images")
            for name, point in values.items():
                if len(point) != 2 or not all(0 <= value <= 1 for value in point):
                    errors.append(f"{character}/{key}/{name}: invalid anchor")
        for name, motion in current["motions"].items():
            for frame in motion.get("frames", []):
                if frame not in current["images"].values():
                    errors.append(f"{character}/{name}: frame absent from image mappings")
                if "_styleA_" not in frame:
                    errors.append(f"{character}/{name}: historical frame still referenced")
        for scene in current["scenes"]:
            for asset in scene["assets"]:
                if asset["type"] == "image":
                    expected = "ready" if asset["key"] in ready_keys else "todo"
                    if asset["status"] != expected:
                        errors.append(f"{scene['id']}/{asset['key']}: expected {expected}")
            used = {beat["figure"] for beat in scene["beats"] if "figure" in beat}
            background = scene.get("stage", {}).get("background")
            if background:
                used.add(background)
            for key in used:
                if key not in current["images"]:
                    errors.append(f"{scene['id']}: unknown image {key}")
            listed = {asset["key"] for asset in scene["assets"] if asset["type"] == "image"}
            if used - listed:
                errors.append(f"{scene['id']}: used images absent from asset list {sorted(used-listed)}")
        summaries.append({"character": character, "scenes": len(current["scenes"]),
                          "beats": sum(len(scene["beats"]) for scene in current["scenes"]),
                          "image_mappings": len(current["images"]), "existing_mappings": len(existing_keys),
                          "ready_mappings": len(ready_keys)})
    changed_history = []
    for relative, expected in json.loads((RUN / "input-hashes.json").read_text()).items():
        path = ROOT / relative
        if path.suffix.lower() in (".png", ".apng") and (not path.is_file() or hashlib.sha256(path.read_bytes()).hexdigest() != expected):
            changed_history.append(relative)
    if changed_history:
        errors.append("Historical PNG/APNG changed")
    return {"scope": "source art only", "protected_narrative_unchanged": not any("protected" in e for e in errors),
            "pass": not errors and not pending, "errors": errors, "pending": pending,
            "historical_image_changes": changed_history, "characters": summaries,
            "unique_existing_pngs": len(images), "images": images,
            "not_run": ["Unity import/playback", "device acceptance", "Claude independent review", "game code tests/build"]}


if __name__ == "__main__":
    print(json.dumps(main(), ensure_ascii=False, indent=2))
