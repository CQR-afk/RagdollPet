"""Extract runtime layers from the two approved Stand DirectionPose images.

No character pixels are sourced from any other pose. Hidden overlap is built
deterministically from neighbouring pixels in the same approved image so the
assembled neutral frame remains identity-exact and does not depend on another
ImageGen pass.
"""

from __future__ import annotations

import hashlib
import json
from pathlib import Path

import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFilter


ROOT = Path(__file__).resolve().parents[1]
CANVAS = (512, 512)

POSES = {
    "Left": {
        "source": ROOT / "Assets/DirectionPoses/Candidates/v1.14/Stand_Left_03.png",
        "eyes": [(328, 220), (365, 231)],
        "ears": [
            {"box": (306, 142, 357, 207), "pivot": (337, 198),
             "polygon": [(306, 190), (323, 144), (357, 195), (349, 207), (319, 203)]},
            {"box": (382, 168, 432, 219), "pivot": (403, 207),
             "polygon": [(382, 207), (414, 169), (432, 207), (421, 219), (393, 216)]},
        ],
        "head": (366, 249, 105, 137),
        "head_core": (365, 251, 84, 108),
        "head_clear": (366, 246, 72, 92),
        "neck_patch": (361, 302, 111, 98),
        "neck": (365, 317, 70, 27),
        "chest": (369, 365, 82, 72),
        "upper": (353, 346, 105, 120),
        "upper_clear": (354, 350, 80, 96),
        "head_pivot": (361, 292),
        "upper_pivot": (344, 419),
    },
    "Left3Q": {
        "source": ROOT / "Assets/DirectionPoses/Candidates/Stand_Left3Q_02.png",
        "eyes": [(348, 208), (392, 211)],
        "ears": [
            {"box": (313, 136, 366, 199), "pivot": (344, 188),
             "polygon": [(313, 194), (335, 138), (366, 188), (357, 199), (326, 195)]},
            {"box": (405, 136, 451, 200), "pivot": (432, 188),
             "polygon": [(405, 191), (438, 138), (451, 191), (440, 200), (416, 197)]},
        ],
        "head": (380, 227, 112, 139),
        "head_core": (381, 236, 91, 111),
        "head_clear": (380, 216, 76, 96),
        "neck_patch": (365, 287, 115, 100),
        "neck": (383, 303, 72, 27),
        "chest": (388, 356, 84, 70),
        "upper": (361, 337, 106, 121),
        "upper_clear": (360, 342, 81, 98),
        "head_pivot": (379, 281),
        "upper_pivot": (351, 416),
    },
    "Right3Q": {
        "source": ROOT / "Assets/DirectionPoses/Candidates/Stand_Right3Q_03.png",
        "eyes": [(395, 209), (434, 210)],
        "ears": [
            {"box": (345, 136, 398, 200), "pivot": (372, 188),
             "polygon": [(345, 194), (363, 138), (398, 190), (392, 200), (359, 199)]},
            {"box": (417, 136, 467, 198), "pivot": (447, 188),
             "polygon": [(417, 191), (451, 138), (467, 190), (461, 198), (435, 196)]},
        ],
        "head": (411, 227, 111, 138),
        "head_core": (412, 236, 90, 110),
        "head_clear": (410, 216, 75, 95),
        "neck_patch": (390, 287, 115, 100),
        "neck": (410, 303, 70, 27),
        "chest": (408, 356, 82, 70),
        "upper": (386, 337, 105, 121),
        "upper_clear": (387, 342, 80, 98),
        "head_pivot": (409, 281),
        "upper_pivot": (379, 416),
    },
    "Right": {
        "source": ROOT / "Assets/DirectionPoses/Candidates/v1.14/Stand_Right_03.png",
        "eyes": [(421, 213), (452, 211)],
        "ears": [
            {"box": (355, 145, 411, 211), "pivot": (387, 199),
             "polygon": [(355, 199), (382, 146), (411, 197), (404, 211), (370, 207)]},
            {"box": (430, 141, 478, 203), "pivot": (453, 192),
             "polygon": [(430, 195), (469, 143), (478, 193), (466, 203), (441, 201)]},
        ],
        "head": (426, 246, 98, 135),
        "head_core": (427, 247, 78, 106),
        "head_clear": (427, 239, 68, 91),
        "neck_patch": (407, 300, 109, 99),
        "neck": (426, 316, 66, 26),
        "chest": (423, 365, 77, 72),
        "upper": (403, 346, 101, 120),
        "upper_clear": (405, 350, 77, 96),
        "head_pivot": (423, 291),
        "upper_pivot": (394, 419),
    },
}


