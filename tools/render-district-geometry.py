#!/usr/bin/env python3
"""Make the district geometry asset of the vote map from the Census cartographic boundary KML.

Run it with a local copy of the pinned zip:
    python3 tools/render-district-geometry.py --source PATH
Check that the committed asset is current:
    python3 tools/render-district-geometry.py --source PATH --check

The script needs only the Python standard library. It never downloads. It verifies the size and the
SHA-256 of the zip, so a changed Census file fails loudly and never changes the asset silently.
"""
import argparse
import hashlib
import heapq
import io
import json
import math
import sys
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ASSET = ROOT / 'src/CivicLens.PublicWeb/wwwroot/data/districts-119.json'

SOURCE_URL = 'https://www2.census.gov/geo/tiger/GENZ2024/kml/cb_2024_us_cd119_20m.zip'
SOURCE_SHA256 = 'da0e648b037ca9156a539cad2fe4c7ddd72d33908ad2fce06e8a64d40913cbf5'
SOURCE_SIZE = 290655
SOURCE_MEMBER = 'cb_2024_us_cd119_20m.kml'
CONGRESS = 119

# Census KML field names, read from ExtendedData/SchemaData/SimpleData. The script fails when one is missing.
FIELD_STATE = 'STATEFP'
FIELD_DISTRICT = 'CD119FP'

WIDTH = 800
KEEP_FRACTION = 0.25
INSET_GAP = 12
ALASKA_SCALE = 0.35  # of the lower 48 scale
HAWAII_SCALE = 1.0

# Albers equal-area conic parameters in degrees: (central meridian, latitude of origin, parallel 1, parallel 2).
# They follow the projections that d3 uses for its USA composite: lower 48, Alaska, and Hawaii.
LOWER_48 = (-96.0, 23.0, 29.5, 45.5)
ALASKA = (-154.0, 50.0, 55.0, 65.0)
HAWAII = (-157.0, 3.0, 8.0, 18.0)

STATE_CODES = {
    '01': 'AL', '02': 'AK', '04': 'AZ', '05': 'AR', '06': 'CA', '08': 'CO', '09': 'CT', '10': 'DE', '11': 'DC',
    '12': 'FL', '13': 'GA', '15': 'HI', '16': 'ID', '17': 'IL', '18': 'IN', '19': 'IA', '20': 'KS', '21': 'KY',
    '22': 'LA', '23': 'ME', '24': 'MD', '25': 'MA', '26': 'MI', '27': 'MN', '28': 'MS', '29': 'MO', '30': 'MT',
    '31': 'NE', '32': 'NV', '33': 'NH', '34': 'NJ', '35': 'NM', '36': 'NY', '37': 'NC', '38': 'ND', '39': 'OH',
    '40': 'OK', '41': 'OR', '42': 'PA', '44': 'RI', '45': 'SC', '46': 'SD', '47': 'TN', '48': 'TX', '49': 'UT',
    '50': 'VT', '51': 'VA', '53': 'WA', '54': 'WV', '55': 'WI', '56': 'WY',
    '60': 'AS', '66': 'GU', '69': 'MP', '72': 'PR', '78': 'VI',
}
DELEGATE_AREAS = {'DC', 'PR', 'GU', 'VI', 'AS', 'MP'}
NO_DISTRICT = 'ZZ'  # Census code for water and other land with no district


class GeometryError(Exception):
    """The input is not the file that this script expects."""


# ---- Input ----

def read_kml(source):
    """Return the KML bytes of the pinned zip. Fail when the size, the hash, or the content differ."""
    data = Path(source).read_bytes()
    if len(data) != SOURCE_SIZE:
        raise GeometryError(f'{source} has {len(data)} bytes. The pinned file has {SOURCE_SIZE} bytes.')
    digest = hashlib.sha256(data).hexdigest()
    if digest != SOURCE_SHA256:
        raise GeometryError(f'{source} has sha256 {digest}. The pinned file has sha256 {SOURCE_SHA256}.')
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        if SOURCE_MEMBER not in archive.namelist():
            raise GeometryError(f'The zip has no member {SOURCE_MEMBER}.')
        return archive.read(SOURCE_MEMBER)


def local_name(element):
    return element.tag.rsplit('}', 1)[-1]


def parse_ring(coordinates):
    points = []
    for token in coordinates.split():
        parts = token.split(',')
        if len(parts) < 2:
            raise GeometryError(f'Bad KML coordinate {token!r}.')
        points.append((float(parts[0]), float(parts[1])))
    if len(points) > 1 and points[0] == points[-1]:
        points.pop()  # a ring holds no closing copy of its first point
    return points


def placemark_fields(placemark):
    return {data.get('name'): (data.text or '').strip() for data in placemark.iter() if local_name(data) == 'SimpleData'}


