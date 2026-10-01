from pathlib import Path

import cv2
import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
KEYS = ROOT / "Assets" / "Sprites"
OUTPUT = ROOT / "Assets" / "SpritesDense"


def load_rgba(path: Path) -> np.ndarray:
    return np.asarray(Image.open(path).convert("RGBA"), dtype=np.float32) / 255.0


def guide(frame: np.ndarray) -> np.ndarray:
    # Optical flow needs visible pixels outside the alpha channel to be stable.
    alpha = frame[:, :, 3:4]
    rgb = frame[:, :, :3] * alpha + 0.45 * (1.0 - alpha)
    return cv2.cvtColor((rgb * 255).astype(np.uint8), cv2.COLOR_RGB2GRAY)


def warp(data: np.ndarray, flow: np.ndarray, amount: float) -> np.ndarray:
    height, width = data.shape[:2]
    x, y = np.meshgrid(np.arange(width), np.arange(height))
    map_x = (x - flow[:, :, 0] * amount).astype(np.float32)
    map_y = (y - flow[:, :, 1] * amount).astype(np.float32)
    single_channel = data.ndim == 3 and data.shape[2] == 1
    source = data[:, :, 0] if single_channel else data
    warped = cv2.remap(source, map_x, map_y, cv2.INTER_CUBIC,
                       borderMode=cv2.BORDER_CONSTANT, borderValue=0)
    return warped[:, :, None] if single_channel else warped


def inbetween(first: np.ndarray, second: np.ndarray, t: float) -> np.ndarray:
    gray_a, gray_b = guide(first), guide(second)
    flow_ab = cv2.calcOpticalFlowFarneback(
        gray_a, gray_b, None, 0.5, 5, 25, 4, 7, 1.5, 0
    )
    flow_ba = cv2.calcOpticalFlowFarneback(
        gray_b, gray_a, None, 0.5, 5, 25, 4, 7, 1.5, 0
    )

    alpha_a, alpha_b = first[:, :, 3:4], second[:, :, 3:4]
    premul_a = first[:, :, :3] * alpha_a
    premul_b = second[:, :, :3] * alpha_b

    # Do not alpha-blend the two silhouettes. Even with correct flow, fur and
    # independently moving paws can create visible double edges. Instead use
    # the temporally nearest keyframe and warp that single subject toward the
    # requested time. Every result therefore contains exactly one cat.
    if t <= 0.5:
        premul = warp(premul_a, flow_ab, t)
        alpha = warp(alpha_a, flow_ab, t)
    else:
        premul = warp(premul_b, flow_ba, 1.0 - t)
        alpha = warp(alpha_b, flow_ba, 1.0 - t)
    rgb = np.divide(premul, np.maximum(alpha, 1e-5), out=np.zeros_like(premul), where=alpha > 1e-5)
    rgba = np.concatenate((np.clip(rgb, 0, 1), np.clip(alpha, 0, 1)), axis=2)
    rgba[rgba[:, :, 3] < 0.008] = 0
    return rgba


def save(frame: np.ndarray, path: Path) -> None:
    Image.fromarray(np.round(frame * 255).astype(np.uint8), "RGBA").save(path, optimize=True)


def read_category(name: str) -> list[np.ndarray]:
    files = sorted((KEYS / name).glob("*.png"))
    if not files:
        raise RuntimeError(f"No keyframes found for {name}")
    return [load_rgba(path) for path in files]


def render(name: str, frames: list[np.ndarray], subdivisions: int, loop: bool = False) -> int:
    folder = OUTPUT / name
    folder.mkdir(parents=True, exist_ok=True)
    for old in folder.glob("*.png"):
        old.unlink()

    result: list[np.ndarray] = []
    pair_count = len(frames) if loop else len(frames) - 1
    for index in range(pair_count):
        first = frames[index]
        second = frames[(index + 1) % len(frames)]
        result.append(first)
        for step in range(1, subdivisions):
            result.append(inbetween(first, second, step / subdivisions))
    if not loop:
        result.append(frames[-1])

    for index, frame in enumerate(result):
        save(frame, folder / f"{name}_{index:03d}.png")
    return len(result)


counts = {}
counts["locomotion"] = render("locomotion", read_category("locomotion"), 4, loop=True)
for category in ("walk_transition", "curious_groom", "yawn"):
    counts[category] = render(category, read_category(category), 4)

# The large stand-to-sleep motion uses 15 purpose-painted anatomical keys.
# Only one conservative optical-flow midpoint is inserted between them.
phase_a = read_category("sleep_phase_a")
phase_b = read_category("sleep_phase_b")
sleep_keys = phase_a + phase_b[1:]
counts["sleep_enter"] = render("sleep_enter", sleep_keys, 2)
counts["wake_up"] = render("wake_up", list(reversed(sleep_keys)), 2)

print("Dense sprite counts:", ", ".join(f"{k}={v}" for k, v in counts.items()))
