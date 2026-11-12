import contextlib
import hashlib
import importlib.util
import io
import re
import subprocess
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / 'tools/render-district-geometry.py'
FIXTURE = Path(__file__).resolve().parent / 'fixtures/cb_2024_us_cd119_20m.zip'

spec = importlib.util.spec_from_file_location('render_district_geometry', SCRIPT)
tool = importlib.util.module_from_spec(spec)
spec.loader.exec_module(tool)


def pinned_to(path):
    """Patch the pinned size and hash of the tool to the values of the KML member of a local zip."""
    data = zipfile.ZipFile(path).read(tool.SOURCE_MEMBER)
    stack = contextlib.ExitStack()
    stack.enter_context(mock.patch.object(tool, 'SOURCE_SIZE', len(data)))
    stack.enter_context(mock.patch.object(tool, 'SOURCE_SHA256', hashlib.sha256(data).hexdigest()))
    return stack


def points(path):
    return [(int(x), int(y)) for x, y in re.findall(r'(-?\d+) (-?\d+)', path)]


def zip_with(directory, kml_text):
    target = Path(directory) / 'sample.zip'
    with zipfile.ZipFile(target, 'w') as archive:
        archive.writestr(tool.SOURCE_MEMBER, kml_text)
    return target


class RenderDistrictGeometryTests(unittest.TestCase):
    def build(self):
        with pinned_to(FIXTURE):
            return tool.build(tool.parse_districts(tool.read_kml(FIXTURE)))

    def test_the_asset_has_the_agreed_shape(self):
        asset = self.build()
        self.assertEqual(['source', 'sha256', 'congress', 'viewBox', 'districts', 'states'], list(asset))
        self.assertEqual(tool.SOURCE_URL, asset['source'])
        self.assertEqual(119, asset['congress'])
        self.assertRegex(asset['viewBox'], r'^0 0 800 \d+$')
        self.assertEqual(['state', 'district', 'delegate', 'path'], list(asset['districts'][0]))
        self.assertEqual(['state', 'path'], list(asset['states'][0]))

    def test_districts_and_states_are_sorted_and_an_at_large_district_is_zero(self):
        asset = self.build()
        self.assertEqual([('AK', 0), ('CO', 1), ('CO', 2)], [(d['state'], d['district']) for d in asset['districts']])
        self.assertEqual(['AK', 'CO'], [s['state'] for s in asset['states']])

    def test_every_path_number_is_a_whole_unit_inside_the_view_box(self):
        asset = self.build()
        height = int(asset['viewBox'].split()[3])
        for item in asset['districts'] + asset['states']:
            self.assertRegex(item['path'], r'^(M(-?\d+ -?\d+)( -?\d+ -?\d+)*Z)+$')
            for x, y in points(item['path']):
                self.assertTrue(0 <= x <= 800 and 0 <= y <= height, (item['state'], x, y))

    def test_the_lower_48_fill_the_width_and_the_alaska_inset_sits_under_them(self):
        asset = self.build()
        by_key = {(d['state'], d['district']): points(d['path']) for d in asset['districts']}
        lower = by_key[('CO', 1)] + by_key[('CO', 2)]
        self.assertEqual((0, 800), (min(x for x, _ in lower), max(x for x, _ in lower)))
        self.assertGreater(min(y for _, y in by_key[('AK', 0)]), max(y for _, y in lower))

    def test_points_round_half_up_to_whole_units_and_a_repeated_rounded_point_is_dropped(self):
        self.assertEqual([(1, 2), (2, 3), (3, 3)], tool.reduce_arc([(0.5, 1.5), (0.7, 1.9), (2.4, 2.6), (3.2, 3.4)], True))
        self.assertEqual([(1, 2), (3, 3)], tool.reduce_arc([(0.5, 1.5), (1.2, 2.2), (3.2, 3.4)], False))

    def test_a_ring_is_simplified_to_a_quarter_of_its_points_and_keeps_its_corners(self):
        asset = self.build()
        alaska = points(next(d['path'] for d in asset['districts'] if d['state'] == 'AK'))
        self.assertEqual(4, len(alaska))  # 16 points in the source

    def test_the_state_outline_uses_the_district_points_and_drops_the_shared_border(self):
        asset = self.build()
        districts = {d['district']: set(points(d['path'])) for d in asset['districts'] if d['state'] == 'CO'}
        outline = next(s['path'] for s in asset['states'] if s['state'] == 'CO')
        self.assertEqual(1, outline.count('M'))
        self.assertLessEqual(set(points(outline)), districts[1] | districts[2])
        shared = districts[1] & districts[2]
        self.assertEqual(2, len(shared))  # the two ends of the shared border
        self.assertEqual(6, len(points(outline)))  # 4 corners and the 2 ends of the shared border

    def test_a_delegate_area_is_listed_with_its_code_and_marked_and_it_has_no_shape(self):
        kml = zipfile.ZipFile(FIXTURE).read(tool.SOURCE_MEMBER).decode()
        delegate = (kml.split('<Placemark>')[1].split('</Placemark>')[0]
                    .replace('>08<', '>11<').replace('>01<', '>98<').replace('>0801<', '>1198<'))
        kml = kml.replace('  </Document>', f'    <Placemark>{delegate}</Placemark>\n  </Document>')
        with tempfile.TemporaryDirectory() as directory:
            source = zip_with(directory, kml)
            with pinned_to(source):
                asset = tool.build(tool.parse_districts(tool.read_kml(source)))
        self.assertEqual([('AK', 0, False), ('CO', 1, False), ('CO', 2, False), ('DC', 0, True)],
                         [(d['state'], d['district'], d['delegate']) for d in asset['districts']])
        self.assertEqual('', next(d['path'] for d in asset['districts'] if d['state'] == 'DC'))
        self.assertEqual(['AK', 'CO'], [s['state'] for s in asset['states']])

    def test_the_text_is_stable_and_has_one_district_on_each_line(self):
        with pinned_to(FIXTURE):
            first, second = tool.generate(FIXTURE), tool.generate(FIXTURE)
        self.assertEqual(first, second)
        self.assertTrue(first.endswith('}\n'))
        self.assertEqual(3, sum('"district":' in line for line in first.splitlines()))

    def test_a_member_with_another_size_is_rejected(self):
        result = subprocess.run([sys.executable, str(SCRIPT), '--source', str(FIXTURE), '--output', '/dev/null'],
                                capture_output=True, text=True)
        self.assertEqual(1, result.returncode)
        self.assertIn('bytes', result.stderr)
        self.assertIn(tool.SOURCE_MEMBER, result.stderr)

    def test_a_member_with_another_sha256_is_rejected_and_writes_nothing(self):
        with tempfile.TemporaryDirectory() as directory, mock.patch.object(tool, 'SOURCE_SIZE', len(zipfile.ZipFile(FIXTURE).read(tool.SOURCE_MEMBER))):
            output = Path(directory) / 'out.json'
            with contextlib.redirect_stderr(io.StringIO()) as errors:
                code = tool.main(['--source', str(FIXTURE), '--output', str(output)])
            self.assertEqual(1, code)
            self.assertIn('sha256', errors.getvalue())
            self.assertFalse(output.exists())

    def test_a_missing_census_field_is_rejected(self):
        kml = zipfile.ZipFile(FIXTURE).read(tool.SOURCE_MEMBER).decode().replace('CD119FP', 'OTHER')
        with tempfile.TemporaryDirectory() as directory:
            source = zip_with(directory, kml)
            with pinned_to(source), contextlib.redirect_stderr(io.StringIO()) as errors:
                self.assertEqual(1, tool.main(['--source', str(source), '--output', str(Path(directory) / 'o.json')]))
            self.assertIn('CD119FP', errors.getvalue())

    def test_check_passes_for_a_fresh_asset_and_fails_for_a_changed_or_missing_one(self):
        with tempfile.TemporaryDirectory() as directory, pinned_to(FIXTURE), contextlib.redirect_stdout(io.StringIO()):
            output = Path(directory) / 'districts.json'
            args = ['--source', str(FIXTURE), '--output', str(output)]
            with contextlib.redirect_stderr(io.StringIO()):
                self.assertEqual(1, tool.main([*args, '--check']))  # missing
                self.assertEqual(0, tool.main(args))
                self.assertEqual(0, tool.main([*args, '--check']))
                output.write_text(output.read_text().replace('"CO"', '"CA"', 1))
                self.assertEqual(1, tool.main([*args, '--check']))  # changed

    def test_check_writes_nothing(self):
        with tempfile.TemporaryDirectory() as directory, pinned_to(FIXTURE):
            output = Path(directory) / 'districts.json'
            with contextlib.redirect_stderr(io.StringIO()):
                tool.main(['--source', str(FIXTURE), '--output', str(output), '--check'])
            self.assertFalse(output.exists())


if __name__ == '__main__':
    unittest.main()
