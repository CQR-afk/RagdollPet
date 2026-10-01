"""Build photo-derived face overlays for the WPF 2.5D runtime.

The open-eye and ear layers are copied from the shipped sprites.  Only the
closed eyelids come from the ImageGen occlusion-completion sources.  Outputs
stay on a 512x512 coordinate system so WPF can share one set of pivots across
all display sizes.
"""

from __future__ import annotations

import json
from pathlib import Path

import cv2
import numpy as np
from PIL import Image, ImageFilter


ROOT = Path(__file__).resolve().parents[1]


POSES = {
    "Stand": {
        "open": ROOT / "Assets/Sprites/idle/idle_00.png",
        "closed": ROOT / "Assets/FaceLayers/Source/stand_closed_source.png",
        "eyes": [(382, 201), (421, 203)],
        "ears": [(330, 130, 383, 193, 351, 181), (410, 126, 461, 190, 438, 178)],
    },
    "Sit": {
        "open": ROOT / "Assets/SpritesDense/curious_groom/curious_groom_008.png",
        "closed": ROOT / "Assets/FaceLayers/Source/sit_closed_source.png",
        "eyes": [(351, 229), (389, 234)],
        "ears": [(326, 166, 384, 222, 354, 210), (389, 153, 443, 211, 414, 199)],
    },
}


def alpha_bbox(rgba: np.ndarray) -> tuple[int, int, int, int]:
    ys, xs = np.where(rgba[:, :, 3] > 20)
    return int(xs.min()), int(ys.min()), int(xs.max() + 1), int(ys.max() + 1)


def align_by_silhouette(source: np.ndarray, target: np.ndarray) -> np.ndarray:
    sx0, sy0, sx1, sy1 = alpha_bbox(source)
    tx0, ty0, tx1, ty1 = alpha_bbox(target)
    crop = source[sy0:sy1, sx0:sx1]
    resized = cv2.resize(crop, (tx1 - tx0, ty1 - ty0), interpolation=cv2.INTER_LANCZOS4)
    aligned = np.zeros_like(target)
    aligned[ty0:ty1, tx0:tx1] = resized
    return aligned


def feathered_ellipse(size: tuple[int, int], center: tuple[int, int], radii: tuple[int, int], blur: float) -> np.ndarray:
    mask = Image.new("L", size, 0)
    yy, xx = np.ogrid[: size[1], : size[0]]
    cx, cy = center
    rx, ry = radii
    arr = (((xx - cx) / rx) ** 2 + ((yy - cy) / ry) ** 2 <= 1).astype(np.uint8) * 255
    mask = Image.fromarray(arr, "L").filter(ImageFilter.GaussianBlur(blur))
    return np.asarray(mask)


def masked_overlay(source: np.ndarray, masks: list[np.ndarray]) -> np.ndarray:
    out = source.copy()
    combined = np.maximum.reduce(masks)
    out[:, :, 3] = np.minimum(out[:, :, 3], combined)
    out[out[:, :, 3] == 0, :3] = 0
    return out


def make_eye_layers(open_rgba: np.ndarray, closed_rgba: np.ndarray, eyes: list[tuple[int, int]], out_dir: Path) -> None:
    size = (open_rgba.shape[1], open_rgba.shape[0])
    eye_masks = [feathered_ellipse(size, p, (17, 12), 2.2) for p in eyes]
    closed = masked_overlay(closed_rgba, eye_masks)
    Image.fromarray(closed).save(out_dir / "blink_closed.png")

    base = np.zeros_like(open_rgba)
    pupils = []
    for index, (cx, cy) in enumerate(eyes):
        eye_mask = feathered_ellipse(size, (cx, cy), (10, 8), 1.2)
        pupil_mask = feathered_ellipse(size, (cx, cy), (4, 6), 0.8)

        # Cover the original pupil with nearby iris colour.  Telea inpainting
        # keeps the photographic iris texture and avoids a painted-on disc.
        bgr = cv2.cvtColor(open_rgba[:, :, :3], cv2.COLOR_RGB2BGR)
        inpaint_mask = (pupil_mask > 70).astype(np.uint8) * 255
        iris_rgb = cv2.cvtColor(cv2.inpaint(bgr, inpaint_mask, 3, cv2.INPAINT_TELEA), cv2.COLOR_BGR2RGB)
        eye_base = np.dstack([iris_rgb, np.minimum(open_rgba[:, :, 3], eye_mask)])
        alpha = eye_base[:, :, 3] / 255.0
        for channel in range(4):
            base[:, :, channel] = np.where(alpha > 0, eye_base[:, :, channel], base[:, :, channel])

        pupil = open_rgba.copy()
        pupil[:, :, 3] = np.minimum(open_rgba[:, :, 3], pupil_mask)
        pupil[pupil[:, :, 3] == 0, :3] = 0
        pupils.append(pupil)
        Image.fromarray(pupil).save(out_dir / f"pupil_{'L' if index == 0 else 'R'}.png")

    Image.fromarray(base).save(out_dir / "eye_base.png")


def make_ears(open_rgba: np.ndarray, ears: list[tuple[int, int, int, int, int, int]], out_dir: Path) -> list[dict]:
    metadata = []
    for index, (x0, y0, x1, y1, px, py) in enumerate(ears):
        crop = open_rgba[y0:y1, x0:x1].copy()
        # Soft oval limits the duplicate fur patch to the ear and its root.
        h, w = crop.shape[:2]
        local_mask = feathered_ellipse((w, h), (w // 2, h // 2), (max(1, w // 2 - 2), max(1, h // 2 - 2)), 2.0)
        crop[:, :, 3] = np.minimum(crop[:, :, 3], local_mask)
        name = "ear_L.png" if index == 0 else "ear_R.png"
        Image.fromarray(crop).save(out_dir / name)
        metadata.append({"file": name, "x": x0, "y": y0, "pivotX": px - x0, "pivotY": py - y0, "width": w, "height": h})
    return metadata


def main() -> None:
    manifest = {"canvas": [512, 512], "poses": {}}
    for pose_name, config in POSES.items():
        open_rgba = np.asarray(Image.open(config["open"]).convert("RGBA"))
        generated = np.asarray(Image.open(config["closed"]).convert("RGBA"))
        closed_rgba = align_by_silhouette(generated, open_rgba)
        out_dir = ROOT / "Assets/FaceLayers" / pose_name
        out_dir.mkdir(parents=True, exist_ok=True)
        make_eye_layers(open_rgba, closed_rgba, config["eyes"], out_dir)
        ears = make_ears(open_rgba, config["ears"], out_dir)
        manifest["poses"][pose_name] = {"eyes": config["eyes"], "ears": ears}

    manifest_path = ROOT / "Assets/FaceLayers/face_layers.json"
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    print(manifest_path)


if __name__ == "__main__":
    main()
