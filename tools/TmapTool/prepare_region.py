"""Center host producer: complete regional roads and bounded multi-HGT numeric DEM.

Called by TmapRegionProductionService; dependencies are in requirements-region.txt.
No rendered terrain pixels are used as elevation measurements.
"""
import concurrent.futures
import gzip
import hashlib
import json
import math
import pathlib
import sys
import time
import urllib.request
import numpy as np
import osmium
import shapely
from shapely.geometry import shape, box
from region_contours import extract_contours

ATTRIBUTION = ('Terrain Tiles / Tilezen; global SRTM and GMTED2010 courtesy USGS; '
    'global ETOPO1 NOAA; Europe EU-DEM produced using Copernicus data funded by the European Union; '
    'United Kingdom terrain data Environment Agency copyright and/or database right 2015. '
    'https://github.com/tilezen/joerd/blob/master/docs/attribution.md')


def sha256(path):
    result = hashlib.sha256()
    with pathlib.Path(path).open('rb') as stream:
        while data := stream.read(1024 * 1024):
            result.update(data)
    return result.hexdigest()


def crop_roads(plan, output):
    source = pathlib.Path(plan['PbfPath'])
    bounds = plan['Area']['Bounds']
    raw = plan['Area'].get('BoundaryGeoJson', '')
    document = json.loads(raw) if raw else None
    geometry = shape(document['geometry'] if document and document.get('type') == 'Feature' else document) if document else box(
        bounds['West'], bounds['South'], bounds['East'], bounds['North'])
    if not geometry.is_valid or geometry.is_empty:
        raise ValueError('Invalid regional polygon')
    shapely.prepare(geometry)
    inside = set()
    for obj in osmium.FileProcessor(source, osmium.osm.NODE):
        if shapely.intersects_xy(geometry, obj.location.lon, obj.location.lat):
            inside.add(obj.id)
    closed = set()
    conservative = plan.get('ExcludeConditionalFeatures', False)
    if conservative:
        for obj in osmium.FileProcessor(source, osmium.osm.RELATION):
            if obj.tags.get('type') == 'restriction' and any('conditional' in tag.k for tag in obj.tags):
                closed.update(m.ref for m in obj.members if m.type == 'w' and m.role == 'from')
    ways, nodes = set(), set()
    excluded = 0
    for obj in osmium.FileProcessor(source, osmium.osm.WAY):
        if 'highway' not in obj.tags:
            continue
        refs = [n.ref for n in obj.nodes]
        if not any(ref in inside for ref in refs):
            continue
        if conservative and (obj.id in closed or any('conditional' in tag.k for tag in obj.tags)):
            excluded += 1
            continue
        ways.add(obj.id)
        nodes.update(refs)
    print(json.dumps(dict(stage='regional-roads', ways=len(ways), nodes=len(nodes), excluded_conditional_ways=excluded)), flush=True)
    barriers = restrictions = 0
    target = output / 'region.osm.pbf'
    with osmium.SimpleWriter(target) as writer:
        for obj in osmium.FileProcessor(source):
            if obj.is_node() and obj.id in nodes:
                if conservative and any('conditional' in tag.k for tag in obj.tags):
                    tags = {tag.k: tag.v for tag in obj.tags if 'conditional' not in tag.k}
                    tags.update(access='no', foot='no', bicycle='no', barrier=tags.get('barrier', 'gate'))
                    writer.add_node(obj.replace(tags=tags))
                    barriers += 1
                else:
                    writer.add_node(obj)
            elif obj.is_way() and obj.id in ways:
                writer.add_way(obj)
            elif obj.is_relation() and obj.tags.get('type') == 'restriction':
                if conservative and any('conditional' in tag.k for tag in obj.tags):
                    continue
                if all((m.type == 'n' and m.ref in nodes) or (m.type == 'w' and m.ref in ways) for m in obj.members):
                    writer.add_relation(obj)
                    restrictions += 1
    if not ways:
        raise ValueError('No roads intersect the selected region')
    return dict(source=str(source), source_sha256=sha256(source), output_sha256=sha256(target),
        ways=len(ways), nodes=len(nodes), restrictions=restrictions, excluded_conditional_ways=excluded,
        closed_conditional_nodes=barriers,
        crop_policy='Complete highway ways touching polygon nodes; all referenced nodes retained. Boundary ways may extend outside terrain coverage. No synthetic joins.',
        condition_policy='Conservative closure of all conditional ways/nodes and from-ways of conditional turn restrictions; no conditional feature assumed open.' if conservative else 'Preserve conditions for SDK runtime compilation; unsupported conditions fail.')


