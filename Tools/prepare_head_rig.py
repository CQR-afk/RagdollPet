"""Prepare Stand/Sit head-rig layers on the shared 512x512 sprite canvas."""

from __future__ import annotations

import json
from pathlib import Path

import cv2
import numpy as np
from PIL import Image, ImageFilter


ROOT = Path(__file__).resolve().parents[1]
POSES = {
    "Stand": {
        "original": ROOT / "Assets/Sprites/idle/idle_00.png",
        "headless": ROOT / "Assets/HeadRig/Source/stand_headless_source.png",
        "head": (400, 220, 115, 135),
        "clear": (400, 210, 78, 96),
        "patch": (401, 286, 86, 72),
        "neck": (403, 294, 68, 24),
        "chest": (406, 350, 76, 62),
        "pivot": (401, 278),
        "upper": (378, 330, 92, 112),
        "upper_clear": (378, 334, 72, 91),
        "upper_pivot": (370, 408),
    },
    "Sit": {
        "original": ROOT / "Assets/SpritesDense/curious_groom/curious_groom_008.png",
        "headless": ROOT / "Assets/HeadRig/Source/sit_headless_source.png",
        "head": (371, 238, 110, 135),
        "clear": (371, 226, 73, 93),
        "patch": (373, 300, 80, 70),
        "neck": (374, 307, 65, 23),
        "chest": (380, 366, 70, 64),
        "pivot": (372, 286),
        "upper": (350, 338, 94, 118),
        "upper_clear": (350, 342, 73, 96),
        "upper_pivot": (345, 420),
    },
}


def alpha_bbox(rgba: np.ndarray) -> tuple[int, int, int, int]:
    ys, xs = np.where(rgba[:, :, 3] > 20)
    return int(xs.min()), int(ys.min()), int(xs.max() + 1), int(ys.max() + 1)


def align_by_silhouette(source: np.ndarray, target: np.ndarray) -> np.ndarray:
    source = source.copy()
    rgb = source[:, :, :3]
    near_black = np.max(rgb, axis=2) < 18
    fringe_red = (rgb[:, :, 0] > 170) & (rgb[:, :, 1] < 105) & (rgb[:, :, 2] < 105)
    fringe_yellow = (rgb[:, :, 0] > 170) & (rgb[:, :, 1] > 110) & (rgb[:, :, 2] < 80)
    source[near_black | fringe_red | fringe_yellow, 3] = 0
    source[source[:, :, 3] == 0, :3] = 0
    sx0, sy0, sx1, sy1 = alpha_bbox(source)
    tx0, ty0, tx1, ty1 = alpha_bbox(target)
    crop = source[sy0:sy1, sx0:sx1]
    resized = cv2.resize(crop, (tx1 - tx0, ty1 - ty0), interpolation=cv2.INTER_LANCZOS4)
    aligned = np.zeros_like(target)
    aligned[ty0:ty1, tx0:tx1] = resized
    return aligned


def ellipse_mask(size: tuple[int, int], spec: tuple[int, int, int, int], blur: float) -> np.ndarray:
    cx, cy, rx, ry = spec
    yy, xx = np.ogrid[: size[1], : size[0]]
    raw = (((xx - cx) / rx) ** 2 + ((yy - cy) / ry) ** 2 <= 1).astype(np.uint8) * 255
    return np.asarray(Image.fromarray(raw, "L").filter(ImageFilter.GaussianBlur(blur))) / 255.0


def masked(source: np.ndarray, mask: np.ndarray) -> np.ndarray:
    out = source.copy()
    out[:, :, 3] = np.clip(out[:, :, 3].astype(np.float32) * mask, 0, 255).astype(np.uint8)
    out[out[:, :, 3] == 0, :3] = 0
    return out


def replace_region(original: np.ndarray, replacement: np.ndarray, mask: np.ndarray) -> np.ndarray:
    m = mask[:, :, None]
    out = original.astype(np.float32) * (1 - m) + replacement.astype(np.float32) * m
    out = np.clip(out, 0, 255).astype(np.uint8)
    out[out[:, :, 3] == 0, :3] = 0
    return out


