#!/usr/bin/env python3
"""生成 TuneLab 文件类型图标（Windows .ico，多尺寸）+ 对应的 svg 源。

产出到 TuneLab/Assets/FileIcons/：
  TuneLabProject.ico      —— .tlpx 工程文件（默认保存格式）
  TuneLabProjectJson.ico  —— .tlp  工程文件（JSON 文本形态），换青调纸面以示同族异编码
  TuneLabExtension.ico    —— .tlx  扩展包，方块阵 + 右下留出待装上的那一块

【品牌记号来自素材本体，不是照着画的】
同目录放了 mark.png（无背景、带 alpha 的记号素材）就直接用它，一步图像处理都没有。
没有的话，退而从 TuneLab/Assets/TuneLab.ico 的最大帧反混合抠出记号（背景拟合 → alpha
→ 去掉脚下投影 → 还原前景色）。两条路都保证金属拉丝、耳罩弧度、T 的比例是原样。
纸面（深色折角纸）与扩展包的方块阵仍是本脚本画的矢量。

对画质的三处交代：
  · 记号实心部分（约 77% 的像素）逐像素等于源图——a=1 时反混合按定义即原像素；
    半透明边缘的颜色改从实心区向外扩散得到，不含背景色。
  · 一律从最大帧一次下采样到位，不用「与目标同尺寸的那一帧再缩一点」：那是两次重采样，
    且 0.83 这种接近 1 的比例最伤锐度（实测边缘能量低 18%）。
  · 两种工程文件共用同一份记号，区分只落在纸面配色上——记号一个像素都没动过。

尺寸档：
  32px 及以上   位图记号，缩到纸宽的 MARK_FILL 居中；折角盖在记号之上。
  16 / 20 / 24  手绘整数像素：这几档位图缩下来只剩一团灰雾（源 ico 自己的 16px 帧同样如此），
                故按真记号的比例重绘为纯矩形——去掉唯一的曲线头梁弧，边界落在整像素上才锐利。

用法：
  python tools/icons/gen-file-icons.py                 # 写 ico + svg
  python tools/icons/gen-file-icons.py --preview p.png # 附带多尺寸 / 双底色对照图
依赖：pillow, numpy
"""
import argparse
import base64
import colorsys
import io
import os
import struct
import numpy as np
from functools import lru_cache
from PIL import Image, ImageDraw, ImageFilter

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
SRC_ICON = os.path.join(REPO, "TuneLab", "Assets", "TuneLab.ico")
ASSETS = os.path.join(REPO, "TuneLab", "Assets", "FileIcons")
SVG_DIR = os.path.dirname(os.path.abspath(__file__))
# 可选：直接提供的记号素材（无背景、带 alpha 的大图）。存在则优先，连抠图都省了。
MARK_PNG = os.path.join(SVG_DIR, "mark.png")

S = 1024                 # 矢量部分的绘制画布
G = S // 16              # 16px 网格的一格；关键坐标量化到它的整数倍
OUT_SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
PX_MAX = 24              # 不超过这个尺寸的档手绘像素
PAPER_W = 0.75           # 纸宽 / 画布
MARK_FILL = 0.76         # 记号宽 / 纸宽（记号越瘦高，这个越要收，否则顶到纸边）
PX_MARK_FILL = 0.80     # 手绘档同一比例：小尺寸笔画粗一点才认得出，但再宽耳罩就顶到纸边

# ---- 色板（采样自应用图标）----
GHOST = (150, 156, 182)                                # 扩展包里的空位轮廓
# 扩展包方块用的蓝紫：取自素材耳罩的明暗两端，见 cup_gradient()

# 纸面配色。两种工程文件的区分刻意放在**纸面**上而不是记号上：
#   · 记号是品牌资产，一动（换色相、加东西）就不再是原样；
#   · 纸面是大面积色块，16px 也一眼分得出——角标在那个尺寸根本画不出来，
#     实测 32px 起就糊成一个小色块，等于只有大图标能区分。
PAPERS = {
    "default": dict(                                   # .tlpx：品牌深蓝黑
        dark=((58, 61, 82), (24, 25, 31)),             # 左上受光 → 右下背光
        fold=((92, 97, 128), (56, 59, 79)),            # 折起的纸背
        px=(40, 42, 56), px_fold=(86, 91, 120), px_edge=(63, 66, 88)),
}

# .tlp 的纸面不另配一套颜色，而是把 .tlpx 的纸面**只旋色相**、饱和度与明度原样保留：
# 这样两种纸的明暗质感完全一致，只差色相，规则也说得清（手挑色值会悄悄比另一套更饱和）。
# +120° 落在红棕：与蓝紫是明确的暖冷对比，任何尺寸一眼分得开；语义上也贴 —— .tlp 是更老
# 的那个格式，旧纸/古典的调子正合适。
JSON_HUE_SHIFT = 120


def _rotate_hue(rgb, deg):
    h, s, v = colorsys.rgb_to_hsv(*[c / 255 for c in rgb])
    return tuple(round(c * 255) for c in colorsys.hsv_to_rgb((h + deg / 360) % 1.0, s, v))


def _derive_paper(base, deg):
    out = {}
    for k, v in base.items():
        out[k] = tuple(_rotate_hue(c, deg) for c in v) if isinstance(v[0], tuple)             else _rotate_hue(v, deg)
    return out


PAPERS["json"] = _derive_paper(PAPERS["default"], JSON_HUE_SHIFT)


def q(v):
    """量化到 16px 网格。"""
    return int(round(v / G) * G)


# ================= 从应用图标里取记号 =================
def source_frame(n):
    """读源 ico 的一帧。没有恰好 n 的帧就取最近的更大帧（只下采样，不放大）。"""
    with Image.open(SRC_ICON) as probe:
        sizes = sorted(w for w, h in probe.info["sizes"])
    pick = next((s for s in sizes if s >= n), sizes[-1])
    im = Image.open(SRC_ICON)
    im.size = (pick, pick)
    return im.convert("RGBA")


