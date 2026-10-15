# SPDX-License-Identifier: AGPL-3.0-only
# Copyright (C) 2026 Liu Weikai
# Marching/stitching semantics follow TMAP v0.1.1 tools/tmap_pack.py.
# Center adds host-only vectorized candidate rejection; device format is unchanged.
import math
import numpy as np


def extract_contours(dem, interval=10, maximum_segments=12_000_000, coverage=None):
    if interval <= 0 or maximum_segments < 1:
        raise ValueError('Invalid contour interval or segment budget')
    rows = dem['heights']
    values = np.asarray(rows, dtype=np.float64)
    if values.ndim != 2 or min(values.shape) < 2:
        raise ValueError('Invalid contour source dimensions')
    finite = values[np.isfinite(values)]
    if not finite.size:
        return []
    corners = [values[:-1, :-1], values[:-1, 1:], values[1:, 1:], values[1:, :-1]]
    minimum = np.minimum.reduce(corners)
    maximum = np.maximum.reduce(corners)
    if coverage is not None:
        if coverage.shape != values.shape:
            raise ValueError('Contour coverage dimensions do not match DEM')
        selected = coverage[:-1, :-1] & coverage[:-1, 1:] & coverage[1:, 1:] & coverage[1:, :-1]
        minimum = np.where(selected, minimum, np.nan)
        maximum = np.where(selected, maximum, np.nan)
    latitude, longitude = round(dem['origin'][1] * 1e7), round(dem['origin'][0] * 1e7)
    dx, dy = (round(step * 1e7) for step in dem['step'])
    output, identity, total = [], 1, 0
    for elevation in range(math.ceil(float(finite.min()) / interval) * interval,
                           math.floor(float(finite.max()) / interval) * interval + 1, interval):
        ys, xs = np.nonzero((minimum < elevation) & (maximum >= elevation))
        segments = []
        for y, x in zip(ys.tolist(), xs.tolist()):
            zs = [rows[y][x], rows[y][x + 1], rows[y + 1][x + 1], rows[y + 1][x]]
            coords = [(latitude + y * dy, longitude + x * dx), (latitude + y * dy, longitude + (x + 1) * dx),
                      (latitude + (y + 1) * dy, longitude + (x + 1) * dx), (latitude + (y + 1) * dy, longitude + x * dx)]
            crossings = {}
            for edge in range(4):
                other = (edge + 1) % 4
                if (zs[edge] >= elevation) != (zs[other] >= elevation):
                    ratio = (elevation - zs[edge]) / (zs[other] - zs[edge])
                    crossings[edge] = tuple(round(coords[edge][i] + (coords[other][i] - coords[edge][i]) * ratio) for i in range(2))
            if len(crossings) == 2:
                pairs = [tuple(crossings)]
            elif len(crossings) == 4:
                center_high = sum(zs) / 4 >= elevation
                pairs = [(0, 1), (2, 3)] if (zs[0] >= elevation) == center_high else [(3, 0), (1, 2)]
            else:
                continue
            for a, b in pairs:
                if crossings[a] != crossings[b]:
                    segments.append((crossings[a], crossings[b]))
                    total += 1
                    if total > maximum_segments:
                        raise ValueError('Regional contour segment budget exceeded')
        adjacency = {}
        for index, (a, b) in enumerate(segments):
            adjacency.setdefault(a, []).append(index)
            adjacency.setdefault(b, []).append(index)
        used = set()
        for seed in sorted(adjacency, key=lambda p: (len(adjacency[p]) != 1, p)):
            while any(index not in used for index in adjacency[seed]):
                path, current = [seed], seed
                while True:
                    candidates = [index for index in adjacency[current] if index not in used]
                    if not candidates:
                        break
                    index = candidates[0]
                    used.add(index)
                    a, b = segments[index]
                    current = b if a == current else a
                    path.append(current)
                if len(path) >= 2:
                    output.append(dict(id=identity, elevation_m=elevation, points=[[p[1] / 1e7, p[0] / 1e7] for p in path]))
                    identity += 1
    return output
