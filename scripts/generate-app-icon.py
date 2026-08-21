from __future__ import annotations

import struct
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
FRAMES_DIRECTORY = ROOT / "assets" / "app" / "icon-frames"
ICON_PATH = ROOT / "assets" / "app" / "InputCue.ico"
SIZES = (16, 20, 24, 32, 40, 48, 64, 128, 256)


def read_frames() -> list[bytes]:
    frames: list[bytes] = []
    for size in SIZES:
        path = FRAMES_DIRECTORY / f"InputCue-{size}x{size}.png"
        with Image.open(path) as image:
            if image.size != (size, size) or image.mode != "RGBA":
                raise ValueError(f"{path.name} must be an RGBA {size}x{size} PNG.")
        frames.append(path.read_bytes())
    return frames


def write_ico() -> None:
    frames = read_frames()
    offset = 6 + (16 * len(SIZES))
    entries: list[bytes] = []
    for size, frame in zip(SIZES, frames, strict=True):
        encoded_size = 0 if size == 256 else size
        entries.append(struct.pack(
            "<BBBBHHII",
            encoded_size,
            encoded_size,
            0,
            0,
            1,
            32,
            len(frame),
            offset,
        ))
        offset += len(frame)

    with ICON_PATH.open("wb") as output:
        output.write(struct.pack("<HHH", 0, 1, len(SIZES)))
        output.writelines(entries)
        output.writelines(frames)


if __name__ == "__main__":
    write_ico()