def placemark_rings(placemark):
    rings = []
    for ring in placemark.iter():
        if local_name(ring) != 'LinearRing':
            continue
        for coordinates in ring:
            if local_name(coordinates) == 'coordinates':
                points = parse_ring(coordinates.text or '')
                if len(points) >= 3:
                    rings.append(points)
    return rings


def parse_districts(kml):
    """Return {(state code, district): rings}. A district of 00 or 98 is 0. Rows with no district are dropped."""
    districts = {}
    for placemark in ET.fromstring(kml).iter():
        if local_name(placemark) != 'Placemark':
            continue
        fields = placemark_fields(placemark)
        for name in (FIELD_STATE, FIELD_DISTRICT):
            if name not in fields:
                raise GeometryError(f'A Placemark has no SimpleData field {name}.')
        if fields[FIELD_DISTRICT] == NO_DISTRICT:
            continue
        if fields[FIELD_STATE] not in STATE_CODES:
            raise GeometryError(f'Unknown state FIPS code {fields[FIELD_STATE]!r}.')
        key = (STATE_CODES[fields[FIELD_STATE]], 0 if fields[FIELD_DISTRICT] in ('00', '98') else int(fields[FIELD_DISTRICT]))
        rings = placemark_rings(placemark)
        if not rings:
            raise GeometryError(f'District {key} has no polygon.')
        if key in districts:
            raise GeometryError(f'District {key} appears twice.')
        districts[key] = rings
    if not districts:
        raise GeometryError('The KML holds no district.')
    return districts


# ---- Projection ----

def albers(parameters):
    """Return a function from (longitude, latitude) in degrees to unit-sphere x and y, with y growing south."""
    lon0, lat0, lat1, lat2 = (math.radians(value) for value in parameters)
    n = (math.sin(lat1) + math.sin(lat2)) / 2
    c = math.cos(lat1) ** 2 + 2 * n * math.sin(lat1)
    rho0 = math.sqrt(c - 2 * n * math.sin(lat0)) / n

    def project(lon, lat):
        delta = (math.radians(lon) - lon0 + math.pi) % (2 * math.pi) - math.pi  # wraps the antimeridian
        rho = math.sqrt(max(0.0, c - 2 * n * math.sin(math.radians(lat)))) / n
        theta = n * delta
        return rho * math.sin(theta), -(rho0 - rho * math.cos(theta))

    return project


def region_of(state):
    return state if state in ('AK', 'HI') else 'LOWER48'


def bounds(points):
    xs, ys = [p[0] for p in points], [p[1] for p in points]
    return min(xs), min(ys), max(xs), max(ys)


def build_transforms(districts):
    """Return {region: function from (longitude, latitude) to float screen x and y}.

    The lower 48 fill WIDTH. Alaska and Hawaii are scaled and sit under the lower 48, left to right,
    so that an inset never covers a state."""
    projections = {'LOWER48': albers(LOWER_48), 'AK': albers(ALASKA), 'HI': albers(HAWAII)}
    projected = {region: [] for region in projections}
    for (state, _), rings in districts.items():
        region = region_of(state)
        projected[region].extend(projections[region](*p) for ring in rings for p in ring)
    if not projected['LOWER48']:
        raise GeometryError('The KML holds no district of the lower 48 states.')
    min_x, min_y, max_x, _ = bounds(projected['LOWER48'])
    scale = WIDTH / (max_x - min_x)
    scales = {'LOWER48': scale, 'AK': scale * ALASKA_SCALE, 'HI': scale * HAWAII_SCALE}
    main_bottom = (bounds(projected['LOWER48'])[3] - min_y) * scale
    transforms = {}
    cursor_x, top = 0.0, main_bottom + INSET_GAP
    for region in ('LOWER48', 'AK', 'HI'):
        if not projected[region]:
            continue
        x0, y0, x1, _ = bounds(projected[region])
        left, upper = (0.0, 0.0) if region == 'LOWER48' else (cursor_x, top)
        transforms[region] = _affine(projections[region], scales[region], x0, y0, left, upper)
        if region != 'LOWER48':
            cursor_x += (x1 - x0) * scales[region] + INSET_GAP
    return transforms


def _affine(project, scale, x0, y0, left, top):
    def transform(lon, lat):
        x, y = project(lon, lat)
        return (x - x0) * scale + left, (y - y0) * scale + top
    return transform


# ---- Topology: shared arcs, so that district borders and state borders use the same points ----

def undirected(a, b):
    return (a, b) if a <= b else (b, a)


