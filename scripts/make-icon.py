#!/usr/bin/env python3
"""生成 macOS 应用图标（.icns）。

用法：
    python3 scripts/make-icon.py <输出目录>

会在输出目录生成 AppIcon.icns 与 icon.png（1024x1024，供文档使用）。
需要 Pillow：pip install Pillow
"""

from __future__ import annotations

import subprocess
import sys
import tempfile
from pathlib import Path

from PIL import Image, ImageDraw

# macOS 图标规范：1024 画布上圆角矩形约占 824x824，四周留白
CANVAS = 1024
MARGIN = 100
SQUIRCLE = CANVAS - MARGIN * 2
RADIUS = int(SQUIRCLE * 0.2237)  # Big Sur 风格的连续圆角近似值

# 品牌蓝渐变
TOP_COLOR = (76, 141, 246)
BOTTOM_COLOR = (21, 101, 216)

SS = 4  # 超采样倍数，让边缘更平滑


def rounded_rect_mask(size: int, radius: int) -> Image.Image:
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, size - 1, size - 1), radius=radius, fill=255)
    return mask


def vertical_gradient(size: int, top: tuple[int, int, int], bottom: tuple[int, int, int]) -> Image.Image:
    gradient = Image.new("RGB", (1, size))
    for y in range(size):
        t = y / max(size - 1, 1)
        gradient.putpixel(
            (0, y),
            tuple(round(top[i] + (bottom[i] - top[i]) * t) for i in range(3)),
        )
    return gradient.resize((size, size), Image.Resampling.BILINEAR)


def build_icon() -> Image.Image:
    s = CANVAS * SS
    margin = MARGIN * SS
    squircle = SQUIRCLE * SS
    radius = RADIUS * SS

    canvas = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    draw = ImageDraw.Draw(canvas)

    # 1. 圆角底板（渐变蓝）
    plate = vertical_gradient(squircle, TOP_COLOR, BOTTOM_COLOR).convert("RGBA")
    plate.putalpha(rounded_rect_mask(squircle, radius))
    canvas.alpha_composite(plate, (margin, margin))

    cx = s // 2

    # 2. 白色文档纸片
    paper_w = int(squircle * 0.50)
    paper_h = int(squircle * 0.60)
    paper_left = cx - paper_w // 2
    paper_top = margin + (squircle - paper_h) // 2
    paper_right = paper_left + paper_w
    paper_bottom = paper_top + paper_h
    paper_radius = int(paper_w * 0.10)

    draw.rounded_rectangle(
        (paper_left, paper_top, paper_right, paper_bottom),
        radius=paper_radius,
        fill=(255, 255, 255, 255),
    )

    # 3. 右上角折角
    fold = int(paper_w * 0.26)
    inset = max(int(paper_w * 0.012), SS)
    draw.polygon(
        [
            (paper_right - fold, paper_top + inset),
            (paper_right - inset, paper_top + fold),
            (paper_right - fold, paper_top + fold),
        ],
        fill=(186, 212, 250, 255),
    )

    # 4. 文档顶部的文字线条
    line_color = (178, 199, 232, 255)
    line_h = max(int(paper_h * 0.030), 4 * SS)
    line_left = paper_left + int(paper_w * 0.15)
    line_right = paper_right - int(paper_w * 0.15)
    y = paper_top + int(paper_h * 0.16)
    for width_ratio in (0.62, 1.0, 0.82):
        draw.rounded_rectangle(
            (line_left, y, line_left + int((line_right - line_left) * width_ratio), y + line_h),
            radius=line_h // 2,
            fill=line_color,
        )
        y += int(line_h * 2.2)

    # 5. 纸片下半部分的向下箭头（用底板的蓝，和白色纸张形成清晰对比）
    arrow_color = (26, 115, 232, 255)
    arrow_top = paper_top + int(paper_h * 0.47)
    head_h = int(paper_h * 0.20)
    head_w = int(paper_w * 0.44)
    shaft_w = int(paper_w * 0.16)
    arrow_bottom = paper_bottom - int(paper_h * 0.13)

    shaft_bottom = arrow_bottom - head_h + int(head_h * 0.15)
    draw.rounded_rectangle(
        (cx - shaft_w // 2, arrow_top, cx + shaft_w // 2, shaft_bottom),
        radius=shaft_w // 2,
        fill=arrow_color,
    )
    draw.polygon(
        [
            (cx - head_w // 2, shaft_bottom - int(head_h * 0.1)),
            (cx + head_w // 2, shaft_bottom - int(head_h * 0.1)),
            (cx, arrow_bottom),
        ],
        fill=arrow_color,
    )

    return canvas.resize((CANVAS, CANVAS), Image.Resampling.LANCZOS)


def main() -> int:
    out_dir = Path(sys.argv[1] if len(sys.argv) > 1 else "build/icon")
    out_dir.mkdir(parents=True, exist_ok=True)

    icon = build_icon()
    png_path = out_dir / "icon.png"
    icon.save(png_path)

    with tempfile.TemporaryDirectory() as tmp:
        iconset = Path(tmp) / "AppIcon.iconset"
        iconset.mkdir()

        for size in (16, 32, 128, 256, 512):
            icon.resize((size, size), Image.Resampling.LANCZOS).save(iconset / f"icon_{size}x{size}.png")
            icon.resize((size * 2, size * 2), Image.Resampling.LANCZOS).save(
                iconset / f"icon_{size}x{size}@2x.png"
            )

        subprocess.run(
            ["iconutil", "-c", "icns", str(iconset), "-o", str(out_dir / "AppIcon.icns")],
            check=True,
        )

    print(f"已生成：{out_dir / 'AppIcon.icns'}")
    print(f"预览图：{png_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
