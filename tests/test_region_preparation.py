"""Offline producer evidence: numeric seams, bounded grids and complete regional roads."""
import gzip
import importlib.util
import json
import pathlib
import tempfile
import unittest
from unittest import mock
import io
import sys
import os
import random
import hashlib
import numpy as np
import osmium

ROOT = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools/TmapTool'))
spec = importlib.util.spec_from_file_location('center_region', ROOT / 'tools/TmapTool/prepare_region.py')
producer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(producer)


class RegionPreparationTests(unittest.TestCase):
    def test_matches_frozen_released_sdk_reference_cases_without_network(self):
        from region_contours import extract_contours
        reference = json.loads((ROOT / 'tests/fixtures/region-contour-reference.json').read_text())
        for case in reference['cases']:
            contours = extract_contours(case['dem'], case['interval_m'])
            digest = hashlib.sha256(json.dumps(contours, sort_keys=True, separators=(',', ':')).encode()).hexdigest()
            self.assertEqual(case['contours_sha256'], digest)

    @unittest.skipUnless(os.environ.get('TMAP_SDK_TOOLS'), 'Set TMAP_SDK_TOOLS to the released SDK producer directory')
    def test_candidate_filter_matches_released_sdk_on_saddles_gaps_and_random_grids(self):
        sys.path.insert(0, os.environ['TMAP_SDK_TOOLS'])
        from tmap_pack import extract_contours as reference
        from region_contours import extract_contours
        rng = random.Random(20261015)
        grids = [[[0, 10], [10, 0]], [[None, 10], [0, 20]], [[0, 0], [0, 0]], [[None, None], [None, None]]]
        grids.extend([[rng.randint(-25, 50) for x in range(9)] for y in range(8)] for i in range(20))
        for grid in grids:
            dem = dict(origin=[-0.15, 51.5], step=[0.001, 0.001], heights=grid)
            self.assertEqual(reference(dem, 5), extract_contours(dem, 5))

    def test_contour_work_budget_is_enforced(self):
        from region_contours import extract_contours
        with self.assertRaisesRegex(ValueError, 'segment budget'):
            extract_contours(dict(origin=[0, 0], step=[0.001, 0.001], heights=[[-10, 10], [-10, 10]]), 5, maximum_segments=1)

    def test_contours_do_not_cross_unselected_region_cells(self):
        from region_contours import extract_contours
        dem = dict(origin=[0, 0], step=[0.001, 0.001], heights=[[-10, 10], [-10, 10]])
        self.assertTrue(extract_contours(dem, 5))
        self.assertEqual([], extract_contours(dem, 5, coverage=np.array([[True, True], [True, False]])))

    def test_mosaic_uses_both_numeric_sources_at_seam(self):
        with tempfile.TemporaryDirectory() as folder:
            output = pathlib.Path(folder)
            for name, value in [('N51W001', 100), ('N51E000', 200)]:
                (output / (name + '.hgt.gz')).write_bytes(gzip.compress(np.full((1201, 1201), value, dtype='>i2').tobytes()))
            plan = dict(Area=dict(Name='Seam', Bounds=dict(West=-0.002, South=51.1, East=0.002, North=51.102)),
                GridStepDegrees=0.001, MaximumDemSamples=100, ElevationCacheDirectory=folder, ContourIntervalMeters=10)
            report = producer.terrain(plan, output)
            document = json.loads((output / 'terrain.json').read_text())
            self.assertEqual(2, len(report['sources']))
            self.assertEqual(0, report['missing_samples'])
            self.assertEqual([100, 100, 200, 200, 200], document['dem']['heights'][0])
            self.assertEqual(112, document['dem']['resolution_m'])
            self.assertEqual(0, document['dem']['vertical_datum'])

    def test_dem_budget_fails_before_network_access(self):
        with tempfile.TemporaryDirectory() as folder:
            plan = dict(Area=dict(Bounds=dict(West=0, South=0, East=1, North=1)), GridStepDegrees=0.001,
                        MaximumDemSamples=10)
            with self.assertRaisesRegex(ValueError, 'sample budget'):
                producer.terrain(plan, pathlib.Path(folder))

    def test_hgt_dimension_validation_is_bounded(self):
        with self.assertRaisesRegex(ValueError, 'dimensions'):
            producer.validate_hgt(gzip.compress(b'bad source'))

    def test_truncated_download_is_retried_before_cache_publication(self):
        with tempfile.TemporaryDirectory() as folder:
            output = pathlib.Path(folder)
            valid = gzip.compress(np.full((1201, 1201), 42, dtype='>i2').tobytes())
            responses = [io.BytesIO(valid[:-8]), io.BytesIO(valid)]
            for response in responses:
                response.headers = {'Content-Length': str(response.getbuffer().nbytes)}
            plan = dict(Area=dict(Name='Retry', Bounds=dict(West=0.1, South=51.1, East=0.102, North=51.102)),
                GridStepDegrees=0.001, MaximumDemSamples=100, ElevationCacheDirectory=folder, ContourIntervalMeters=10)
            with mock.patch.object(producer.urllib.request, 'urlopen', side_effect=responses) as request, mock.patch.object(producer.time, 'sleep'):
                result = producer.terrain(plan, output)
            self.assertEqual(2, request.call_count)
            self.assertEqual(0, result['missing_samples'])
            self.assertTrue((output / 'N51E000.hgt.gz').exists())
            self.assertEqual([], list(output.glob('*.part')))

    def test_complete_boundary_way_and_conditional_closure(self):
        with tempfile.TemporaryDirectory() as folder:
            output = pathlib.Path(folder)
            source = output / 'source.osm.pbf'
            with osmium.SimpleWriter(source) as writer:
                for identity, lon in [(1, 0.1), (2, 0.2), (3, 0.8)]:
                    writer.add_node(osmium.osm.mutable.Node(id=identity, location=(lon, 51.1), tags={}))
                writer.add_way(osmium.osm.mutable.Way(id=10, nodes=[1, 3], tags={'highway': 'path'}))
                writer.add_way(osmium.osm.mutable.Way(id=11, nodes=[1, 2], tags={'highway': 'path', 'access:conditional': 'no @ (wet)'}))
            plan = dict(PbfPath=str(source), Area=dict(Bounds=dict(West=0, South=51, East=0.5, North=51.5)), ExcludeConditionalFeatures=True)
            report = producer.crop_roads(plan, output)
            self.assertEqual(1, report['ways'])
            self.assertEqual(1, report['excluded_conditional_ways'])
            self.assertEqual(2, report['nodes'])
            nodes, ways = [], []
            for obj in osmium.FileProcessor(output / 'region.osm.pbf'):
                if obj.is_node(): nodes.append(obj.id)
                if obj.is_way(): ways.append((obj.id, [node.ref for node in obj.nodes]))
            self.assertEqual([1, 3], nodes)
            self.assertEqual([(10, [1, 3])], ways)


if __name__ == '__main__':
    unittest.main()