def junctions(rings):
    """Points where the border graph does not simply continue: a point with a number of edges other than 2,
    or where an edge shared by two rings meets an edge of one ring."""
    uses = {}
    for ring in rings:
        for i, point in enumerate(ring):
            edge = undirected(point, ring[(i + 1) % len(ring)])
            uses[edge] = uses.get(edge, 0) + 1
    around = {}
    for edge, count in uses.items():
        for point in edge:
            around.setdefault(point, []).append(count)
    return {point for point, counts in around.items() if len(counts) != 2 or counts[0] != counts[1]}


def split_ring(ring, cut_points):
    """Split a ring into arcs, each a list of points that starts and ends at a cut point. No cut point: one closed arc."""
    starts = [i for i, point in enumerate(ring) if point in cut_points]
    if not starts:  # start at the smallest point, so that two districts that share the ring name the same arc
        first = ring.index(min(ring))
        return [ring[first:] + ring[:first] + [ring[first]]]
    rotated = ring[starts[0]:] + ring[:starts[0]]
    arcs, current = [], [rotated[0]]
    for point in rotated[1:] + [rotated[0]]:
        current.append(point)
        if point in cut_points:
            arcs.append(current)
            current = [point]
    return arcs


class Topology:
    """The arcs of all districts. Rings of different districts that share a border share the arc object."""

    def __init__(self):
        self.arcs = []   # list of lists of (lon, lat)
        self._ids = {}   # canonical point sequence -> arc id

    def add(self, arc):
        """Return (arc id, reversed). The same border read in the other direction gives the same id."""
        key, backward = tuple(arc), False
        if key[::-1] < key:
            key, backward = key[::-1], True
        if key not in self._ids:
            self._ids[key] = len(self.arcs)
            self.arcs.append(list(key))
        return self._ids[key], backward


def simplify(points, keep):
    """Visvalingam-Whyatt: remove the point with the smallest triangle area until `keep` points remain. The ends stay."""
    count = len(points)
    if count <= keep or count <= 2:
        return list(points)
    previous, following = list(range(-1, count - 1)), list(range(1, count + 1))
    removed = [False] * count
    stamp = [0] * count

    def area(i):
        (ax, ay), (bx, by), (cx, cy) = points[previous[i]], points[i], points[following[i]]
        return abs((bx - ax) * (cy - ay) - (cx - ax) * (by - ay)) / 2

    heap = [(area(i), i, 0) for i in range(1, count - 1)]
    heapq.heapify(heap)
    remaining = count
    while remaining > keep and heap:
        _, i, version = heapq.heappop(heap)
        if removed[i] or version != stamp[i]:
            continue
        removed[i] = True
        remaining -= 1
        before, after = previous[i], following[i]
        following[before], previous[after] = after, before
        for neighbor in (before, after):
            if 0 < neighbor < count - 1:
                stamp[neighbor] += 1
                heapq.heappush(heap, (area(neighbor), neighbor, stamp[neighbor]))
    return [point for i, point in enumerate(points) if not removed[i]]


def round_unit(value):
    return math.floor(value + 0.5)


def reduce_arc(points, full):
    """Simplify an arc to about KEEP_FRACTION of its points, then round to whole units. A closed arc keeps 3 points plus the repeat."""
    closed = points[0] == points[-1]
    minimum = 4 if closed else 2
    keep = len(points) if full else max(minimum, math.ceil(len(points) * KEEP_FRACTION))
    rounded = []
    for x, y in simplify(points, keep):
        point = (round_unit(x), round_unit(y))
        if not rounded or rounded[-1] != point:
            rounded.append(point)
    return rounded


def ring_points(parts, arcs):
    """Join arcs, each read forward or reversed, into the points of one ring, with no repeated joint and no closing copy."""
    points = []
    for arc_id, backward in parts:
        arc = arcs[arc_id][::-1] if backward else arcs[arc_id]
        points.extend(arc if not points else arc[1:])
    if len(points) > 1 and points[0] == points[-1]:
        points.pop()
    return points


def ring_path(points):
    return 'M' + ' '.join(f'{x} {y}' for x, y in points) + 'Z'


def paths_of(rings_parts, arcs):
    """Return the path of a list of rings. A ring of fewer than 3 distinct points is dropped. None when no ring is left."""
    drawn = []
    for parts in rings_parts:
        points = ring_points(parts, arcs)
        if len(set(points)) >= 3:
            drawn.append(ring_path(points))
    return ''.join(drawn) if drawn else None


