# Generate WallpaperChanger.ico (multi-size PNG-based ICO)
# Theme: dark blue-purple gradient, white rounded picture frame,
#        orange sun and layered mountains inside - a "wallpaper" glyph.
from PIL import Image, ImageDraw

def make(size):
    img = Image.new("RGB", (size, size))
    d = ImageDraw.Draw(img)
    # vertical gradient background (deep blue -> purple)
    for y in range(size):
        t = y / max(1, size - 1)
        r = int(14 + 52 * t)
        g = int(30 + 18 * t)
        b = int(84 + 66 * t)
        d.line([(0, y), (size, y)], fill=(r, g, b))

    # frame metrics (relative)
    m = size * 0.10          # outer margin of frame
    fm = size * 0.16         # frame thickness
    x0, y0 = m, m
    x1, y1 = size - m, size - m
    rad = size * 0.07
    # white rounded frame (draw big white rounded rect, then punch inner)
    d.rounded_rectangle([x0, y0, x1, y1], radius=rad, fill=(255, 255, 255))
    d.rounded_rectangle([x0 + fm, y0 + fm, x1 - fm, y1 - fm], radius=max(2, rad - fm),
                        fill=(r, g, b))  # inner matches gradient midpoint approx

    # inner canvas coords
    ix0, iy0 = x0 + fm, y0 + fm
    ix1, iy1 = x1 - fm, y1 - fm
    iw, ih = ix1 - ix0, iy1 - iy0

    # sky inside frame: slightly lighter gradient
    for y in range(int(iy0), int(iy1)):
        t = (y - iy0) / max(1, ih - 1)
        r2 = int(20 + 46 * t)
        g2 = int(44 + 16 * t)
        b2 = int(100 + 62 * t)
        d.line([(ix0, y), (ix1, y)], fill=(r2, g2, b2))

    # sun (top-left, warm orange)
    sun_r = iw * 0.10
    sun_x, sun_y = ix0 + iw * 0.24, iy0 + ih * 0.26
    d.ellipse([sun_x - sun_r, sun_y - sun_r, sun_x + sun_r, sun_y + sun_r],
              fill=(255, 176, 64))

    # far mountain (darker purple-blue)
    far = [(ix0, iy1),
           (ix0 + iw * 0.30, iy0 + ih * 0.46),
           (ix0 + iw * 0.52, iy1)]
    d.polygon(far, fill=(58, 62, 128))

    # near mountain (dark)
    near = [(ix0 + iw * 0.30, iy1),
            (ix0 + iw * 0.58, iy0 + ih * 0.58),
            (ix0 + iw * 0.86, iy1)]
    d.polygon(near, fill=(34, 34, 72))

    return img

sizes = [16, 24, 32, 48, 64, 128, 256]
images = [make(s) for s in sizes]
# main image must be the largest; append the rest
images[-1].save(r"E:\workbuddy\windows系统优化\WallpaperChanger\icon.ico",
                format="ICO", sizes=[(s, s) for s in sizes],
                append_images=images[:-1])
print("icon.ico written, sizes:", sizes)
