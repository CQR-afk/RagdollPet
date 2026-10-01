from __future__ import annotations

from pathlib import Path
from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[1]
CANDIDATES = ROOT / "Assets" / "DirectionPoses" / "Candidates"
RAW = CANDIDATES / "Raw"
CENTER_PATH = ROOT / "Assets" / "Sprites" / "idle" / "idle_00.png"

NAMES = [
    "Stand_Left3Q_01",
    "Stand_Left3Q_02",
    "Stand_Left3Q_03",
    "Stand_Right3Q_01",
    "Stand_Right3Q_02",
    "Stand_Right3Q_03",
]


def alpha_bbox(image: Image.Image, threshold: int = 5) -> tuple[int, int, int, int]:
    alpha = image.getchannel("A")
    mask = alpha.point(lambda value: 255 if value > threshold else 0)
    bbox = mask.getbbox()
    if bbox is None:
        raise ValueError("Image has no visible alpha content")
    return bbox


def normalize(source: Path, target_bbox: tuple[int, int, int, int]) -> Image.Image:
    image = Image.open(source).convert("RGBA")
    bbox = alpha_bbox(image)
    subject = image.crop(bbox)

    target_left, target_top, target_right, target_bottom = target_bbox
    target_height = target_bottom - target_top
    scale = target_height / subject.height
    width = max(1, round(subject.width * scale))
    height = max(1, round(subject.height * scale))
    subject = subject.resize((width, height), Image.Resampling.LANCZOS)

    target_center_x = (target_left + target_right) / 2.0
    x = round(target_center_x - width / 2.0)
    y = target_bottom - height

    # Preserve uniform scaling. If a generated silhouette is slightly wider,
    # shift it inside the canvas instead of stretching or cropping it.
    x = min(max(x, 0), 512 - width)
    y = min(max(y, 0), 512 - height)

    canvas = Image.new("RGBA", (512, 512), (0, 0, 0, 0))
    canvas.alpha_composite(subject, (x, y))
    return canvas


def checkerboard(size: tuple[int, int], cell: int = 16) -> Image.Image:
    width, height = size
    result = Image.new("RGBA", size, (228, 228, 228, 255))
    draw = ImageDraw.Draw(result)
    for y in range(0, height, cell):
        for x in range(0, width, cell):
            if (x // cell + y // cell) % 2:
                draw.rectangle((x, y, x + cell - 1, y + cell - 1), fill=(202, 202, 202, 255))
    return result


def labeled_panel(image: Image.Image, label: str) -> Image.Image:
    panel = checkerboard((512, 560))
    panel.alpha_composite(image, (0, 48))
    draw = ImageDraw.Draw(panel)
    font = ImageFont.load_default(size=22)
    box = draw.textbbox((0, 0), label, font=font)
    x = (512 - (box[2] - box[0])) // 2
    draw.text((x, 13), label, fill=(28, 28, 28, 255), font=font)
    return panel


def save_strip(items: list[tuple[str, Image.Image]], destination: Path) -> None:
    strip = Image.new("RGBA", (512 * len(items), 560), (255, 255, 255, 255))
    for index, (label, image) in enumerate(items):
        strip.alpha_composite(labeled_panel(image, label), (512 * index, 0))
    strip.convert("RGB").save(destination, quality=95)


def main() -> None:
    CANDIDATES.mkdir(parents=True, exist_ok=True)
    center = Image.open(CENTER_PATH).convert("RGBA")
    center_bbox = alpha_bbox(center)

    normalized: dict[str, Image.Image] = {}
    for name in NAMES:
        output = normalize(RAW / f"{name}_raw.png", center_bbox)
        output.save(CANDIDATES / f"{name}.png", optimize=True)
        normalized[name] = output

    # Provisional identity-first picks for review only. These are not wired into runtime.
    selected_left = normalized["Stand_Left3Q_02"]
    selected_right = normalized["Stand_Right3Q_03"]
    save_strip(
        [
            ("Left3Q candidate 02", selected_left),
            ("Stand Center", center),
            ("Right3Q candidate 03", selected_right),
        ],
        CANDIDATES / "Stand_DirectionPose_Comparison.jpg",
    )

    save_strip(
        [(name.replace("Stand_", ""), normalized[name]) for name in NAMES],
        CANDIDATES / "Stand_DirectionPose_AllCandidates.jpg",
    )

    print(f"Center alpha bbox: {center_bbox}")
    for name in NAMES:
        print(f"{name}: {alpha_bbox(normalized[name])}")


if __name__ == "__main__":
    main()