def extract_mark(img):
    """把记号从深色背景上反混合抠出来，返回带 alpha 的 RGBA。

    背景是平滑渐变，先用「不像记号」的像素把它拟合成二维二次多项式，于是记号覆盖处的
    背景也能外推出来；每像素与拟合背景的色距给出 alpha；再反混合 F = B + (C-B)/a
    还原前景真色，否则半透明边缘会带着背景色、贴到别处显脏。
    """
    n = img.size[0]
    a = np.array(img)
    rgb, alpha0 = a[..., :3].astype(np.float64), a[..., 3]
    lum = rgb.mean(2)
    mx, mn = rgb.max(2), rgb.min(2)
    sat = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1), 0)

    yy, xx = np.mgrid[0:n, 0:n].astype(np.float64)
    u, v = xx / max(n - 1, 1), yy / max(n - 1, 1)
    basis = np.stack([np.ones_like(u), u, v, u * u, u * v, v * v], -1)

    seed = (lum > 130) | ((sat > 0.40) & (lum > 70))          # 粗判记号
    grow = max(3, (n // 32) * 2 + 1)                          # 膨胀，边缘不进拟合
    seed = np.array(Image.fromarray((seed * 255).astype(np.uint8))
                    .filter(ImageFilter.MaxFilter(grow))) > 127
    fit = (~seed) & (alpha0 > 250)
    bg = np.zeros_like(rgb)
    for c in range(3):
        coef, *_ = np.linalg.lstsq(basis[fit], rgb[..., c][fit], rcond=None)
        bg[..., c] = basis @ coef

    alpha = np.clip((np.linalg.norm(rgb - bg, axis=2) - 8.0) / 22.0, 0, 1)
    alpha[alpha0 < 250] = 0                                   # 圆角外不参与
    # 记号脚下有一片投影：它同样与背景有色差，但比背景【暗】。留着就成一团灰雾，
    # 故只保留比背景更亮或更饱和的像素（记号最暗处仍远亮于背景）。
    bg_lum = bg.mean(2)
    bg_mx, bg_mn = bg.max(2), bg.min(2)
    bg_sat = np.where(bg_mx > 0, (bg_mx - bg_mn) / np.maximum(bg_mx, 1), 0)
    alpha[((lum - bg_lum) < 4) & ((sat - bg_sat) < 0.12)] = 0
    box = np.zeros((n, n), bool)                              # 画布四周是背景的辉光，别误收
    m = max(1, int(round(n * 0.12)))
    box[m:n - m, m:n - m] = True
    alpha[~box] = 0
    alpha[alpha < 0.05] = 0

    # 实心处 a=1，反混合按定义就等于原像素（逐像素零损失）。但最外一圈半透明像素上
    # (C-B)/a 会放大背景估计误差、把背景色掺进边缘，贴到别的底色上就显出一道脏轮廓；
    # 那里改成从实心区向外扩散填充——记号是平滑的金属/塑料渐变，外扩几乎无误差，
    # 且彻底不含背景色。alpha 本身仍由色距给出，边缘的柔和过渡不受影响。
    fg = np.clip(bg + (rgb - bg) / np.maximum(alpha, 0.18)[..., None], 0, 255)
    fg = spread_color(fg, alpha >= 0.9)
    return Image.fromarray(np.dstack([fg, alpha * 255]).astype(np.uint8), "RGBA")


def spread_color(fg, seed, iters=4):
    """把 seed 区域的颜色逐层向外扩散（3×3 邻域均值），seed 内的像素不动。"""
    fg, m = fg.copy(), seed.copy()
    for _ in range(iters):
        acc = np.zeros_like(fg)
        cnt = np.zeros(m.shape)
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                acc += np.roll(np.roll(fg * m[..., None], dy, 0), dx, 1)
                cnt += np.roll(np.roll(m.astype(np.float64), dy, 0), dx, 1)
        ring = (~m) & (cnt > 0)
        fg[ring] = acc[ring] / cnt[ring][..., None]
        m = m | ring
    return fg


@lru_cache(maxsize=2)
def base_mark():
    """取记号本体（已裁到 bbox）。优先用直接提供的素材，没有才从应用图标里抠。

    · MARK_PNG 存在时直接用：无背景素材不需要抠图，一步处理都没有。
      规格见 README「直接提供记号素材」。
    · 否则从源 ico 的最大帧抠。不用「与目标同尺寸的那一帧」——那帧自己已经是从大图
      缩过一次的，再按构图需要缩 0.83 就是第二次重采样，而 0.83 这种接近 1 的比例最伤
      锐度（实测边缘能量比从最大帧一次下采样低 18%）。始终从最大帧一次缩到位。

    记号对两种工程文件是**同一份**：区分在纸面配色上，见 PAPERS。
    """
    if os.path.exists(MARK_PNG):
        with Image.open(MARK_PNG) as im:
            mark = im.convert("RGBA")
    else:
        mark = extract_mark(source_frame(max_frame_size()))
    return mark.crop(mark.getbbox())


def max_frame_size():
    with Image.open(SRC_ICON) as probe:
        return max(w for w, h in probe.info["sizes"])


def mark_for(n):
    """给尺寸 n 用的记号：从最大的一份一次下采样到纸宽的 MARK_FILL。"""
    crop = base_mark()
    tw = max(1, round(n * PAPER_W * MARK_FILL))
    return crop.resize((tw, max(1, round(crop.height * tw / crop.width))), Image.LANCZOS)


@lru_cache(maxsize=1)
def cup_gradient():
    """素材里耳罩的明暗两端，给扩展包的方块用——这样三枚图标的蓝紫是同一个来源。"""
    m = base_mark()
    a = np.array(m)
    w = a.shape[1]
    side = max(1, round(w * 0.18))
    px = a[:, :side].reshape(-1, 4)
    px = px[px[:, 3] > 220][:, :3].astype(float)
    lum = px.mean(1)
    hi = px[lum >= np.percentile(lum, 55)].mean(0)   # 别取到高光，否则方块发白
    lo = px[lum <= np.percentile(lum, 25)].mean(0)
    return tuple(int(v) for v in hi), tuple(int(v) for v in lo)


@lru_cache(maxsize=1)
def mark_metrics():
    """从素材量出手绘档要用的比例和颜色，而不是把数字写死。

    手绘档（16/20/24）把记号简化成纯矩形，但「横杠多宽、竖杠多粗、耳罩多大、横杠在
    什么高度」这些比例应当跟着素材走——写死的话，换一版记号就悄悄失真了。
    """
    m = base_mark()
    a = np.array(m)
    op = a[..., 3] > 128
    h, w = op.shape
    side = max(1, round(w * 0.18))                     # 左右这一带是耳罩

    core = op[:, side:w - side]                        # 中央区里最宽的行 = T 横杠
    widths = core.sum(1)
    rows = np.nonzero(widths >= widths.max() * 0.9)[0]
    bar_rows = op[rows[0]:rows[-1] + 1, side:w - side]

    left = op[:, :side]                                # 左耳罩的行列跨度
    cols = np.nonzero(left.any(0))[0]
    lrows = np.nonzero(left.any(1))[0]

    def median_color(mask2d, x_off=0):
        ys, xs = np.nonzero(mask2d)
        px = a[ys, xs + x_off]
        px = px[px[:, 3] > 200]
        return tuple(int(v) for v in np.median(px[:, :3], axis=0)) if len(px) else (200, 200, 200)

    return dict(
        ratio=h / w,                                   # 记号高 / 宽
        bar_w=widths.max() / w,
        bar_h=(rows[-1] - rows[0] + 1) / h,
        bar_cy=(rows[0] + rows[-1]) / 2 / h,           # 横杠中线在记号高的百分之几
        stem_w=op[round(h * 0.92):, :].sum(1).max() / w,
        cup_w=(cols[-1] - cols[0] + 1) / w if len(cols) else 0.10,
        cup_h=(lrows[-1] - lrows[0] + 1) / h if len(lrows) else 0.30,
        cup_cy=(lrows[0] + lrows[-1]) / 2 / h if len(lrows) else 0.42,
        metal=median_color(bar_rows, side)[:3],        # 手绘档的实色也从素材采
        cup=median_color(left),
    )


# ================= 矢量部分：纸面 =================
def mask_new():
    return Image.new("L", (S, S), 0)


def grad(c0, c1, angle="diag"):
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    t = yy / (S - 1) if angle == "v" else xx / (S - 1) if angle == "h" else \
        (xx / (S - 1) + yy / (S - 1)) / 2
    a = np.array(c0, np.float32).reshape(1, 1, 3)
    b = np.array(c1, np.float32).reshape(1, 1, 3)
    return Image.fromarray((a + (b - a) * t[..., None]).astype(np.uint8), "RGB")


def fill(dst, mask, c0, c1=None, angle="diag"):
    dst.paste(grad(c0, c1 or c0, angle), (0, 0), mask)


def clip(mask, to):
    return Image.composite(mask, Image.new("L", (S, S), 0), to)


def doc_geom(detail):
    """文档外形。mid 档折角略大，缩到 32px 后轮廓才立得住。"""
    fold = q(240) if detail == "mid" else q(224)
    # 只有一个圆角半径：纸原本的角用它，折叠形成的交界不倒圆（见 paper_poly / fold_poly）。
    return dict(l=q(S * (1 - PAPER_W) / 2), r=q(S * (1 + PAPER_W) / 2),
                t=q(56), b=q(968), rc=q(40), fold=fold)


# ---- 圆角多边形：位图与 svg 共用同一份顶点+半径定义，两边不会各自漂移 ----
def _corner(prev, cur, nxt, r):
    """返回该顶点的 (入切点, 顶点, 出切点)。二次贝塞尔以顶点为控制点近似圆角，
    在这个尺度上与真圆弧的差别看不出来，却省掉一堆圆心/夹角计算。"""
    def unit(a, b):
        dx, dy = b[0] - a[0], b[1] - a[1]
        d = (dx * dx + dy * dy) ** 0.5 or 1.0
        return dx / d, dy / d, d

    ux1, uy1, d1 = unit(cur, prev)
    ux2, uy2, d2 = unit(cur, nxt)
    rr = min(r, d1 / 2, d2 / 2)
    return ((cur[0] + ux1 * rr, cur[1] + uy1 * rr), cur,
            (cur[0] + ux2 * rr, cur[1] + uy2 * rr))


def rounded_poly_points(pts, steps=10):
    """pts: [(x, y, r), ...]（顺时针）。返回可直接喂给 ImageDraw.polygon 的点列。"""
    out = []
    n = len(pts)
    for i in range(n):
        p1, c, p2 = _corner(pts[i - 1][:2], pts[i][:2], pts[(i + 1) % n][:2], pts[i][2])
        out.append(p1)
        for k in range(1, steps):
            t = k / steps
            out.append((((1 - t) ** 2) * p1[0] + 2 * (1 - t) * t * c[0] + t * t * p2[0],
                        ((1 - t) ** 2) * p1[1] + 2 * (1 - t) * t * c[1] + t * t * p2[1]))
        out.append(p2)
    return out


def rounded_poly_svg(pts, k=1.0):
    """同一份顶点转成 svg path（Q 命令 = 同一条二次贝塞尔）。k 是坐标缩放。"""
    def f(v):
        return round(v * k, 2)
    seg = []
    n = len(pts)
    for i in range(n):
        p1, c, p2 = _corner(pts[i - 1][:2], pts[i][:2], pts[(i + 1) % n][:2], pts[i][2])
        seg.append(("M" if i == 0 else "L") + f"{f(p1[0])} {f(p1[1])}")
        seg.append(f"Q{f(c[0])} {f(c[1])} {f(p2[0])} {f(p2[1])}")
    return " ".join(seg) + " Z"


def paper_poly(d):
    """纸形顶点（顺时针，从左上角起）。右上被折角切掉，所以那里是两个顶点。

    【圆角只属于纸原本的角】左上、右下、左下三个是裁切出来的角，倒圆；上边与斜边、
    斜边与右边这两处交界是**折叠**形成的，不该磨圆，是 135° 的尖角。
    （原本的右上角并没有消失——它被折下来了，成了折起三角的那个直角顶点，见 fold_poly。）
    """
    return [(d["l"], d["t"], d["rc"]), (d["r"] - d["fold"], d["t"], 0),
            (d["r"], d["t"] + d["fold"], 0), (d["r"], d["b"], d["rc"]),
            (d["l"], d["b"], d["rc"])]


FOLD_OVERLAP = 6        # 折角斜边向外盖过纸形轮廓的量（1024 画布上）


def fold_poly(d, overlap=FOLD_OVERLAP):
    """折起的纸背三角：**等腰直角**，直角在折痕尖端，斜边与纸形轮廓的斜边同向。

    整个三角沿斜边法向（右上）平移 overlap/√2，为的是盖过纸形轮廓自己的抗锯齿边。
    平移会让两个顶点探到纸外，但这不要紧——纸面与折角先在**不透明的 RGB 层**上合成，
    最后由纸形 mask 一次裁出轮廓（见 paper_big），纸外的部分自然被裁掉。
    早先试过让那两个顶点沿纸边滑动来避免出界，代价是三角不再是直角、折痕尖端变钝。

    【顶点的圆角】斜边两端是折叠交界，尖的（与纸形轮廓那两个顶点一致）。而直角那个顶点
    就是**原纸的右上角**被折下来的角，所以它跟其它纸角用同一个圆角半径。
    """
    k = overlap / (2 ** 0.5)
    return [(d["r"] - d["fold"] + k, d["t"] - k, 0),
            (d["r"] + k, d["t"] + d["fold"] - k, 0),
            (d["r"] - d["fold"] + k, d["t"] + d["fold"] - k, d["rc"])]




def paper_mask(d):
    """纸形轮廓（含右上被折角切掉的斜边）。全图唯一的一条 alpha 边界就来自它。"""
    m = mask_new()
    ImageDraw.Draw(m).polygon(rounded_poly_points(paper_poly(d)), fill=255)
    return m


def paper_big(detail, palette="default"):
    """深色折角纸底（1024 画布），**含折角**。返回 (img, geom)。

    折角在这里画第一遍（与纸面同层，斜边处与不透明的纸面混合，不会透出背景），
    记号之后还会再画一遍盖住记号（见 fold_big / project_icon）：折起来的是纸的背面，
    物理上它必须遮住印在其下的内容。
    """
    pal = PAPERS[palette]
    d = doc_geom(detail)
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    dm = paper_mask(d)

    # 纸面色铺满 RGB 层 → 折角直接画在上面（两种不透明色混合，边界处不涉及背景）
    # → 最后用纸形 mask 一次裁出轮廓。全图只有这一条 alpha 边界。
    base = grad(*pal["dark"])
    fm = mask_new()
    ImageDraw.Draw(fm).polygon(rounded_poly_points(fold_poly(d)), fill=255)
    base.paste(grad(*pal["fold"]), (0, 0), fm)
    img.paste(base, (0, 0), dm)
    if detail == "hi":
        # 纸面内描边：一圈极淡的受光边，让纸从深色背景里浮起来。
        # 斜边那一段要挖掉——那里的边界由折角自己负责，留着只会在 45° 上留一串锯齿亮点。
        e = dm.filter(ImageFilter.FIND_EDGES).point(lambda x: min(255, x * 4))
        e = clip(e.filter(ImageFilter.MaxFilter(3)), dm).point(lambda x: x * 50 // 255)
        fm = mask_new()
        ImageDraw.Draw(fm).polygon(rounded_poly_points(fold_poly(d, FOLD_OVERLAP + 4)), fill=255)
        e = Image.composite(Image.new("L", (S, S), 0), e, fm)
        img.paste(Image.new("RGB", (S, S), (255, 255, 255)), (0, 0), e)
    return img, d


def fold_big(detail, palette="default"):
    """折角图层（1024 画布）：折起的纸背。

    不再另画斜边高光——折角本身就比纸面亮一档，对比够了；那条线在两端还会溢出纸外，
    在折痕与纸边的交界处显脏。
    """
    pal = PAPERS[palette]
    d = doc_geom(detail)
    f = d["fold"]
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    fm = mask_new()
    ImageDraw.Draw(fm).polygon(rounded_poly_points(fold_poly(d)), fill=255)
    # alpha 裁到纸形：纸外不留像素；斜边处的 alpha 与纸形轮廓一致，底下又是不透明的纸，
    # 所以既不溢出也不透背景。
    fill(img, clip(fm, paper_mask(d)), *pal["fold"])
    return img


def px_paper(n, palette="default"):
    """手绘档的纸面，**不含折角**。返回 (img, draw, (l, t, r, b, fold))。"""
    pal = PAPERS[palette]
    img = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    dr = ImageDraw.Draw(img)
    t = max(1, round(n * 0.06))
    h = n - 2 * t
    w = round(h * 0.80)
    l = (n - w) // 2
    r, b = l + w - 1, t + h - 1
    f = max(3, round(w * 0.30))

    dr.rectangle([l, t, r, b], fill=pal["px"])
    for x, y in [(l, t), (l, b), (r, b)]:       # 三角削 1px 替代圆角（右上是折角）
        dr.point((x, y), fill=(0, 0, 0, 0))
    dr.line([(l, t + 1), (l, b - 1)], fill=pal["px_edge"])
    dr.line([(l + 1, t), (r - f, t)], fill=pal["px_edge"])
    return img, dr, (l, t, r, b, f)


def px_fold(img, geom, palette="default"):
    """手绘档的折角：斜切 + 折起的亮三角。同样最后画——它要盖住记号，
    而且斜切写的是透明像素，晚于记号才能把溢出纸外的记号一并切掉。"""
    pal = PAPERS[palette]
    l, t, r, b, f = geom
    dr = ImageDraw.Draw(img)
    for i in range(f):
        y = t + i
        dr.line([(r - f + i + 1, y), (r, y)], fill=(0, 0, 0, 0))
        if i:
            dr.line([(r - f, y), (r - f + i, y)], fill=pal["px_fold"])
    return img


def paper(n, palette="default"):
    img, _ = paper_big("hi" if n >= 64 else "mid", palette)
    return img.resize((n, n), Image.LANCZOS)


def fold(n, palette="default"):
    return fold_big("hi" if n >= 64 else "mid", palette).resize((n, n), Image.LANCZOS)


# ================= 成品：工程文件 =================
def px_project(n, palette="default"):
    """手绘档的记号：把记号简化成纯矩形（去掉唯一的曲线头梁弧），比例与配色都取自素材。"""
    img, dr, (l, t, r, b, f) = px_paper(n, palette)
    g = mark_metrics()
    iw = r - l + 1
    mw = max(5, round(iw * PX_MARK_FILL))
    mh = max(5, round(mw * g["ratio"]))
    bar_w = max(3, round(mw * g["bar_w"])) | 1
    bar_h = max(1, round(mh * g["bar_h"]))
    stem_w = max(2, round(mw * g["stem_w"]))
    cup_w = max(1, round(mw * g["cup_w"]))
    cup_h = max(3, round(mh * g["cup_h"]))

    cx = (l + r) // 2
    top = (t + b) // 2 - mh // 2
    bar_cy = top + round(mh * g["bar_cy"])
    bar_x0, bar_x1 = cx - bar_w // 2, cx + bar_w // 2
    cup_cy = top + round(mh * g["cup_cy"])
    for x in (cx - mw // 2, cx - mw // 2 + mw - cup_w):
        dr.rectangle([x, cup_cy - cup_h // 2, x + cup_w - 1, cup_cy - cup_h // 2 + cup_h - 1],
                     fill=g["cup"])
    dr.rectangle([bar_x0, bar_cy - bar_h // 2, bar_x1, bar_cy - bar_h // 2 + bar_h - 1],
                 fill=g["metal"])
    dr.rectangle([cx - stem_w // 2, bar_cy - bar_h // 2,
                  cx - stem_w // 2 + stem_w - 1, top + mh - 1], fill=g["metal"])
    return px_fold(img, (l, t, r, b, f), palette)


def project_icon(n, palette="default"):
    if n <= PX_MAX:
        return px_project(n, palette)
    base = paper(n, palette)
    mark = mark_for(n)
    base.alpha_composite(mark, (round((n - mark.width) / 2), round((n - mark.height) / 2)))
    base.alpha_composite(fold(n, palette))
    return base


# ================= 成品：扩展包 =================
BLOCK_W, BLOCK_GAP, BLOCK_CY = q(440), q(56), q(600)


def _blocks_big(img, cy=None, w=None):
    w = w or BLOCK_W
    s = (w - BLOCK_GAP) // 2
    x0, y0 = S // 2 - w // 2, (cy or BLOCK_CY) - w // 2
    blocks = mask_new()
    bd = ImageDraw.Draw(blocks)
    for ix, iy in [(0, 0), (1, 0), (0, 1)]:
        x, y = x0 + ix * (s + BLOCK_GAP), y0 + iy * (s + BLOCK_GAP)
        bd.rounded_rectangle([x, y, x + s, y + s], radius=s * 0.22, fill=255)
    fill(img, blocks, *cup_gradient())
    # 右下空位：只画轮廓，读作「还差一块」= 可安装
    x, y, bw = x0 + s + BLOCK_GAP, y0 + s + BLOCK_GAP, max(G // 4, s // 9)
    outer, inner = mask_new(), mask_new()
    ImageDraw.Draw(outer).rounded_rectangle([x, y, x + s, y + s], radius=s * 0.22, fill=255)
    ImageDraw.Draw(inner).rounded_rectangle([x + bw, y + bw, x + s - bw, y + s - bw],
                                            radius=s * 0.22, fill=255)
    fill(img, Image.composite(Image.new("L", (S, S), 0), outer, inner), GHOST)
    return img


def badge_t(n, height=None):
    """落款：从位图记号里裁出纯 T（横杠以下、两侧耳罩与弧腿之内）。整枚记号缩到落款
    这个尺寸只会读成别的东西，故只取 T。height 给定时按高度定尺寸——印在箱盖上就得
    照箱盖的高度来，按宽度算会溢出去。"""
    mark = base_mark()
    w, h = mark.width, mark.height
    crop = mark.crop((round(w * 0.22), round(h * 0.33), w - round(w * 0.22), h))
    if height:
        th = max(1, round(height))
        tw = max(1, round(crop.width * th / crop.height))
    else:
        tw = max(1, round(n * 0.15))
        th = max(1, round(crop.height * tw / crop.width))
    return crop.resize((tw, th), Image.LANCZOS)


# .tlx 是个 package（zip 里装着插件），不是文档，所以容器不该是折角纸。
# 四种造型都实现在这里，靠 EXT_STYLE 选：
#   box-front-sharp  【定稿】方角的正视闭盖盒：正面正对观者，T 印上去零形变；顶上一片
#                    向后收窄的盖交代厚度。不倒圆——这个造型硬朗才像盒子
#   box-front        同上但整体倒圆（盖与正面共用一条外轮廓，所以圆角接得上）
#   open-box         正视开盖：两片盖从中线向左右外翻，「包」的语义最明确，代价是箱体变矮
#   isometric        等距立方（尖角），T 印在朝左那一面；isometric-top 则印在盖顶
#   carton           纸箱正面 + 盖缝 + 方块阵；carton-taped 再加一条封条
#   page             最早的折角纸，语义不对（package 不是文档），留作对照
EXT_STYLE = "box-front-sharp"
ISO_HH = 130            # 等距顶面的半高：越小视角越低、侧面斜切越浅
ISO_ROUND = 0           # 立方体外轮廓的圆角半径；0 = 尖角（这个造型不带圆角更好）
# 一点透视的正面盒：正面矩形（T 印上去零形变）+ 顶部一片向后收的梯形表示厚度
BOXF = dict(l=q(112), r=q(912), t=q(344), b=q(880), rc=q(56), lid_h=q(160), inset=q(104))
CARTON = dict(l=q(96), r=q(928), t=q(160), b=q(864), rc=q(72), seam=q(320),
              blocks=q(380))    # 方块阵比容器小一圈，四周留出呼吸空间


def _carton_big(taped=False):
    """正面纸箱（1024 画布）。箱盖比箱体亮一档，盖缝是一道暗线。"""
    pal = PAPERS["default"]
    c = CARTON
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    body = mask_new()
    ImageDraw.Draw(body).rounded_rectangle([c["l"], c["t"], c["r"], c["b"]], radius=c["rc"], fill=255)
    fill(img, body, *pal["dark"])
    lid = mask_new()
    ImageDraw.Draw(lid).rectangle([0, 0, S, c["seam"]], fill=255)
    fill(img, clip(lid, body), *pal["fold"])
    seam = mask_new()
    ImageDraw.Draw(seam).rectangle([0, c["seam"] - q(8), S, c["seam"] + q(8)], fill=255)
    fill(img, clip(seam, body), (26, 28, 37))
    if taped:
        tape = mask_new()
        ImageDraw.Draw(tape).rectangle([S // 2 - q(48), c["t"], S // 2 + q(48), c["seam"]], fill=255)
        fill(img, clip(tape, body), *cup_gradient())
    # 受光边
    e = body.filter(ImageFilter.FIND_EDGES).point(lambda x: min(255, x * 4))
    e = clip(e.filter(ImageFilter.MaxFilter(3)), body).point(lambda x: x * 50 // 255)
    img.paste(Image.new("RGB", (S, S), (255, 255, 255)), (0, 0), e)
    return _blocks_big(img, cy=(c["seam"] + c["b"]) // 2, w=c["blocks"])


def _boxfront_big(rounded=True):
    """一点透视的正面盒子：正面正对观者，所以 T 印上去零形变；顶上一片向后收窄的盖。

    【盖子与正面为什么会贴不上】两片各自倒圆的话，正面顶角往内收、盖子下边却是直的，
    轮廓就错开了。解法同立方体：把盖子和正面当**一条外轮廓**（盖子上边 → 斜边 → 正面
    两侧 → 底边，共六个顶点）整体倒圆，再用 y = t 这条线把它切成盖与面。轮廓天生连续，
    接缝只是一条明暗硬边。rounded=False 则整体尖角（这个造型不倒圆也成立）。
    """
    pal = PAPERS["default"]
    c = BOXF
    r = c["rc"] if rounded else 0
    outline = mask_new()
    ImageDraw.Draw(outline).polygon(rounded_poly_points([
        (c["l"] + c["inset"], c["t"] - c["lid_h"], r * 0.72),
        (c["r"] - c["inset"], c["t"] - c["lid_h"], r * 0.72),
        (c["r"], c["t"], r), (c["r"], c["b"], r),
        (c["l"], c["b"], r), (c["l"], c["t"], r)]), fill=255)

    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    face = mask_new()
    ImageDraw.Draw(face).rectangle([0, c["t"], S, S], fill=255)
    fill(img, clip(face, outline), *pal["dark"])
    lid = mask_new()
    ImageDraw.Draw(lid).rectangle([0, 0, S, c["t"] + 2], fill=255)   # 多盖 2px，接缝不透底
    fill(img, clip(lid, outline), *pal["fold"])

    e = outline.filter(ImageFilter.FIND_EDGES).point(lambda x: min(255, x * 4))
    e = clip(e.filter(ImageFilter.MaxFilter(3)), outline).point(lambda x: x * 46 // 255)
    img.paste(Image.new("RGB", (S, S), (255, 255, 255)), (0, 0), e)
    return img


OPENBOX = dict(l=q(176), r=q(848), t=q(448), b=q(880), rc=q(40),
               gap=q(20),        # 中线处两片盖之间的开口
               dx=q(150),        # 盖向外展开的水平量
               dy=q(216))        # 盖翻起的高度


def _openbox_big():
    """正视的开盖纸箱：箱体是正对观者的矩形（T 印上去零形变），顶上两片盖从中线
    向左右外翻。盖先画、箱体后画——箱体正好压住盖的铰链边，接缝不用另做处理。
    盖的内端比外端翻得更高，才读得出是「掀开」而不是「贴着」。
    """
    pal = PAPERS["default"]
    c = OPENBOX
    cx = S // 2
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))

    for sign in (-1, 1):                     # -1 左盖，1 右盖
        inner_x = cx + sign * c["gap"]
        outer_x = c["r"] if sign > 0 else c["l"]
        pts = [(inner_x, c["t"], c["rc"] * 0.5),
               (outer_x, c["t"], c["rc"] * 0.5),
               (outer_x + sign * c["dx"], c["t"] - c["dy"], c["rc"] * 0.8),
               (inner_x + sign * c["dx"] * 0.42, c["t"] - c["dy"] * 1.12, c["rc"] * 0.8)]
        if sign < 0:
            pts = pts[::-1]                  # 保持顺时针，圆角方向才对
        m = mask_new()
        ImageDraw.Draw(m).polygon(rounded_poly_points(pts), fill=255)
        fill(img, m, *pal["fold"])
        # 盖板内侧的暗边：让两片盖分得开，也交代厚度
        e = m.filter(ImageFilter.FIND_EDGES).point(lambda x: min(255, x * 4))
        e = clip(e.filter(ImageFilter.MaxFilter(3)), m).point(lambda x: x * 70 // 255)
        img.paste(Image.new("RGB", (S, S), (26, 27, 36)), (0, 0), e)

    body = mask_new()
    ImageDraw.Draw(body).rounded_rectangle([c["l"], c["t"], c["r"], c["b"]],
                                           radius=c["rc"], fill=255)
    fill(img, body, *pal["dark"])
    # 箱口：顶边内侧一条暗带，表示箱子是敞开的
    mouth = mask_new()
    ImageDraw.Draw(mouth).rectangle([0, c["t"], S, c["t"] + q(28)], fill=255)
    fill(img, clip(mouth, body), (20, 21, 28))
    e = body.filter(ImageFilter.FIND_EDGES).point(lambda x: min(255, x * 4))
    e = clip(e.filter(ImageFilter.MaxFilter(3)), body).point(lambda x: x * 46 // 255)
    img.paste(Image.new("RGB", (S, S), (255, 255, 255)), (0, 0), e)
    return img


def _isometric_big(t_face="left"):
    """等距立方（1024 画布）。

    立方体默认是**尖角**（ISO_ROUND=0）：这个造型不带圆角本来就挺好，硬朗才像盒子。

    【真要倒圆的话】不能三个面各自倒圆——相邻面在棱上会各自往回缩，棱角处就露出背景，
    圆角矩形拼不成圆角立方体。得反过来：先把整个立方体的**外轮廓**（一个六边形）整体
    倒圆，再用从三棱交点出发的三条射线把它切成三个面，各面与轮廓求交。于是外角圆润且
    不可能漏，棱仍是该有的硬边。把 ISO_ROUND 调成非零就走这条路。
    相邻面沿棱互相多盖几像素：同一图层上后画的覆盖先画的，缝就不会从两层抗锯齿里透出来。
    """
    pal = PAPERS["default"]
    cx, top_y = S // 2, q(272)
    hw, hh, bh, r = q(380), q(ISO_HH), q(300), ISO_ROUND

    top = (cx, top_y - hh)                   # 六个外顶点，顺时针
    ru, rd = (cx + hw, top_y), (cx + hw, top_y + bh)
    bottom = (cx, top_y + hh + bh)
    ld, lu = (cx - hw, top_y + bh), (cx - hw, top_y)
    ctr = (cx, top_y + hh)                   # 三棱交点

    outline = mask_new()
    ImageDraw.Draw(outline).polygon(
        rounded_poly_points([(*top, r), (*ru, r), (*rd, r), (*bottom, r), (*ld, r), (*lu, r)]),
        fill=255)

    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    faces = [([top, ru, ctr, lu], pal["fold"]),          # 顶面最亮
             ([lu, ctr, bottom, ld], pal["dark"]),       # 左面
             ([ru, rd, bottom, ctr], ((34, 36, 48), (19, 20, 26)))]   # 右面背光
    for pts, cols in faces:
        m = mask_new()
        ImageDraw.Draw(m).polygon(_grow(pts, 6), fill=255)
        fill(img, clip(m, outline), *cols)

    # 受光边：沿外轮廓一圈极淡的白，和纸面用的是同一手法
    e = outline.filter(ImageFilter.FIND_EDGES).point(lambda x: min(255, x * 4))
    e = clip(e.filter(ImageFilter.MaxFilter(3)), outline).point(lambda x: x * 42 // 255)
    img.paste(Image.new("RGB", (S, S), (255, 255, 255)), (0, 0), e)

    # T 往哪个面上印：
    #   left —— 朝左那一面只有一个方向被斜切，视角放低（ISO_HH 小）后斜切更浅；
    #   top  —— 盖顶两个方向都斜，形变更重（容易读成「倒的 L」），但位置最显眼。
    t_img = badge_t(S)
    if t_face == "top":
        u_full, v_full = (hw, -hh), (hw, hh)     # 顶面的两条边
        u0, u1, v0, v1 = 0.20, 0.80, 0.22, 0.78
        p0 = (lu[0] + u0 * u_full[0] + v0 * v_full[0],
              lu[1] + u0 * u_full[1] + v0 * v_full[1])
        du, dv = v1 - v0, u1 - u0
        U = (dv * v_full[0] / t_img.width, dv * v_full[1] / t_img.width)
        V = (du * u_full[0] / t_img.height, du * u_full[1] / t_img.height)
    else:
        u0, u1, v0, v1 = 0.24, 0.76, 0.28, 0.72
        p0 = (lu[0] + u0 * hw, lu[1] + u0 * hh + v0 * bh)
        U = ((u1 - u0) * hw / t_img.width, (u1 - u0) * hh / t_img.width)
        V = (0.0, (v1 - v0) * bh / t_img.height)
    img.alpha_composite(_affine_onto(t_img, p0, U, V))
    return img


def _affine_onto(mark, p0, U, V):
    """把 mark 铺到由 p0 + X·U + Y·V 张成的平行四边形上（等距的面就是这种形状）。
    Image.AFFINE 收的是目标到源的逆变换，所以这里解的是那个 2x2 的逆矩阵。"""
    det = U[0] * V[1] - U[1] * V[0]
    a, b = V[1] / det, -V[0] / det
    d_, e = -U[1] / det, U[0] / det
    c = -(a * p0[0] + b * p0[1])
    f = -(d_ * p0[0] + e * p0[1])
    return mark.transform((S, S), Image.AFFINE, (a, b, c, d_, e, f), resample=Image.BICUBIC)


def _grow(pts, d):
    """多边形顶点沿「远离重心」的方向各外移 d 像素。面互相多盖一点，棱上就不会有缝。"""
    gx = sum(p[0] for p in pts) / len(pts)
    gy = sum(p[1] for p in pts) / len(pts)
    out = []
    for x, y in pts:
        dx, dy = x - gx, y - gy
        n = (dx * dx + dy * dy) ** 0.5 or 1.0
        out.append((x + dx / n * d, y + dy / n * d))
    return out


def _px_boxfront(n):
    """手绘档的正视闭盖盒：方角箱体 + 顶部一条略窄的盖 + 正面中央一个纯 T。
    T 的比例同样取自素材（mark_metrics 量出的横杠/竖杠占比，换算到落款裁片上）。"""
    img = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    dr = ImageDraw.Draw(img)
    pal = PAPERS["default"]
    g = mark_metrics()
    m = max(1, round(n * 0.08))
    l, r = m, n - 1 - m
    lid_h = max(1, round(n * 0.13))
    t = max(1 + lid_h, round(n * 0.20))
    b = n - 1 - max(1, round(n * 0.08))
    inset = max(1, round(n * 0.07))

    dr.rectangle([l + inset, t - lid_h, r - inset, t - 1], fill=pal["px_fold"])   # 盖
    dr.rectangle([l, t, r, b], fill=pal["px"])                                    # 正面

    fh = b - t + 1
    th = max(4, round(fh * 0.54))
    tw = max(3, round(th * 0.75)) | 1
    bar_h = max(1, round(th * g["bar_h"] / (g["ratio"] * 0.67)))
    stem_w = max(1, round(tw * g["stem_w"] / g["bar_w"]))
    cx = (l + r) // 2
    ty = t + (fh - th) // 2
    dr.rectangle([cx - tw // 2, ty, cx - tw // 2 + tw - 1, ty + bar_h - 1], fill=g["metal"])
    dr.rectangle([cx - stem_w // 2, ty, cx - stem_w // 2 + stem_w - 1, ty + th - 1],
                 fill=g["metal"])
    return img


def _px_carton(n, taped=False):
    """手绘档的纸箱：方角容器 + 顶部盖带 + 盖缝 + 方块阵。
    等距立方在这个尺寸画不出来，故 isometric 样式在手绘档退化成同一个纸箱。"""
    img = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    dr = ImageDraw.Draw(img)
    pal = PAPERS["default"]
    m = max(1, round(n * 0.09))
    l, r = m, n - 1 - m
    t, b = max(1, round(n * 0.13)), n - 1 - max(1, round(n * 0.13))
    seam = t + max(1, round((b - t) * 0.30))
    dr.rectangle([l, t, r, b], fill=pal["px"])
    dr.rectangle([l, t, r, seam - 1], fill=pal["px_fold"])       # 箱盖
    for x, y in [(l, t), (r, t), (l, b), (r, b)]:                # 四角削 1px
        dr.point((x, y), fill=(0, 0, 0, 0))
    if taped and r - l >= 8:
        cx = (l + r) // 2
        dr.rectangle([cx - 1, t, cx, seam - 1], fill=mark_metrics()["cup"])
    gap = max(1, round(n * 0.05))
    s = max(2, (round((r - l + 1) * 0.58) - gap) // 2)
    w = s * 2 + gap
    x0 = (l + r) // 2 - w // 2
    y0 = (seam + b) // 2 - w // 2
    for ix, iy in [(0, 0), (1, 0), (0, 1)]:
        x, y = x0 + ix * (s + gap), y0 + iy * (s + gap)
        dr.rectangle([x, y, x + s - 1, y + s - 1], fill=mark_metrics()["cup"])
    x, y = x0 + s + gap, y0 + s + gap
    dr.rectangle([x, y, x + s - 1, y + s - 1], outline=GHOST, width=1)
    return img


def extension_icon(n, style=None):
    style = style or EXT_STYLE
    if style == "page":                     # 旧造型：折角纸 + 方块阵 + 落款
        if n <= PX_MAX:
            img, dr, (l, t, r, b, f) = px_paper(n)
            gap = max(1, round(n * 0.055))
            s = max(2, (round((r - l + 1) * 0.62) - gap) // 2)
            w = s * 2 + gap
            x0, y0 = (l + r) // 2 - w // 2, t + round((b - t + 1) * 0.54) - w // 2
            for ix, iy in [(0, 0), (1, 0), (0, 1)]:
                x, y = x0 + ix * (s + gap), y0 + iy * (s + gap)
                dr.rectangle([x, y, x + s - 1, y + s - 1], fill=mark_metrics()["cup"])
            x, y = x0 + s + gap, y0 + s + gap
            dr.rectangle([x, y, x + s - 1, y + s - 1], outline=GHOST, width=1)
            return px_fold(img, (l, t, r, b, f))
        img, _ = paper_big("hi" if n >= 64 else "mid")
        out = _blocks_big(img).resize((n, n), Image.LANCZOS)
        if n >= 128:
            out.alpha_composite(badge_t(n), (round(n * 0.145), round(n * 0.10)))
        out.alpha_composite(fold(n))
        return out

    if n <= PX_MAX:
        if style.startswith("box-front") or style == "open-box":
            return _px_boxfront(n)
        return _px_carton(n, taped=style == "carton-taped")
    big = (_isometric_big() if style == "isometric" else
           _isometric_big("top") if style == "isometric-top" else
           _openbox_big() if style == "open-box" else
           _boxfront_big(True) if style == "box-front" else
           _boxfront_big(False) if style == "box-front-sharp" else
           _carton_big(style == "carton-taped"))
    out = big.resize((n, n), Image.LANCZOS)
    if style == "open-box":
        c = OPENBOX
        fh = (c["b"] - c["t"]) / S * n
        b = badge_t(n, height=fh * 0.52)
        cy = (c["t"] + q(28) + c["b"]) / 2 / S * n     # 让开的箱口，重心略往下
        out.alpha_composite(b, (round((n - b.width) / 2), round(cy - b.height / 2)))
        return out
    if style.startswith("box-front"):       # 正面居中印 T，零形变
        c = BOXF
        fh = (c["b"] - c["t"]) / S * n
        b = badge_t(n, height=fh * 0.52)
        cy = (c["t"] + c["b"]) / 2 / S * n
        out.alpha_composite(b, (round((n - b.width) / 2), round(cy - b.height / 2)))
        return out
    if n >= 128 and style == "carton":
        # 落款印在箱盖正中，像包装上的品牌标。isometric 没有正对观者的平面；
        # carton-taped 的箱盖中央已经被封条占了，两者都不画。
        lid_h = (CARTON["seam"] - CARTON["t"]) / S * n
        b = badge_t(n, height=lid_h * 0.52)
        lid_cy = (CARTON["t"] + CARTON["seam"]) / 2 / S * n
        out.alpha_composite(b, (round((n - b.width) / 2), round(lid_cy - b.height / 2)))
    return out


# ================= 打包 =================
ICONS = {
    "TuneLabProject":     lambda n: project_icon(n, "default"),
    "TuneLabProjectJson": lambda n: project_icon(n, "json"),
    "TuneLabExtension":   extension_icon,
}


def render(name):
    return {n: ICONS[name](n) for n in OUT_SIZES}


def save_ico(path, frames):
    """手工组装多帧 ICO：每帧存 PNG（Vista+ 支持），Pillow 自身只会从单图缩放。"""
    blobs = []
    for size in sorted(frames):
        buf = io.BytesIO()
        frames[size].save(buf, format="PNG", optimize=True)
        blobs.append((size, buf.getvalue()))
    offset = 6 + 16 * len(blobs)
    dirs, data = b"", b""
    for size, blob in blobs:
        dim = 0 if size >= 256 else size        # 256 在 ICO 目录里记作 0
        dirs += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(blob), offset)
        offset += len(blob)
        data += blob
    with open(path, "wb") as f:
        f.write(struct.pack("<HHH", 0, 1, len(blobs)) + dirs + data)


# ================= svg 源 =================
def svg_paper_path(d, k=1.0):
    return rounded_poly_svg(paper_poly(d), k)


def svg_fold_path(d, k=1.0):
    return rounded_poly_svg(fold_poly(d), k)


def data_uri(img):
    buf = io.BytesIO()
    img.save(buf, format="PNG", optimize=True)
    return "data:image/png;base64," + base64.b64encode(buf.getvalue()).decode()


def hexc(c):
    return "#%02X%02X%02X" % c


# svg 视口取 256 而不是 1024：源记号只有 256px，视口再大也只是把同一份位图放大，
# 白白让文件里多几百 KB 的模糊像素。256 下「1 单位 = 1 源像素」，记号与 ico 的 256 帧逐像素一致。
SVG_V = 256
K = SVG_V / S            # 矢量几何的缩放系数


SVG_NOTE = ("  <!-- 本文件由 tools/icons/gen-file-icons.py 生成，请勿手改；改形改色改脚本。\n"
            "       容器是矢量；记号是从素材来的位图（256px 原分辨率，与 ico 的 256 帧逐像素\n"
            "       一致），所以 svg 不会与 ico 脱节。 -->\n")


def _svg_gradients(pal):
    """svg 头 + 三个共用渐变。容器形状各造型自己画。"""
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {SVG_V} {SVG_V}" '
            f'width="{SVG_V}" height="{SVG_V}">\n' + SVG_NOTE + '  <defs>\n'
            + "".join(
                f'    <linearGradient id="{name}" gradientUnits="userSpaceOnUse" '
                f'x1="0" y1="0" x2="{SVG_V}" y2="{SVG_V}">'
                f'<stop offset="0" stop-color="{hexc(c0)}"/>'
                f'<stop offset="1" stop-color="{hexc(c1)}"/></linearGradient>\n'
                for name, (c0, c1) in [("dark", pal["dark"]), ("fold", pal["fold"]),
                                       ("cup", cup_gradient())])
            + '  </defs>\n\n')


def _svg_project(pal):
    """折角纸 + 记号 + 折角（折角在内容之后，与位图侧同一个顺序）。"""
    d = {k: round(v * K, 2) for k, v in doc_geom("hi").items()}
    mark = mark_for(SVG_V)
    return (f'  <defs><path id="paper" d="{svg_paper_path(d)}"/>'
            f'<clipPath id="clipPaper"><use href="#paper"/></clipPath>'
            f'</defs>\n'
            f'  <use href="#paper" fill="url(#dark)"/>\n'
            f'  <use href="#paper" fill="none" stroke="#FFFFFF" stroke-opacity="0.196" '
            f'stroke-width="{round(6 * K, 2)}" clip-path="url(#clipPaper)"/>\n'
            # 折角画两遍：这一遍与纸面同层，记号之后再来一遍盖住记号。位图侧同一策略。
            f'  <path d="{svg_fold_path(d)}" fill="url(#fold)" clip-path="url(#clipPaper)"/>\n'
            f'  <image x="{round((SVG_V - mark.width) / 2)}" '
            f'y="{round((SVG_V - mark.height) / 2)}" width="{mark.width}" '
            f'height="{mark.height}" href="{data_uri(mark)}"/>\n'
            f'  <path d="{svg_fold_path(d)}" fill="url(#fold)" clip-path="url(#clipPaper)"/>\n')


def _svg_boxfront():
    """正视闭盖盒：盖与正面共用一条外轮廓，再按 y = t 切成两块（同位图侧的做法）。"""
    c = BOXF
    r = c["rc"] if EXT_STYLE == "box-front" else 0
    outline = rounded_poly_svg([
        (c["l"] + c["inset"], c["t"] - c["lid_h"], r * 0.72),
        (c["r"] - c["inset"], c["t"] - c["lid_h"], r * 0.72),
        (c["r"], c["t"], r), (c["r"], c["b"], r),
        (c["l"], c["b"], r), (c["l"], c["t"], r)], K)
    top, seam, bot = (c["t"] - c["lid_h"]) * K, c["t"] * K, c["b"] * K
    badge = badge_t(SVG_V, height=(c["b"] - c["t"]) * K * 0.52)
    return (f'  <defs><clipPath id="clipBox"><path d="{outline}"/></clipPath></defs>\n'
            f'  <g clip-path="url(#clipBox)">\n'
            f'    <rect x="0" y="{round(seam, 2)}" width="{SVG_V}" '
            f'height="{round(bot - seam + 1, 2)}" fill="url(#dark)"/>\n'
            f'    <rect x="0" y="{round(top - 1, 2)}" width="{SVG_V}" '
            f'height="{round(seam - top + 1, 2)}" fill="url(#fold)"/>\n'
            f'  </g>\n'
            f'  <path d="{outline}" fill="none" stroke="#FFFFFF" stroke-opacity="0.18" '
            f'stroke-width="{round(6 * K, 2)}" clip-path="url(#clipBox)"/>\n'
            f'  <image x="{round((SVG_V - badge.width) / 2)}" '
            f'y="{round((seam + bot) / 2 - badge.height / 2)}" width="{badge.width}" '
            f'height="{badge.height}" href="{data_uri(badge)}"/>\n')


def write_svg(out_path, kind, palette="default"):
    """svg 源：容器是矢量，记号是嵌进来的位图（与 ico 同一份，不可能与产物脱节）。

    扩展包按当前 EXT_STYLE 出图。换造型时若这里没有对应实现就会报错——宁可报错，
    也不要默默写出一个与 ico 不一样的 svg。
    """
    pal = PAPERS[palette]
    if kind == "project":
        body = _svg_project(pal)
    elif EXT_STYLE.startswith("box-front"):
        body = _svg_boxfront()
    else:
        raise NotImplementedError(
            f"no svg path for EXT_STYLE={EXT_STYLE!r}; add one next to _svg_boxfront()")
    with open(out_path, "w", encoding="utf-8") as f:
        f.write(_svg_gradients(pal) + body + "</svg>\n")


SVGS = {"TuneLabProject": ("project", "default"), "TuneLabProjectJson": ("project", "json"),
        "TuneLabExtension": ("ext", "default")}


# ================= 预览 =================
def preview(path, rendered, show=(256, 128, 48, 32, 24, 16)):
    """多尺寸 × 浅/深底对照图。小尺寸按 NEAREST 放大，便于逐像素检查。"""
    pad, gap, cell, label_w = 26, 20, 256, 150
    row_h = cell + 46
    width = label_w + pad * 2 + (sum(min(cell, max(s, 112)) + gap for s in show) + 34) * 2
    sheet = Image.new("RGB", (width, pad * 2 + row_h * len(rendered)), (255, 255, 255))
    dr = ImageDraw.Draw(sheet)
    for row, (name, frames) in enumerate(rendered.items()):
        y = pad + row * row_h
        dr.text((pad, y + cell // 2), name, fill=(20, 20, 20))
        x = label_w + pad
        for bg in [(243, 243, 245), (32, 32, 36)]:
            for s in show:
                box = min(cell, max(s, 112))
                dr.rectangle([x, y, x + box, y + box], fill=bg)
                im = frames[s]
                im = im.resize((box, box), Image.NEAREST) if s < box else im
                sheet.paste(im, (x, y), im)
                dr.text((x, y + box + 8), f"{s}px", fill=(90, 90, 96))
                x += box + gap
            x += 34
    sheet.save(path)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=ASSETS, help="output directory for .ico files")
    ap.add_argument("--preview", help="also write a multi-size comparison sheet here")
    ap.add_argument("--no-svg", action="store_true", help="skip regenerating the svg sources")
    ap.add_argument("--ext-style", choices=["carton", "carton-taped", "isometric", "isometric-top",
                             "box-front", "box-front-sharp", "open-box", "page"],
                    help="override the .tlx container shape (default: EXT_STYLE)")
    args = ap.parse_args()

    if args.ext_style:
        globals()["EXT_STYLE"] = args.ext_style
    os.makedirs(args.out, exist_ok=True)
    rendered = {}
    for name in ICONS:
        frames = render(name)
        rendered[name] = frames
        path = os.path.join(args.out, name + ".ico")
        save_ico(path, frames)
        print(f"wrote {path} ({os.path.getsize(path)} bytes, {len(frames)} frames)")
    if not args.no_svg:
        for name, (kind, palette) in SVGS.items():
            path = os.path.join(SVG_DIR, name + ".svg")
            write_svg(path, kind, palette)
            print(f"wrote {path}")
    if args.preview:
        preview(args.preview, rendered)
        print(f"wrote {args.preview}")


if __name__ == "__main__":
    main()
