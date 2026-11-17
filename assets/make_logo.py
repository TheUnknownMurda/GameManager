"""Logo « Game Manager » : manette blanche dont la poignée droite se désagrège en pixels
(= désinstaller / faire de la place), sur une tuile « squircle » en dégradé cyan → indigo → violet."""
import math
import sys
from PIL import Image, ImageChops, ImageDraw, ImageFilter

S = 1024
OUT = sys.argv[1] if len(sys.argv) > 1 else "."


def lerp(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(len(a)))


def gradient(stops):
    """Dégradé diagonal (haut-gauche → bas-droite) à plusieurs arrêts."""
    small = 256
    img = Image.new("RGB", (small, small))
    px = img.load()
    for y in range(small):
        for x in range(small):
            t = (x * 0.55 + y * 0.45) / (small - 1)
            for i in range(len(stops) - 1):
                p0, c0 = stops[i]
                p1, c1 = stops[i + 1]
                if t <= p1 or i == len(stops) - 2:
                    u = min(1.0, max(0.0, (t - p0) / (p1 - p0)))
                    px[x, y] = lerp(c0, c1, u)
                    break
    return img.resize((S, S), Image.BICUBIC)


def squircle_mask(margin=40, n=5.0):
    small = 512
    m = Image.new("L", (small, small), 0)
    px = m.load()
    a = (small - 2 * margin * small / S) / 2
    c = small / 2
    for y in range(small):
        for x in range(small):
            dx, dy = abs(x + 0.5 - c) / a, abs(y + 0.5 - c) / a
            if dx ** n + dy ** n <= 1:
                px[x, y] = 255
    return m.resize((S, S), Image.LANCZOS)