def terrain(plan, output):
    bounds, step = plan['Area']['Bounds'], plan['GridStepDegrees']
    left = round(math.floor(bounds['West'] / step) * step, 7)
    bottom = round(math.floor(bounds['South'] / step) * step, 7)
    columns = math.ceil((bounds['East'] - left) / step) + 1
    rows = math.ceil((bounds['North'] - bottom) / step) + 1
    if min(rows, columns) < 2 or rows * columns > plan['MaximumDemSamples']:
        raise ValueError('Regional DEM exceeds configured sample budget')
    right, top = left + (columns - 1) * step, bottom + (rows - 1) * step
    cache = pathlib.Path(plan['ElevationCacheDirectory'])
    cache.mkdir(parents=True, exist_ok=True)
    heights = np.full((rows, columns), -32768, dtype=np.int32)
    tiles = [(x, y) for x in range(math.floor(left), math.floor(right) + 1)
             for y in range(math.floor(bottom), math.floor(top) + 1)]
    if len(tiles) > 64:
        raise ValueError('Regional elevation download exceeds 64-tile budget')
    def fetch(tile):
        x, y = tile
        stem = f'{"N" if y >= 0 else "S"}{abs(y):02d}{"E" if x >= 0 else "W"}{abs(x):03d}'
        path = cache / (stem + '.hgt.gz')
        url = f'https://s3.amazonaws.com/elevation-tiles-prod/skadi/{stem[:3]}/{stem}.hgt.gz'
        if not path.exists():
            temporary = path.with_suffix('.part')
            for attempt in range(4):
                actual_url = url if attempt % 2 == 0 else url.replace('s3.amazonaws.com/elevation-tiles-prod', 'elevation-tiles-prod.s3.amazonaws.com')
                try:
                    request = urllib.request.Request(actual_url, headers={'User-Agent': 'TrailMateCenter/0.1', 'Cache-Control': 'no-cache'})
                    with urllib.request.urlopen(request, timeout=90) as response:
                        compressed = response.read(64 * 1024 * 1024 + 1)
                        expected = response.headers.get('Content-Length')
                    if expected and len(compressed) != int(expected):
                        raise ValueError('Truncated HGT HTTP response')
                    if len(compressed) > 64 * 1024 * 1024:
                        raise ValueError('HGT download exceeds 64 MiB compressed limit')
                    temporary.write_bytes(compressed)
                    validate_hgt(compressed)
                    temporary.replace(path)
                    url = actual_url
                    break
                except (OSError, EOFError, ValueError):
                    if attempt == 3:
                        raise
                    time.sleep(2 ** attempt)
                finally:
                    temporary.unlink(missing_ok=True)
        compressed = path.read_bytes()
        data = validate_hgt(compressed)
        return x, y, data, dict(url=url, sha256=sha256(path), bytes=len(compressed), side=data.shape[0])
    provenance = []
    # Two download workers; total retained source count is bounded by the 64-tile limit.
    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
        for x, y, data, metadata in pool.map(fetch, tiles):
            xs, ys = left + np.arange(columns) * step, bottom + np.arange(rows) * step
            ix, iy = np.flatnonzero((xs >= x) & (xs < x + 1)), np.flatnonzero((ys >= y) & (ys < y + 1))
            scale = data.shape[0] - 1
            px, py = (xs[ix] - x) * scale, (1 - (ys[iy] - y)) * scale
            ax, ay = np.floor(px).astype(int).clip(0, scale - 1), np.floor(py).astype(int).clip(0, scale - 1)
            fx, fy = px - ax, py - ay
            a, c = data[np.ix_(ay, ax)].astype(float), data[np.ix_(ay, ax + 1)].astype(float)
            d, e = data[np.ix_(ay + 1, ax)].astype(float), data[np.ix_(ay + 1, ax + 1)].astype(float)
            valid = (a != -32768) & (c != -32768) & (d != -32768) & (e != -32768)
            values = (a * (1 - fx) + c * fx) * (1 - fy[:, None]) + (d * (1 - fx) + e * fx) * fy[:, None]
            heights[np.ix_(iy, ix)] = np.where(valid, np.rint(values), -32768).astype(np.int32)
            provenance.append(metadata)
            print(json.dumps(dict(stage='elevation-source', **metadata)), flush=True)
    missing = int((heights == -32768).sum())
    resolution = math.ceil(111320 * step)
    document = dict(name=plan['Area']['Name'], contour_interval_m=plan['ContourIntervalMeters'],
        dem=dict(origin=[left, bottom], step=[step, step],
            heights=[[None if value == -32768 else value for value in row] for row in heights.tolist()],
            resolution_m=resolution, vertical_datum=0, source=ATTRIBUTION,
            block_side=32, profile_spacing_m=resolution, profile_threshold_m=3))
    coverage = None
    if plan.get('ClipContoursToRegion', True) and plan['Area'].get('BoundaryGeoJson'):
        boundary = json.loads(plan['Area']['BoundaryGeoJson'])
        geometry = shape(boundary['geometry'] if boundary.get('type') == 'Feature' else boundary)
        if not geometry.is_valid:
            raise ValueError('Invalid contour coverage polygon')
        shapely.prepare(geometry)
        coverage = shapely.intersects_xy(geometry, (left + np.arange(columns) * step)[None, :],
            (bottom + np.arange(rows) * step)[:, None])
    document['contours'] = extract_contours(document['dem'], plan['ContourIntervalMeters'], coverage=coverage)
    (output / 'terrain.json').write_text(json.dumps(document), encoding='utf-8')
    return dict(sources=provenance, bounds=[left, bottom, right, top], rows=rows, columns=columns,
        missing_samples=missing, resolution_m=resolution, contour_interval_m=plan['ContourIntervalMeters'],
        vertical_datum='unknown mixed-source datum; no vertical conversion claimed', attribution=ATTRIBUTION,
        resampling='Bilinear, rounded to integer metres; any NoData corner preserves a gap.',
        contour_features=len(document['contours']), contour_producer='Center candidate-filtered marching squares; SDK v0.1.1 stitching semantics',
        contour_coverage='Cells with all four sample nodes inside selected region' if coverage is not None else 'Full numeric grid',
        contour_producer_sha256=sha256(pathlib.Path(__file__).with_name('region_contours.py')))


def validate_hgt(compressed):
    import io
    with gzip.GzipFile(fileobj=io.BytesIO(compressed)) as stream:
        raw = stream.read(3601 * 3601 * 2 + 1)
    side = math.isqrt(len(raw) // 2)
    if side not in (1201, 3601) or side * side * 2 != len(raw):
        raise ValueError('Invalid HGT dimensions or decompression limit exceeded')
    return np.frombuffer(raw, dtype='>i2').reshape(side, side)


if __name__ == '__main__':
    plan = json.loads(pathlib.Path(sys.argv[1]).read_text(encoding='utf-8'))
    output = pathlib.Path(sys.argv[2])
    provenance = dict(producer='TrailMateCenter.TmapRegionProductionService',
        script_sha256=sha256(__file__), terrain=terrain(plan, output), roads=crop_roads(plan, output))
    (output / 'provenance.json').write_text(json.dumps(provenance, indent=2), encoding='utf-8')