def ellipse_mask(spec: tuple[int, int, int, int], blur: float = 0.0) -> np.ndarray:
    cx, cy, rx, ry = spec
    yy, xx = np.ogrid[: CANVAS[1], : CANVAS[0]]
    array = ((((xx - cx) / rx) ** 2 + ((yy - cy) / ry) ** 2) <= 1).astype(np.uint8) * 255
    image = Image.fromarray(array, "L")
    if blur:
        image = image.filter(ImageFilter.GaussianBlur(blur))
    return np.asarray(image, dtype=np.uint8)


def rounded_box_mask(box: tuple[int, int, int, int], blur: float = 2.0) -> np.ndarray:
    x0, y0, x1, y1 = box
    mask = Image.new("L", CANVAS, 0)
    ImageDraw.Draw(mask).ellipse((x0, y0, x1, y1), fill=255)
    if blur:
        mask = mask.filter(ImageFilter.GaussianBlur(blur))
    return np.asarray(mask, dtype=np.uint8)


def polygon_mask(points: list[tuple[int, int]], blur: float = 1.8) -> np.ndarray:
    mask = Image.new("L", CANVAS, 0)
    ImageDraw.Draw(mask).polygon(points, fill=255)
    if blur:
        mask = mask.filter(ImageFilter.GaussianBlur(blur))
    return np.asarray(mask, dtype=np.uint8)


def rgba_with_mask(source: np.ndarray, mask: np.ndarray) -> np.ndarray:
    result = source.copy()
    result[:, :, 3] = np.minimum(result[:, :, 3], mask)
    result[result[:, :, 3] == 0, :3] = 0
    return result


def alpha_over(bottom: np.ndarray, top: np.ndarray) -> np.ndarray:
    return np.asarray(Image.alpha_composite(Image.fromarray(bottom), Image.fromarray(top)))


def same_pose_inpaint(source: np.ndarray, mask: np.ndarray) -> np.ndarray:
    """Fill a hidden area using only neighbouring pixels from this pose."""
    bgr = cv2.cvtColor(source[:, :, :3], cv2.COLOR_RGB2BGR)
    binary = (mask > 24).astype(np.uint8) * 255
    filled = cv2.inpaint(bgr, binary, 7, cv2.INPAINT_TELEA)
    return cv2.cvtColor(filled, cv2.COLOR_BGR2RGB)


def make_hidden_neck(source: np.ndarray, config: dict, clear: np.ndarray) -> np.ndarray:
    patch = ellipse_mask(config["neck_patch"], 7.0)
    expanded_clear = cv2.dilate(clear, np.ones((11, 11), np.uint8), iterations=1)
    patch = np.minimum(patch, expanded_clear)
    # Hidden overlap may extend a few pixels beyond the photographed contour,
    # but never across a large transparent area.  Without this guard, cloned
    # transparent-black pixels become an opaque oval beside side-facing heads.
    source_silhouette = (source[:, :, 3] > 24).astype(np.uint8) * 255
    patch = np.minimum(patch, source_silhouette)
    rgb = source[:, :, :3].copy()
    # Build the hidden overlap by cloning lower shoulder/chest pixels from the
    # same approved pose. It is intentionally only visible during head motion.
    center_x = config["upper"][0]
    ys, xs = np.where(patch > 8)
    for y, x in zip(ys, xs):
        sample_x = int(np.clip(round(center_x + (x - center_x) * 0.80), 0, 511))
        sample_y = int(np.clip(y + 78, 0, 511))
        rgb[y, x] = source[sample_y, sample_x, :3]
    hidden = np.zeros_like(source)
    hidden[:, :, :3] = rgb
    hidden[:, :, 3] = patch
    hidden[hidden[:, :, 3] == 0, :3] = 0
    return hidden


