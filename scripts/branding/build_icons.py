"""Сборка иконки Rubrica из assets/logo_icr.png (Phase 12.1, new_addons.md §1.7).

    python scripts/branding/build_icons.py

Выход — src/StudComp.App/Resources/Branding/:
  Rubrica.ico            мультиразмер 16..256 — герб-медальон, качественный ступенчатый даунскейл
  logo-256.png / 512     тот же герб покрупнее (сплэш, «О программе», онбординг)
  glyph-16/24/32/48.png  герб в мелком размере для титул-бара

Иконка — фирменный герб пользователя во всех размерах. ImageMagick в системе нет: даунскейл делаем
ступенчато (последовательное деление пополам боксом + финальный LANCZOS) — так тонкий орнамент
кольца сохраняется лучше, чем при одном LANCZOS 1268→16; плюс лёгкий unsharp на мелких.
"""

from __future__ import annotations

import io
import struct
import sys
from pathlib import Path

from PIL import Image, ImageFilter

REPO = Path(__file__).resolve().parents[2]
SOURCE = REPO / "assets" / "logo_icr.png"
OUT = REPO / "src" / "StudComp.App" / "Resources" / "Branding"

ICO_SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
GLYPH_SIZES = [16, 24, 32, 48]
PAD_RATIO = 0.012


def load_medallion() -> Image.Image:
    img = Image.open(SOURCE).convert("RGBA")
    bbox = img.getbbox()
    if bbox:
        img = img.crop(bbox)
    side = max(img.size)
    pad = int(side * PAD_RATIO)
    canvas = Image.new("RGBA", (side + 2 * pad, side + 2 * pad), (0, 0, 0, 0))
    canvas.paste(img, (pad + (side - img.width) // 2, pad + (side - img.height) // 2), img)
    return canvas


def downscale(base: Image.Image, size: int) -> Image.Image:
    """Ступенчатый даунскейл: делим пополам, пока не близко к цели, затем LANCZOS."""
    cur = base
    while cur.width // 2 >= size:
        cur = cur.resize((cur.width // 2, cur.height // 2), Image.BOX)

    out = cur.resize((size, size), Image.LANCZOS)
    if size <= 48:
        out = out.filter(ImageFilter.UnsharpMask(radius=0.8, percent=60, threshold=0))
    return out


def write_ico(path: Path, frames: list[Image.Image]) -> None:
    entries: list[tuple[bytes, bytes]] = []
    for frame in frames:
        buf = io.BytesIO()
        frame.save(buf, format="PNG")
        data = buf.getvalue()
        w = 0 if frame.width >= 256 else frame.width
        h = 0 if frame.height >= 256 else frame.height
        entries.append((struct.pack("<BBBBHH", w, h, 0, 0, 1, 32), data))

    offset = 6 + 16 * len(entries)
    out = bytearray(struct.pack("<HHH", 0, 1, len(entries)))
    blobs = bytearray()
    for header, data in entries:
        out += header + struct.pack("<II", len(data), offset)
        blobs += data
        offset += len(data)

    path.write_bytes(bytes(out) + bytes(blobs))


def main() -> int:
    if not SOURCE.exists():
        print(f"нет исходника: {SOURCE}", file=sys.stderr)
        return 1

    OUT.mkdir(parents=True, exist_ok=True)
    base = load_medallion()

    write_ico(OUT / "Rubrica.ico", [downscale(base, s) for s in ICO_SIZES])
    downscale(base, 256).save(OUT / "logo-256.png")
    base.resize((512, 512), Image.LANCZOS).save(OUT / "logo-512.png")
    for s in GLYPH_SIZES:
        downscale(base, s).save(OUT / f"glyph-{s}.png")

    print("готово:", ", ".join(p.name for p in sorted(OUT.iterdir())))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
