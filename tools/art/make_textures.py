#!/usr/bin/env python3
"""Graphics stage G2: builds the PS1-style battlefield textures (client/assets/ground/*.png, client/assets/terrain/*.png).

Made for this project (licence "Original"). Small tiling textures cut to 15-bit colour (5 bits per channel), drawn by
the client with nearest filtering, so they read as chunky pixels up close like a PlayStation game. Each terrain
texture's average colour stays close to the plain colour of that kind of terrain, so the battlefield keeps its
contrast with the side colours.

- ground/mud.png: churned mud with ruts and wet patches (tiles once per 6").
- terrain/rubble.png (Low), terrain/sandbags.png (Cover), terrain/stone.png (Wall): tile once per 2".
- ground/crater.png and ground/duckboard.png: flat decals with cut-out edges, laid on open ground.

Run: python3 tools/art/make_textures.py (needs numpy and Pillow; same input gives the same bytes).
"""
import os

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.abspath(os.path.join(HERE, "..", "..", "client", "assets"))


def noise(rng, size, cells):
    """Tiling value noise in 0..1: a periodic grid of random values, smoothly interpolated."""
    grid = rng.random((cells, cells))
    t = np.arange(size) * cells / size
    i = np.floor(t).astype(int)
    f = t - i
    f = f * f * (3 - 2 * f)
    i1 = (i + 1) % cells
    a = grid[np.ix_(i, i)]
    b = grid[np.ix_(i, i1)]
    c = grid[np.ix_(i1, i)]
    d = grid[np.ix_(i1, i1)]
    fx = f[None, :]
    fy = f[:, None]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def fractal(rng, size, cells=(4, 8, 16, 32), weights=(0.45, 0.3, 0.15, 0.1)):
    return sum(w * noise(rng, size, c) for c, w in zip(cells, weights))


def psx(rgb, alpha=None):
    """15-bit colour: each channel cut to 5 bits."""
    q = np.clip(np.round(np.clip(rgb, 0, 255) / 255 * 31) / 31 * 255, 0, 255).astype(np.uint8)
    if alpha is None:
        return Image.fromarray(q, "RGB")
    return Image.fromarray(np.dstack([q, np.where(alpha > 0.5, 255, 0).astype(np.uint8)]), "RGBA")


def colour(hexc):
    return np.array([int(hexc[i:i + 2], 16) for i in (0, 2, 4)], dtype=float)


def shade(base, amount):
    return base[None, None, :] * amount[:, :, None]


def mud(rng, size=128):
    n = fractal(rng, size)
    wet = noise(rng, size, 6)
    tracks = np.abs(np.sin((np.arange(size)[None, :] / size * 2 * np.pi * 3) + 0.6 * np.sin(np.arange(size)[:, None] / size * 2 * np.pi * 2)))
    amount = 0.78 + 0.42 * n - 0.06 * (tracks < 0.12) * (noise(rng, size, 8) > 0.4)
    rgb = shade(colour("403829"), amount)
    puddle = wet > 0.72
    rgb[puddle] = rgb[puddle] * 0.72 + colour("2e3029") * 0.28
    specks = (rng.random((size, size)) > 0.985) & puddle
    rgb[specks] = colour("6a6a5e")
    stones = rng.random((size, size)) > 0.993
    rgb[stones] = colour("5c5244")
    return psx(rgb)