def make_body_layers(source: np.ndarray, config: dict) -> tuple[np.ndarray, np.ndarray]:
    clear = ellipse_mask(config["head_clear"], 3.0)
    # The original whole-pose sprite also contains the ear silhouettes. They
    # must not remain on the body plate when the complete HeadGroup moves.
    for ear in config["ears"]:
        ear_clear = rounded_box_mask(ear["box"], 2.0)
        ear_clear = cv2.dilate(ear_clear, np.ones((7, 7), np.uint8), iterations=1)
        clear = np.maximum(clear, ear_clear)
    cleared = source.copy()
    cleared[:, :, 3] = np.clip(
        cleared[:, :, 3].astype(np.float32) * (1.0 - clear.astype(np.float32) / 255.0), 0, 255
    ).astype(np.uint8)
    cleared[cleared[:, :, 3] == 0, :3] = 0

    body_with_neck = alpha_over(cleared, make_hidden_neck(source, config, clear))
    upper_mask = ellipse_mask(config["upper"], 7.0)
    upper = rgba_with_mask(body_with_neck, upper_mask)

    upper_clear = ellipse_mask(config["upper_clear"], 4.0)
    body = body_with_neck.copy()
    body[:, :, 3] = np.clip(
        body[:, :, 3].astype(np.float32) * (1.0 - upper_clear.astype(np.float32) / 255.0), 0, 255
    ).astype(np.uint8)
    body[body[:, :, 3] == 0, :3] = 0
    return body, upper


def make_ear_layers(source: np.ndarray, config: dict) -> tuple[list[np.ndarray], np.ndarray]:
    ears: list[np.ndarray] = []
    combined = np.zeros(CANVAS[::-1], dtype=np.uint8)
    for ear in config["ears"]:
        mask = polygon_mask(ear["polygon"], 1.8)
        combined = np.maximum(combined, mask)
        ears.append(rgba_with_mask(source, mask))
    return ears, combined


def make_head_base(source: np.ndarray, config: dict, ear_mask: np.ndarray) -> np.ndarray:
    head_mask = ellipse_mask(config["head"], 4.0)
    core_mask = ellipse_mask(config["head_core"], 3.0)
    ear_remove = np.minimum(ear_mask, head_mask)
    # Pull hidden crown texture from lower/inner pixels of this exact head.
    # Generic inpainting sees transparent black around the outer ear and can
    # produce a dark smear, so use a deterministic same-pose clone instead.
    rgb = source[:, :, :3].copy()
    center_x = config["head_core"][0]
    ys, xs = np.where(ear_remove > 12)
    for y, x in zip(ys, xs):
        sample_x = int(round(center_x + (x - center_x) * 0.62))
        sample_y = min(190, y + 28)
        rgb[y, x] = source[sample_y, sample_x, :3]

    # Keep the photographed head everywhere except the ears. Under each ear,
    # expose a small same-pose inpainted crown patch for twitch rotation.
    head = source.copy()
    head[:, :, :3] = np.where((ear_remove > 12)[:, :, None], rgb, head[:, :, :3])
    alpha = np.minimum(source[:, :, 3], head_mask)
    alpha = np.clip(alpha.astype(np.int16) - ear_remove.astype(np.int16), 0, 255).astype(np.uint8)
    hidden_root = np.zeros_like(alpha)
    for ear in config["ears"]:
        px, py = ear["pivot"]
        root_mask = ellipse_mask((px, py, 23, 17), 3.0)
        hidden_root = np.maximum(hidden_root, np.minimum(np.minimum(core_mask, ear_remove), root_mask))
    hidden_root = np.minimum(hidden_root, source[:, :, 3])
    alpha = np.maximum(alpha, hidden_root)
    head[:, :, 3] = alpha
    head[head[:, :, 3] == 0, :3] = 0
    return head