def state_boundary_rings(owned_arcs, topology_arcs):
    """Chain the arcs that one state uses once into closed rings. Each ring is a list of (arc id, reversed)."""
    at_point = {}
    for arc_id in owned_arcs:
        arc = topology_arcs[arc_id]
        at_point.setdefault(arc[0], []).append(arc_id)
        at_point.setdefault(arc[-1], []).append(arc_id)
    unused, rings = list(owned_arcs), []
    while unused:
        first = unused.pop(0)
        parts = [(first, False)]
        start, here = topology_arcs[first][0], topology_arcs[first][-1]
        while here != start:
            options = [a for a in at_point[here] if a in unused]
            if not options:
                raise GeometryError('A state border does not close.')
            arc_id = options[0]
            unused.remove(arc_id)
            backward = topology_arcs[arc_id][0] != here
            parts.append((arc_id, backward))
            here = topology_arcs[arc_id][0] if backward else topology_arcs[arc_id][-1]
        rings.append(parts)
    return rings


# ---- Output ----

def build(districts):
    """Return the asset as a dictionary. Raise GeometryError when the input does not give a valid map."""
    drawn = {key: rings for key, rings in districts.items() if key[0] not in DELEGATE_AREAS}
    transforms = build_transforms(drawn)
    all_rings = [ring for key in sorted(drawn) for ring in drawn[key]]
    cut_points = junctions(all_rings)

    topology = Topology()
    district_rings = {}
    for key in sorted(drawn):
        district_rings[key] = [[topology.add(arc) for arc in split_ring(ring, cut_points)] for ring in drawn[key]]

    screen = {}
    for key, rings in district_rings.items():
        transform = transforms[region_of(key[0])]
        for parts in rings:
            for arc_id, _ in parts:
                screen.setdefault(arc_id, [transform(*p) for p in topology.arcs[arc_id]])

    full_arcs = set()
    reduced = {}
    while True:
        reduced = {arc_id: reduce_arc(points, arc_id in full_arcs) for arc_id, points in screen.items()}
        failing = [rings for rings in district_rings.values() if paths_of(rings, reduced) is None]
        if not failing:
            break
        # A district with fewer than 3 points keeps all the points of its arcs. Its neighbors share those arcs.
        more = {arc_id for rings in failing for parts in rings for arc_id, _ in parts} - full_arcs
        if not more:
            raise GeometryError('A district has fewer than 3 points.')
        full_arcs |= more
    paths = {key: paths_of(rings, reduced) for key, rings in district_rings.items()}

    state_paths = {}
    for state in sorted({key[0] for key in drawn}):
        uses = {}
        for key, rings in district_rings.items():
            if key[0] == state:
                for parts in rings:
                    for arc_id, _ in parts:
                        uses[arc_id] = uses.get(arc_id, 0) + 1
        boundary = [arc_id for arc_id in sorted(uses) if uses[arc_id] == 1]
        path = paths_of(state_boundary_rings(boundary, topology.arcs), reduced)
        if path is None:
            raise GeometryError(f'State {state} has no outline.')
        state_paths[state] = path

    height = math.ceil(max(y for points in reduced.values() for _, y in points))
    return {
        'source': SOURCE_URL,
        'sha256': SOURCE_SHA256,
        'congress': CONGRESS,
        'viewBox': f'0 0 {WIDTH} {height}',
        'districts': [
            {'state': key[0], 'district': key[1], 'delegate': key[0] in DELEGATE_AREAS, 'path': paths.get(key, '')}
            for key in sorted(districts)
        ],
        'states': [{'state': state, 'path': state_paths[state]} for state in sorted(state_paths)],
    }


def render(asset):
    """Return the text of the asset, one district or state on each line, so that a change reads well in a diff."""
    def lines(items):
        return ',\n'.join('    ' + json.dumps(item, separators=(',', ':')) for item in items)

    head = ',\n'.join(f'  {json.dumps(name)}: {json.dumps(asset[name])}' for name in ('source', 'sha256', 'congress', 'viewBox'))
    return (f'{{\n{head},\n  "districts": [\n{lines(asset["districts"])}\n  ],\n'
            f'  "states": [\n{lines(asset["states"])}\n  ]\n}}\n')


def generate(source):
    return render(build(parse_districts(read_kml(source))))


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--source', required=True, help='local copy of cb_2024_us_cd119_20m.zip')
    parser.add_argument('--output', type=Path, default=ASSET, help='asset to write or check')
    parser.add_argument('--check', action='store_true', help='fail when the asset differs from a fresh run, and write nothing')
    args = parser.parse_args(argv)
    try:
        text = generate(args.source)
    except (GeometryError, OSError, zipfile.BadZipFile, ET.ParseError, ValueError) as error:
        print(f'error: {error}', file=sys.stderr)
        return 1
    if args.check:
        current = args.output.read_text(encoding='utf-8') if args.output.exists() else None
        if current != text:
            print(f'{args.output} is missing or differs from a fresh run. Run this script without --check.', file=sys.stderr)
            return 1
        print(f'{args.output} is current.')
        return 0
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(text, encoding='utf-8')
    print(f'Wrote {args.output}.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