def stone(rng, size=128):
    rgb = np.zeros((size, size, 3))
    n = fractal(rng, size, (8, 16, 32), (0.5, 0.3, 0.2))
    course = 16
    for row in range(size // course):
        y0 = row * course
        x = (row % 2) * 13
        widths = []
        while sum(widths) < size:
            widths.append(int(rng.integers(22, 38)))
        widths[-1] -= sum(widths) - size
        for w in widths:
            tone = 0.82 + 0.3 * rng.random()
            tint = colour("8a8680") * tone
            for yy in range(y0, y0 + course):
                for k in range(w):
                    xx = (x + k) % size
                    rgb[yy, xx] = tint
            # mortar: the bottom row and the left column of each block
            for k in range(w):
                rgb[(y0 + course - 1) % size, (x + k) % size] = colour("2c2a28")
            for yy in range(y0, y0 + course):
                rgb[yy, x % size] = colour("2c2a28")
            x += w
    rgb *= (0.8 + 0.4 * n)[:, :, None]
    soot = noise(rng, size, 4) > 0.6
    rgb[soot] *= 0.75
    cracks = rng.random((size, size)) > 0.992
    rgb[cracks] = colour("24221f")
    return psx(rgb)


def sandbags(rng, size=128):
    rgb = np.zeros((size, size, 3))
    rgb[:] = colour("3a3022")
    weave = 0.9 + 0.1 * ((np.arange(size)[None, :] + np.arange(size)[:, None]) % 2)
    n = fractal(rng, size, (8, 16, 32), (0.5, 0.3, 0.2))
    bag_h, bag_w = 16, 32
    yy, xx = np.mgrid[0:size, 0:size]
    for row in range(size // bag_h):
        offset = (row % 2) * bag_w // 2
        for col in range(size // bag_w + 1):
            cx = (offset + col * bag_w + bag_w / 2) % size
            cy = row * bag_h + bag_h / 2
            dx = np.minimum(np.abs(xx - cx), size - np.abs(xx - cx)) / (bag_w / 2 + 2)
            dy = (yy - cy) / (bag_h / 2 + 1)
            inside = dx ** 2 + dy ** 2 <= 1
            light = 1.08 - 0.35 * np.clip(dy, -1, 1) - 0.15 * dx
            tone = 0.9 + 0.2 * rng.random()
            rgb[inside] = (colour("86765a") * tone)[None, :] * light[inside][:, None]
    rgb *= (weave * (0.85 + 0.3 * n))[:, :, None]
    return psx(rgb)


def rubble(rng, size=128):
    rgb = np.zeros((size, size, 3))
    rgb[:] = colour("4a4032")
    yy, xx = np.mgrid[0:size, 0:size]
    for _ in range(70):
        cx, cy = rng.random() * size, rng.random() * size
        r = 3 + rng.random() * 9
        kind = rng.random()
        base = colour("5e4638") if kind < 0.3 else colour("66625a") if kind < 0.8 else colour("54442f")
        dx = np.minimum(np.abs(xx - cx), size - np.abs(xx - cx))
        dy = np.minimum(np.abs(yy - cy), size - np.abs(yy - cy))
        inside = np.maximum(dx * (0.7 + rng.random() * 0.6), dy) < r
        light = 1.1 - 0.4 * ((yy - cy) / r)
        rgb[inside] = base[None, :] * np.clip(light[inside], 0.6, 1.3)[:, None] * (0.8 + 0.3 * rng.random())
    rgb *= (0.85 + 0.3 * fractal(rng, size))[:, :, None]
    return psx(rgb)


def crater(rng, size=64):
    yy, xx = np.mgrid[0:size, 0:size]
    r = np.hypot(xx - size / 2 + 0.5, yy - size / 2 + 0.5) / (size / 2)
    wobble = 0.08 * (noise(rng, size, 8) - 0.5)
    r = r + wobble
    n = fractal(rng, size, (4, 8, 16), (0.5, 0.3, 0.2))
    rgb = np.zeros((size, size, 3))
    rim = (r > 0.62) & (r <= 0.95)
    bowl = r <= 0.62
    water = r <= 0.3
    rgb[:] = colour("473e2e")
    rgb[rim] = colour("5a4e3a") * (1.05 - 0.4 * np.abs(r[rim] - 0.8)[:, None])
    rgb[bowl] = colour("2c271f") * (0.8 + 0.5 * r[bowl])[:, None]
    rgb[water] = colour("2a2c27")
    glint = water & (rng.random((size, size)) > 0.94)
    rgb[glint] = colour("55574e")
    rgb *= (0.85 + 0.3 * n)[:, :, None]
    return psx(rgb, alpha=(r <= 0.95).astype(float))


def duckboard(rng, w=128, h=32):
    rgb = np.zeros((h, w, 3))
    alpha = np.zeros((h, w))
    grain = fractal(rng, max(w, h))[:h, :w]
    slat, gap = 8, 3
    x = 0
    while x < w:
        tone = 0.8 + 0.3 * rng.random()
        broken = rng.random() < 0.12
        for k in range(slat):
            if x + k >= w:
                break
            top = int(rng.integers(0, 6)) if broken and k == 0 else 0
            rgb[top:, x + k] = colour("5c4630") * tone
            alpha[top:, x + k] = 1
            if broken:
                alpha[: h // 3, x + k] = 0
        x += slat + gap
    for y in (5, h - 6):     # the two rails under the slats show through the gaps
        rgb[y:y + 3, :][alpha[y:y + 3, :] == 0] = colour("3a2c1e")
        alpha[y:y + 3, :] = 1
    rgb *= (0.85 + 0.3 * grain)[:, :, None]
    return psx(rgb, alpha)


def main():
    rng = np.random.default_rng(1916)
    out = {
        "ground/mud.png": mud(rng),
        "terrain/stone.png": stone(rng),
        "terrain/sandbags.png": sandbags(rng),
        "terrain/rubble.png": rubble(rng),
        "ground/crater.png": crater(rng),
        "ground/duckboard.png": duckboard(rng),
    }
    for path, image in out.items():
        full = os.path.join(ASSETS, path)
        os.makedirs(os.path.dirname(full), exist_ok=True)
        image.save(full, optimize=True)
        print(path, image.size, os.path.getsize(full), "bytes")


if __name__ == "__main__":
    main()