def make_eye_layers(source: np.ndarray, eyes: list[tuple[int, int]]) -> tuple[np.ndarray, list[np.ndarray], np.ndarray]:
    eye_base = np.zeros_like(source)
    pupils: list[np.ndarray] = []
    blink = np.zeros_like(source)

    for cx, cy in eyes:
        eye_mask = ellipse_mask((cx, cy, 17, 12), 2.0)
        iris_mask = ellipse_mask((cx, cy, 15, 10), 1.2)
        pupil_mask = ellipse_mask((cx, cy, 4, 6), 0.7)

        iris_rgb = same_pose_inpaint(source, pupil_mask)
        eye = np.zeros_like(source)
        eye[:, :, :3] = iris_rgb
        eye[:, :, 3] = np.minimum(source[:, :, 3], eye_mask)
        eye[eye[:, :, 3] == 0, :3] = 0
        eye_base = alpha_over(eye_base, eye)

        pupil = rgba_with_mask(source, pupil_mask)
        pupils.append(pupil)

        # Closed eyelid is a same-source local completion: remove the open eye
        # with neighbouring mask/fur, then add a restrained photographic crease.
        local = source[:, :, :3].copy()
        # Compress the photographed upper-eyelid/facial-fur strip over the
        # aperture. This stays within the approved pose and avoids a painted
        # black oval while still covering the open iris completely.
        y0 = max(0, cy - 12)
        y1 = min(512, cy + 13)
        x0 = max(0, cx - 17)
        x1 = min(512, cx + 18)
        for target_y in range(y0, y1):
            t = (target_y - y0) / max(1, y1 - y0 - 1)
            sample_y = int(round(cy - 17 + t * 9))
            local[target_y, x0:x1] = source[sample_y, x0:x1, :3]
        points = []
        for dx in range(-11, 12):
            y = round(cy + 1 + 0.026 * dx * dx)
            points.append((cx + dx, y))
        sampled = source[max(0, cy - 11):cy - 4, max(0, cx - 10):cx + 11, :3]
        line_color = tuple(int(v) for v in np.percentile(sampled.reshape(-1, 3), 24, axis=0))
        cv2.polylines(local, [np.asarray(points, dtype=np.int32)], False, line_color, 1, cv2.LINE_AA)
        local = cv2.GaussianBlur(local, (0, 0), 0.18)
        blink_piece = np.zeros_like(source)
        blink_piece[:, :, :3] = local
        blink_piece[:, :, 3] = np.minimum(source[:, :, 3], eye_mask)
        blink_piece[blink_piece[:, :, 3] == 0, :3] = 0
        blink = alpha_over(blink, blink_piece)

    return eye_base, pupils, blink


def moved_layer(layer: np.ndarray, pivot: tuple[int, int], dx: float, dy: float, degrees: float) -> np.ndarray:
    matrix = cv2.getRotationMatrix2D(pivot, degrees, 1.0)
    matrix[0, 2] += dx
    matrix[1, 2] += dy
    return cv2.warpAffine(
        layer, matrix, CANVAS, flags=cv2.INTER_LANCZOS4,
        borderMode=cv2.BORDER_CONSTANT, borderValue=(0, 0, 0, 0)
    )


def save_rgba(path: Path, array: np.ndarray) -> None:
    array = array.copy()
    array[array[:, :, 3] == 0, :3] = 0
    Image.fromarray(array, "RGBA").save(path, optimize=True)