def main() -> None:
    manifest = {"canvas": [512, 512], "poses": {}}
    for name, cfg in POSES.items():
        original = np.asarray(Image.open(cfg["original"]).convert("RGBA"))
        generated = np.asarray(Image.open(cfg["headless"]).convert("RGBA"))
        headless = align_by_silhouette(generated, original)
        size = (original.shape[1], original.shape[0])

        head_mask = ellipse_mask(size, cfg["head"], 4.0)
        clear_mask = ellipse_mask(size, cfg["clear"], 3.0)
        patch_mask = ellipse_mask(size, cfg["patch"], 7.0)
        neck_mask = ellipse_mask(size, cfg["neck"], 4.5)
        chest_mask = ellipse_mask(size, cfg["chest"], 7.0)
        upper_mask = ellipse_mask(size, cfg["upper"], 7.0)
        upper_clear_mask = ellipse_mask(size, cfg["upper_clear"], 4.0)

        out_dir = ROOT / "Assets/HeadRig" / name
        out_dir.mkdir(parents=True, exist_ok=True)
        cleared = original.copy()
        cleared[:, :, 3] = np.clip(cleared[:, :, 3].astype(np.float32) * (1 - clear_mask), 0, 255).astype(np.uint8)
        cleared[cleared[:, :, 3] == 0, :3] = 0
        neck_fill = masked(headless, patch_mask)
        body_with_neck = Image.alpha_composite(Image.fromarray(cleared), Image.fromarray(neck_fill))
        body_with_neck_rgba = np.asarray(body_with_neck)
        upper = Image.fromarray(masked(body_with_neck_rgba, upper_mask))
        lower_body = body_with_neck_rgba.copy()
        lower_body[:, :, 3] = np.clip(lower_body[:, :, 3].astype(np.float32) * (1 - upper_clear_mask), 0, 255).astype(np.uint8)
        lower_body[lower_body[:, :, 3] == 0, :3] = 0
        body = Image.fromarray(lower_body)
        head = Image.fromarray(masked(original, head_mask))
        neck = Image.fromarray(masked(original, neck_mask))
        chest = Image.fromarray(masked(original, chest_mask))
        body.save(out_dir / "body_base.png")
        upper.save(out_dir / "upper_body.png")
        head.save(out_dir / "head.png")
        neck.save(out_dir / "neck_fur.png")
        chest.save(out_dir / "chest_fur.png")
        preview = Image.alpha_composite(body, upper)
        preview = Image.alpha_composite(preview, head)
        preview = Image.alpha_composite(preview, neck)
        preview = Image.alpha_composite(preview, chest)
        preview.save(ROOT / "Assets/HeadRig/Source" / f"{name.lower()}_assembled_preview.png")

        # Stress preview at the configured runtime limit.  This deliberately
        # exposes the edge that would gap if Head_Base had no hidden overlap.
        head_rgba = np.asarray(head)
        matrix = cv2.getRotationMatrix2D(cfg["pivot"], -7.0, 1.0)
        matrix[0, 2] += 10.0
        matrix[1, 2] -= 5.0
        moved_head = cv2.warpAffine(head_rgba, matrix, (512, 512), flags=cv2.INTER_LANCZOS4,
                                    borderMode=cv2.BORDER_CONSTANT, borderValue=(0, 0, 0, 0))
        upper_assembly = Image.alpha_composite(upper, Image.fromarray(moved_head))
        upper_assembly = Image.alpha_composite(upper_assembly, neck)
        upper_assembly = Image.alpha_composite(upper_assembly, chest)
        upper_matrix = cv2.getRotationMatrix2D(cfg["upper_pivot"], -1.0, 1.0)
        upper_matrix[0, 2] += 2.0
        upper_matrix[1, 2] -= .8
        moved_upper = cv2.warpAffine(np.asarray(upper_assembly), upper_matrix, (512, 512),
                                     flags=cv2.INTER_LANCZOS4, borderMode=cv2.BORDER_CONSTANT,
                                     borderValue=(0, 0, 0, 0))
        moved = Image.alpha_composite(body, Image.fromarray(moved_upper))
        moved.save(ROOT / "Assets/HeadRig/Source" / f"{name.lower()}_movement_preview.png")

        manifest["poses"][name] = {
            "pivot": list(cfg["pivot"]),
            "headMask": list(cfg["head"]),
            "bodyClearMask": list(cfg["clear"]),
            "neckMask": list(cfg["neck"]),
            "chestMask": list(cfg["chest"]),
            "upperBodyMask": list(cfg["upper"]),
            "upperBodyClearMask": list(cfg["upper_clear"]),
            "upperBodyPivot": list(cfg["upper_pivot"]),
        }

    path = ROOT / "Assets/HeadRig/head_rig.json"
    path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    print(path)


if __name__ == "__main__":
    main()
