"""
Renders the My Git icon (crest shield + symmetric "Y merge" commit glyph)
to SVG, PNG and a multi-size Windows .ico.

Usage:  python render_icons.py
Output: svg/, png/ and my-git.ico next to this script.

Everything is defined on a 120 x 120 grid, the same grid the SVGs use.
Three detail tiers keep the icon crisp at every size:
  large  (48 px and up)   thin strokes, three commit dots
  medium (24 to 40 px)    thicker strokes, three commit dots
  small  (16 and 20 px)   thickest strokes, only the bottom commit dot
"""

from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).parent
BACKGROUND = "#161615"
FOREGROUND = "#FFFFFF"
CORNER_RADIUS = 26
SUPERSAMPLE = 8  # draw 8x larger, then downscale for smooth edges

# How big the shield and glyph are inside the rounded square (1.0 = original artwork).
# Strokes keep their tuned widths; only positions shrink toward ART_CENTER.
ART_SCALE = 0.8
ART_CENTER = (60, 61)

# Outline-only variants (no background) for the window title bar.
OUTLINE_COLORS = {"white": "#FFFFFF", "black": "#1F2328"}
OUTLINE_ICO_SIZES = [16, 20, 24, 32, 40, 48]

# Shapes on the 120 grid. Each path is a start point plus segments:
# ("L", x, y) for straight lines, ("C", x1, y1, x2, y2, x, y) for cubic curves.
CREST = [
    (22, 27),
    ("C", 40, 27, 50, 22, 60, 14),
    ("C", 70, 22, 80, 27, 98, 27),
    ("L", 98, 58),
    ("C", 98, 84, 82, 100, 60, 108),
    ("C", 38, 100, 22, 84, 22, 58),
    ("L", 22, 27),
]
LEFT_BRANCH = [(48, 42), ("L", 48, 50), ("C", 48, 60, 60, 58, 60, 68), ("L", 60, 78)]
RIGHT_BRANCH = [(72, 42), ("L", 72, 50), ("C", 72, 60, 60, 58, 60, 68)]

TIERS = {
    "large": {"crest": 6, "glyph": 3, "top_dot": 4.5, "bottom_dot": 5.5, "branch_x": 0},
    "medium": {"crest": 8, "glyph": 4.5, "top_dot": 5.5, "bottom_dot": 6.5, "branch_x": 0},
    "small": {"crest": 11, "glyph": 9, "top_dot": 0, "bottom_dot": 10, "branch_x": 4},
}

PNG_SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256, 512, 1024]
ICO_SIZES = [16, 20, 24, 32, 40, 48, 64, 256]


def art(point, art_scale=None):
    """Shrinks a point on the 120 grid toward ART_CENTER (by ART_SCALE unless given)."""
    factor = ART_SCALE if art_scale is None else art_scale
    cx, cy = ART_CENTER
    return (cx + (point[0] - cx) * factor, cy + (point[1] - cy) * factor)


def tier_for(size: int) -> str:
    if size <= 20:
        return "small"
    if size <= 40:
        return "medium"
    return "large"


def shift_branches(spread: float):
    """Moves the two branch tops outward by `spread` units (used by the small tier)."""
    def shift(path, dx):
        out = []
        for item in path:
            if isinstance(item[0], str):
                kind, *coords = item
                # Only the first points of each branch move; the merge stays centred.
                moved = [c + dx if i % 2 == 0 and c != 60 else c for i, c in enumerate(coords)]
                out.append((kind, *moved))
            else:
                out.append((item[0] + dx, item[1]))
        return out
    return shift(LEFT_BRANCH, -spread), shift(RIGHT_BRANCH, spread)


def flatten(path, steps=48):
    """Turns a path into a list of points."""
    points = [path[0]]
    for segment in path[1:]:
        x0, y0 = points[-1]
        if segment[0] == "L":
            points.append((segment[1], segment[2]))
        else:
            _, x1, y1, x2, y2, x3, y3 = segment
            for i in range(1, steps + 1):
                t = i / steps
                a, b, c, d = (1 - t) ** 3, 3 * (1 - t) ** 2 * t, 3 * (1 - t) * t ** 2, t ** 3
                points.append((a * x0 + b * x1 + c * x2 + d * x3, a * y0 + b * y1 + c * y2 + d * y3))
    return points