def assemble(layers: dict[str, np.ndarray], blink: bool = False) -> np.ndarray:
    order = [
        "Body_Base", "UpperBody_Base", "Head_Base", "Ear_L", "Ear_R",
        "Eye_Base", "Blink_Closed" if blink else "Pupil_L",
    ]
    if not blink:
        order.append("Pupil_R")
    order.extend(["Neck_Fur", "Chest_Fur"])
    result = np.zeros((512, 512, 4), dtype=np.uint8)
    for name in order:
        result = alpha_over(result, layers[name])
    return result


def main() -> None:
    manifest = {"version": 1, "canvas": [512, 512], "sourcePolicy": "approved-pose-only", "poses": {}}

    for direction, config in POSES.items():
        source_path: Path = config["source"]
        source = np.asarray(Image.open(source_path).convert("RGBA"))
        out_dir = ROOT / "Assets/DirectionPoses/Stand" / direction
        out_dir.mkdir(parents=True, exist_ok=True)

        body, upper = make_body_layers(source, config)
        ears, ear_mask = make_ear_layers(source, config)
        head = make_head_base(source, config, ear_mask)
        eyes, pupils, blink = make_eye_layers(source, config["eyes"])
        neck = rgba_with_mask(source, ellipse_mask(config["neck"], 4.5))
        chest = rgba_with_mask(source, ellipse_mask(config["chest"], 7.0))

        layers = {
            "Body_Base": body,
            "UpperBody_Base": upper,
            "Head_Base": head,
            "Neck_Fur": neck,
            "Chest_Fur": chest,
            "Ear_L": ears[0],
            "Ear_R": ears[1],
            "Eye_Base": eyes,
            "Pupil_L": pupils[0],
            "Pupil_R": pupils[1],
            "Blink_Closed": blink,
        }
        for name, layer in layers.items():
            save_rgba(out_dir / f"{name}.png", layer)

        neutral = assemble(layers)
        closed = assemble(layers, blink=True)
        save_rgba(out_dir / "_Preview_Assembled.png", neutral)
        save_rgba(out_dir / "_Preview_Blink.png", closed)

        # Stress the authored overlap without changing runtime assets.
        head_group_names = ["Head_Base", "Ear_L", "Ear_R", "Eye_Base", "Pupil_L", "Pupil_R"]
        head_group = np.zeros_like(source)
        for name in head_group_names:
            head_group = alpha_over(head_group, layers[name])
        moved_head = moved_layer(head_group, config["head_pivot"], 10, -5, -7)
        stress = alpha_over(body, upper)
        stress = alpha_over(stress, moved_head)
        stress = alpha_over(stress, neck)
        stress = alpha_over(stress, chest)
        save_rgba(out_dir / "_Preview_HeadStress.png", stress)

        source_hash = hashlib.sha256(source_path.read_bytes()).hexdigest()
        error = np.abs(neutral.astype(np.int16) - source.astype(np.int16))
        visible = np.maximum(neutral[:, :, 3], source[:, :, 3]) > 24
        mean_error = float(error[visible, :3].mean()) if np.any(visible) else 0.0
        manifest["poses"][direction] = {
            "source": str(source_path.relative_to(ROOT)).replace("\\", "/"),
            "sourceSha256": source_hash,
            "folder": str(out_dir.relative_to(ROOT)).replace("\\", "/"),
            "canvasAlignedLayers": True,
            "headPivot": list(config["head_pivot"]),
            "upperBodyPivot": list(config["upper_pivot"]),
            "eyes": [list(p) for p in config["eyes"]],
            "ears": [
                {"pivot": list(ear["pivot"]), "box": list(ear["box"])} for ear in config["ears"]
            ],
            "neutralAssemblyMeanRgbError": round(mean_error, 3),
            "layers": [f"{name}.png" for name in layers],
        }

    manifest_path = ROOT / "Assets/DirectionPoses/Stand/direction_layers.json"
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    print(manifest_path)


if __name__ == "__main__":
    main()