def pill(w, h, angle):
    layer = Image.new("L", (S, S), 0)
    cx, cy = S // 2, S // 2
    ImageDraw.Draw(layer).rounded_rectangle([cx - w // 2, cy - h // 2, cx + w // 2, cy + h // 2], radius=min(w, h) // 2, fill=255)
    return layer.rotate(angle, resample=Image.BICUBIC)


def place(layer, x, y):
    """Déplace un calque centré en (S/2, S/2) vers (x, y)."""
    out = Image.new("L", (S, S), 0)
    out.paste(layer, (x - S // 2, y - S // 2))
    return out


# ---------- Silhouette de la manette ----------
ctrl = Image.new("L", (S, S), 0)
d = ImageDraw.Draw(ctrl)
d.rounded_rectangle([196, 352, 828, 632], radius=140, fill=255)
ctrl = ImageChops.lighter(ctrl, place(pill(214, 340, -24), 330, 640))
ctrl = ImageChops.lighter(ctrl, place(pill(214, 340, 24), 694, 640))

# Découpes : croix directionnelle et 4 boutons (on voit le dégradé à travers)
d = ImageDraw.Draw(ctrl)
dx, dy, arm, th = 352, 482, 104, 40
d.rounded_rectangle([dx - arm, dy - th, dx + arm, dy + th], radius=16, fill=0)
d.rounded_rectangle([dx - th, dy - arm, dx + th, dy + arm], radius=16, fill=0)
bx, by, off, r = 672, 482, 64, 31
for ox, oy in [(0, -off), (off, 0), (0, off), (-off, 0)]:
    d.ellipse([bx + ox - r, by + oy - r, bx + ox + r, by + oy + r], fill=0)

# ---------- Désagrégation en pixels (poignée droite) ----------
g = 48                     # taille d'un « pixel »
origin_x, origin_y = 16, 8
T = 1400                   # au-delà de x + y > T, la poignée part en pixels
band = int(g * 1.5)
cells = Image.new("L", (S, S), 0)
cd = ImageDraw.Draw(cells)
src = ctrl.copy()
for gy in range(origin_y, S, g):
    for gx in range(origin_x, S, g):
        cx, cy = gx + g / 2, gy + g / 2
        if cy < 560 or cx < 600:
            continue
        s = cx + cy
        if s > T:
            ImageDraw.Draw(ctrl).rectangle([gx, gy, gx + g - 1, gy + g - 1], fill=0)
        elif s > T - band:
            # bord en escalier : cellule pleine si son centre était dans la manette
            inside = src.getpixel((int(cx), int(cy))) > 128
            ImageDraw.Draw(ctrl).rectangle([gx, gy, gx + g - 1, gy + g - 1], fill=0)
            if inside:
                cd.rounded_rectangle([gx + 3, gy + 3, gx + g - 4, gy + g - 4], radius=7, fill=255)
ctrl = ImageChops.lighter(ctrl, cells)

# Pixels qui s'envolent vers le haut à droite
flying = [  # (x, y, taille, opacité)
    (812, 704, 40, 230),
    (868, 616, 32, 185),
    (910, 540, 25, 140),
    (938, 472, 18, 100),
    (858, 520, 14, 80),
]
fly = Image.new("L", (S, S), 0)
fd = ImageDraw.Draw(fly)
for x, y, size, alpha in flying:
    fd.rounded_rectangle([x - size // 2, y - size // 2, x + size // 2, y + size // 2], radius=max(3, size // 6), fill=alpha)

# ---------- Composition ----------
bg = gradient([(0.0, (34, 211, 238)), (0.48, (99, 102, 241)), (1.0, (168, 85, 247))]).convert("RGBA")

# Lumière douce en haut à gauche
glow = Image.new("L", (S, S), 0)
ImageDraw.Draw(glow).ellipse([-260, -320, 640, 520], fill=90)
glow = glow.filter(ImageFilter.GaussianBlur(120))
bg = Image.composite(Image.new("RGBA", (S, S), (255, 255, 255, 255)), bg, glow)

# Ombre portée de la manette
shadow_src = ImageChops.lighter(ctrl, fly)
shadow = Image.new("L", (S, S), 0)
shadow.paste(shadow_src, (0, 22))
shadow = shadow.filter(ImageFilter.GaussianBlur(26)).point(lambda v: int(v * 0.32))
bg = Image.composite(Image.new("RGBA", (S, S), (24, 18, 72, 255)), bg, shadow)

white = Image.new("RGBA", (S, S), (255, 255, 255, 255))
bg = Image.composite(white, bg, ctrl)
bg = Image.composite(white, bg, fly)

tile = Image.new("RGBA", (S, S), (0, 0, 0, 0))
tile.paste(bg, (0, 0), squircle_mask())

tile.save(f"{OUT}/logo-1024.png")
for size in (512, 256, 128, 64):
    tile.resize((size, size), Image.LANCZOS).save(f"{OUT}/logo-{size}.png")

def write_ico(path, image, sizes):
    """ICO classique : images BMP 32 bits (avec masque) jusqu'à 128 px, PNG pour 256 px.
    Les ICO « tout PNG » de Pillow s'affichent mal dans certains composants Windows."""
    import io
    import struct
    frames = []
    for size in sizes:
        img = image.resize((size, size), Image.LANCZOS).convert("RGBA")
        if size >= 256:
            buf = io.BytesIO()
            img.save(buf, format="PNG")
            frames.append((size, buf.getvalue()))
            continue
        header = struct.pack("<IiiHHIIiiII", 40, size, size * 2, 1, 32, 0, 0, 0, 0, 0, 0)
        px = img.load()
        xor = bytearray()
        for y in range(size - 1, -1, -1):  # lignes de bas en haut, BGRA
            for x in range(size):
                r, g, b, a = px[x, y]
                xor += bytes((b, g, r, a))
        row_bytes = ((size + 31) // 32) * 4
        and_mask = bytearray()
        for y in range(size - 1, -1, -1):
            row = bytearray(row_bytes)
            for x in range(size):
                if px[x, y][3] == 0:
                    row[x // 8] |= 0x80 >> (x % 8)
            and_mask += row
        frames.append((size, header + bytes(xor) + bytes(and_mask)))
    out = bytearray(struct.pack("<HHH", 0, 1, len(frames)))
    offset = 6 + 16 * len(frames)
    for size, data in frames:
        dim = 0 if size >= 256 else size
        out += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    for _, data in frames:
        out += data
    with open(path, "wb") as f:
        f.write(out)


# Icône Windows multi-tailles
write_ico(f"{OUT}/app.ico", tile, [16, 20, 24, 32, 40, 48, 64, 96, 128, 256])

# Planche d'aperçu : tailles réelles sur fond clair et sombre
sheet = Image.new("RGBA", (980, 330), (243, 245, 249, 255))
sd = ImageDraw.Draw(sheet)
sd.rectangle([0, 165, 980, 330], fill=(14, 16, 20, 255))
x = 20
for size in (256, 64, 48, 32, 24, 16):
    img = tile.resize((size, size), Image.LANCZOS)
    y_light = 20 + (128 - min(size, 128)) // 2 if size <= 128 else 20
    if size == 256:
        big = tile.resize((290, 290), Image.LANCZOS)
        sheet.paste(big, (x, 20), big)
        x += 320
        continue
    sheet.paste(img, (x, 80 - size // 2), img)
    sheet.paste(img, (x, 245 - size // 2), img)
    x += size + 40
sheet.save(f"{OUT}/preview.png")
print("ok")