def render(size: int, background: bool = True, color: str = FOREGROUND) -> Image.Image:
    # Without the rounded square there's nothing to leave room for: use the full artwork size.
    art_scale = ART_SCALE if background else 1.0
    tier = TIERS[tier_for(size)]
    scale = size * SUPERSAMPLE / 120

    canvas = Image.new("RGBA", (size * SUPERSAMPLE,) * 2, (0, 0, 0, 0))
    draw = ImageDraw.Draw(canvas)

    def p(point):
        x, y = art(point, art_scale)
        return (x * scale, y * scale)

    def stroke(path, width, closed=False):
        points = [p(pt) for pt in flatten(path)]
        if closed:
            points.append(points[1])  # overlap one segment so the closing join is smooth
        w = width * scale
        draw.line(points, fill=color, width=round(w), joint="curve")
        # Pillow leaves tiny gaps where thick segments meet; a round dot on every point fills them.
        for point in points:
            dot(point, w / 2)
        if not closed:
            for end in (points[0], points[-1]):
                dot(end, w / 2)

    def dot(center, radius):
        x, y = center
        draw.ellipse((x - radius, y - radius, x + radius, y + radius), fill=color)

    if background:
        draw.rounded_rectangle((0, 0, canvas.width - 1, canvas.height - 1), radius=CORNER_RADIUS * scale, fill=BACKGROUND)
    stroke(CREST, tier["crest"], closed=True)

    left, right = shift_branches(tier["branch_x"])
    stroke(left, tier["glyph"])
    stroke(right, tier["glyph"])

    if tier["top_dot"]:
        dot(p(left[0]), tier["top_dot"] * scale)
        dot(p(right[0]), tier["top_dot"] * scale)
    dot(p((60, 79)), tier["bottom_dot"] * scale)

    return canvas.resize((size, size), Image.LANCZOS)


def fmt(value: float) -> str:
    return f"{value:.2f}".rstrip("0").rstrip(".")


def svg_point(x, y) -> str:
    ax, ay = art((x, y))
    return f"{fmt(ax)} {fmt(ay)}"


def svg_path(path):
    start, *segments = path
    parts = [f"M{svg_point(*start)}"]
    for s in segments:
        coords = s[1:]
        pairs = " ".join(svg_point(coords[i], coords[i + 1]) for i in range(0, len(coords), 2))
        parts.append(("L" if s[0] == "L" else "C") + pairs)
    return " ".join(parts)


def write_svg(name: str, tier_name: str):
    tier = TIERS[tier_name]
    left, right = shift_branches(tier["branch_x"])
    bottom_x, bottom_y = art((60, 79))
    top_dots = ""
    if tier["top_dot"]:
        top_dots = (
            f'\n  <circle cx="{fmt(art(left[0])[0])}" cy="{fmt(art(left[0])[1])}" r="{tier["top_dot"]}" fill="{FOREGROUND}"/>'
            f'\n  <circle cx="{fmt(art(right[0])[0])}" cy="{fmt(art(right[0])[1])}" r="{tier["top_dot"]}" fill="{FOREGROUND}"/>'
        )
    svg = f"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 120 120" width="120" height="120">
  <title>My Git</title>
  <rect width="120" height="120" rx="{CORNER_RADIUS}" fill="{BACKGROUND}"/>
  <path d="{svg_path(CREST[:-1])} Z" fill="none" stroke="{FOREGROUND}" stroke-width="{tier["crest"]}" stroke-linejoin="round"/>
  <path d="{svg_path(left)} {svg_path(right)}" fill="none" stroke="{FOREGROUND}" stroke-width="{tier["glyph"]}" stroke-linecap="round" stroke-linejoin="round"/>{top_dots}
  <circle cx="{fmt(bottom_x)}" cy="{fmt(bottom_y)}" r="{tier["bottom_dot"]}" fill="{FOREGROUND}"/>
</svg>
"""
    (ROOT / "svg" / name).write_text(svg, encoding="utf-8")


def main():
    (ROOT / "svg").mkdir(exist_ok=True)
    (ROOT / "png").mkdir(exist_ok=True)

    write_svg("my-git-icon.svg", "large")
    write_svg("my-git-icon-medium.svg", "medium")
    write_svg("my-git-icon-small.svg", "small")

    images = {size: render(size) for size in sorted(set(PNG_SIZES + ICO_SIZES))}
    for size in PNG_SIZES:
        images[size].save(ROOT / "png" / f"my-git-{size}.png")

    # Each .ico entry gets its own hand-tuned image instead of a resized copy.
    largest = images[max(ICO_SIZES)]
    largest.save(
        ROOT / "my-git.ico",
        sizes=[(s, s) for s in ICO_SIZES],
        append_images=[images[s] for s in ICO_SIZES if s != max(ICO_SIZES)],
    )

    # Outline-only title bar icons: white for dark title bars, black for light ones.
    for name, color in OUTLINE_COLORS.items():
        outline = {size: render(size, background=False, color=color) for size in OUTLINE_ICO_SIZES}
        outline[max(OUTLINE_ICO_SIZES)].save(
            ROOT / f"my-git-outline-{name}.ico",
            sizes=[(s, s) for s in OUTLINE_ICO_SIZES],
            append_images=[outline[s] for s in OUTLINE_ICO_SIZES if s != max(OUTLINE_ICO_SIZES)],
        )

    # A contact sheet to eyeball every size at once.
    sheet = Image.new("RGBA", (sum(PNG_SIZES[:9]) + 10 * 12, 280), (240, 240, 240, 255))
    x = 12
    for size in PNG_SIZES[:9]:
        sheet.alpha_composite(images[size], (x, 270 - size))
        x += size + 12
    sheet.save(ROOT / "png" / "contact-sheet.png")
    print("Wrote svg/, png/, my-git.ico and the outline .ico files")


if __name__ == "__main__":
    main()
