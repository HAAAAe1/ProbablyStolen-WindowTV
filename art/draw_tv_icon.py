"""生成 WindowTV 的物品图标：48x48 像素风复古电视（原创）。

区别于此前的参考图：暖色木壳、兔耳天线、彩条测试卡、两条小脚、右侧旋钮。
用整数坐标逐块绘制，保证像素边缘干净。
"""

import os
import sys

from PIL import Image, ImageDraw

SIZE = 48

# ---- 调色板（整体压暗，和游戏里偏暗的货架氛围更协调）----
INK = (37, 25, 15, 255)        # 最外层描边，深棕而不是纯黑
SHADE = (64, 45, 28, 255)      # 阴影/脚
SHELL = (192, 170, 130, 255)   # 机身主色（米黄木壳）
SHELL_HI = (209, 195, 164, 255)  # 顶部高光
SHELL_LO = (164, 140, 94, 255)  # 底部与右侧暗部
PANEL = (70, 52, 36, 255)      # 右侧控制面板：深棕，和浅机身拉开对比
KNOB = (194, 173, 133, 255)    # 旋钮：米色，深底更亮
KNOB_HI = (255, 255, 255, 160)
BEZEL = (24, 25, 33, 255)      # 屏幕外框
GLASS_LO = (16, 25, 36, 255)   # 屏幕底色

BARS = [
    (177, 64, 56, 255),    # 红
    (191, 114, 47, 255),   # 橙
    (198, 166, 62, 255),   # 黄
    (91, 149, 77, 255),    # 绿
    (64, 150, 144, 255),   # 青
    (60, 98, 152, 255),    # 蓝
    (113, 81, 144, 255),   # 紫
]


def px(d, x, y, color):
    d.point((x, y), fill=color)


def rect(d, x0, y0, x1, y1, color):
    """含端点填充。"""
    d.rectangle([x0, y0, x1, y1], fill=color)


def box(d, x0, y0, x1, y1, color):
    """1px 边框。"""
    d.rectangle([x0, y0, x1, y1], outline=color)


def build():
    img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    # ---------- 兔耳天线（先画，压在机身后面）----------
    # 45 度斜线：每行左移 1px、画 2px 宽，台阶紧密，缩到小尺寸也不会断成虚线
    for i in range(10):
        y = 14 - i
        left = 18 - i
        right = 30 + i
        rect(d, left, y, left + 1, y, INK)
        rect(d, right, y, right + 1, y, INK)
    # 顶端实心小球 + 左上高光
    rect(d, 6, 2, 9, 5, INK)
    rect(d, 38, 2, 41, 5, INK)
    px(d, 7, 3, SHELL_HI)
    px(d, 39, 3, SHELL_HI)

    # ---------- 机身 ----------
    rect(d, 4, 14, 43, 39, INK)          # 描边底
    rect(d, 5, 15, 42, 38, SHELL)        # 面板
    rect(d, 5, 15, 42, 16, SHELL_HI)     # 顶部高光
    rect(d, 6, 17, 41, 17, SHELL_HI)
    rect(d, 41, 18, 42, 35, SHELL_LO)    # 右侧暗边
    rect(d, 5, 36, 42, 38, SHELL_LO)     # 底部暗部
    box(d, 4, 14, 43, 39, INK)           # 再压一次描边，防止填充盖住

    # ---------- 屏幕 ----------
    rect(d, 8, 19, 33, 34, BEZEL)
    rect(d, 9, 20, 32, 20, (10, 11, 16, 255))   # 屏幕上沿内阴影，做出凹陷感
    rect(d, 10, 21, 31, 32, GLASS_LO)

    # 彩条测试卡：7 条竖带，每条 3px
    bar_top, bar_bottom = 21, 28
    x = 10
    for color in BARS:
        rect(d, x, bar_top, x + 2, bar_bottom, color)
        x += 3
    # 彩条下方的深色字幕区 + 扫描线
    rect(d, 10, 29, 31, 32, (30, 42, 56, 255))
    rect(d, 10, 30, 31, 30, (58, 84, 106, 255))
    px(d, 12, 31, (120, 150, 170, 255))
    px(d, 20, 31, (120, 150, 170, 255))
    px(d, 28, 31, (120, 150, 170, 255))

    # ---------- 右侧控制面板 ----------
    rect(d, 34, 19, 41, 35, PANEL)
    box(d, 34, 19, 41, 35, SHELL_LO)
    # 两个旋钮（5x5 去角成圆，深底上一眼能看见）
    for ky in (21, 27):
        rect(d, 35, ky, 39, ky + 4, KNOB)
        px(d, 35, ky, PANEL)
        px(d, 39, ky, PANEL)
        px(d, 35, ky + 4, PANEL)
        px(d, 39, ky + 4, PANEL)
    # 扬声器格栅
    rect(d, 35, 33, 40, 33, KNOB)

    # ---------- 底座与脚 ----------
    rect(d, 16, 39, 31, 39, SHELL_LO)
    rect(d, 8, 40, 13, 42, INK)
    rect(d, 34, 40, 39, 42, INK)
    rect(d, 9, 40, 12, 41, SHADE)
    rect(d, 35, 40, 38, 41, SHADE)

    return img


def main():
    out_dir = os.path.dirname(os.path.abspath(__file__))
    img = build()
    icon_path = os.path.join(out_dir, "television.png")
    img.save(icon_path)

    # 一份 8 倍放大的预览，方便肉眼检查像素
    img.resize((SIZE * 8, SIZE * 8), Image.NEAREST).save(os.path.join(out_dir, "preview_x8.png"))
    print("saved", icon_path)


if __name__ == "__main__":
    sys.exit(main())
