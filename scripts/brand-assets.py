"""Generates KOSHI brand assets (ICO/PNG/favicon) from the master files in assets/brand/.

Requires Pillow (dev-only):  python -m pip install pillow
Usage:                      python scripts/brand-assets.py
"""
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
BRAND = ROOT / "assets" / "brand"
ICON_SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]


def square(img: Image.Image, padding: float = 0.04) -> Image.Image:
    img = img.convert("RGBA")
    img = img.crop(img.getbbox())
    side = int(max(img.size) * (1 + 2 * padding))
    canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    canvas.paste(img, ((side - img.width) // 2, (side - img.height) // 2), img)
    return canvas


def save_ico(img: Image.Image, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    big = img.resize((256, 256), Image.LANCZOS)
    big.save(path, format="ICO", sizes=[(s, s) for s in ICON_SIZES])
    print("wrote", path.relative_to(ROOT))


def save_png(img: Image.Image, path: Path, size: int | None = None, width: int | None = None) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    out = img
    if size:
        out = img.resize((size, size), Image.LANCZOS)
    elif width:
        out = img.resize((width, round(img.height * width / img.width)), Image.LANCZOS)
    out.save(path, format="PNG", optimize=True)
    print("wrote", path.relative_to(ROOT))


def main() -> None:
    icon = square(Image.open(BRAND / "koshi-icon.png"))
    logo = Image.open(BRAND / "koshi-logo.png").convert("RGBA")
    logo = logo.crop(logo.getbbox())

    # Windows agent (EXE, tray, window), service and installer
    save_ico(icon, ROOT / "src" / "KOSHI.Agent" / "Assets" / "koshi.ico")
    save_png(icon, ROOT / "src" / "KOSHI.Agent" / "Assets" / "koshi-icon.png", size=256)
    save_png(logo, ROOT / "src" / "KOSHI.Agent" / "Assets" / "koshi-logo.png", width=480)
    save_ico(icon, ROOT / "src" / "KOSHI.Agent.Service" / "koshi.ico")
    save_ico(icon, ROOT / "deploy" / "installer" / "koshi.ico")

    # Admin dashboard
    admin = ROOT / "src" / "koshi-admin"
    save_ico(icon, admin / "public" / "favicon.ico")
    save_png(icon, admin / "public" / "koshi-icon-192.png", size=192)
    save_png(icon, admin / "public" / "koshi-icon-512.png", size=512)
    save_png(icon, admin / "src" / "assets" / "koshi-icon.png", size=128)
    save_png(logo, admin / "src" / "assets" / "koshi-logo.png", width=640)


if __name__ == "__main__":
    main()
