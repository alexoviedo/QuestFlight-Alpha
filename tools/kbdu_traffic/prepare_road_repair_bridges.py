"""Recover the confirmed D-16-DG railway overpass omitted by the road-only inventory.

Offline only. Elevations are an approach-anchored reconstruction, not surveyed
deck heights or a claim of verified railway clearance. Raw sources stay external.
"""
import argparse
import copy
import json
from pathlib import Path

import numpy as np
from PIL import Image
from pyproj import Transformer
from scipy.ndimage import map_coordinates

ROOT = Path(__file__).resolve().parents[2]


def build(work, inventory, osm):
    doc = json.loads((work / 'baseline/KBDU_GradeSeparatedRoadAuthorityV1.json').read_text())
    inv = json.loads(inventory.read_text())
    elements = {str(e['id']): e for e in json.loads(osm.read_text())['elements']}
    env = ROOT / 'QuestFlight/Assets/Production/Environment'
    dtm = np.asarray(Image.open(env / 'AerialSceneryV1/Lidar/KBDU_Lidar_DTM_R16.png')) * .05 + 1000 - 1611.7824
    crs = '+proj=aeqd +lat_0=40.039365270000 +lon_0=-105.226089580000 +datum=WGS84 +units=m +no_defs'
    project = Transformer.from_crs(4326, crs, always_xy=True)
    utm = Transformer.from_crs(crs, 32613, always_xy=True)

    def height(p):
        e, n = utm.transform(p[0], p[1])
        return float(map_coordinates(dtm, [[(2046 - (n - 4432150.874940165)) / 4],
                                           [(e - 480712.1393769088 + 2046) / 4]], order=1)[0])

    rejected = [r for r in inv['rejected_upper_ways'] if r.get('cdot', {}).get('structure_id') == 'D-16-DG']
    aliases = sorted({r for f in rejected for r in f['upper_route_ids']})
    for way, lanes, z0, z1 in [('43417343', 2, -530, -345), ('43417357', 3, -535, -355)]:
        e = elements[way]
        p = np.array([project.transform(g['lon'], g['lat']) for g in e['geometry']])
        # Northbound uses a centered source line. Southbound's right_of:1 tag
        # places the line between lanes 1 and 2, 1.75 m left of pavement center.
        center_x = float(np.mean(p[:, 0])) - (1.75 if lanes == 3 else 0)
        endpoints = [np.array([center_x, z0]), np.array([center_x, z1])]
        h0, h1 = map(height, endpoints)
        z = np.linspace(z0, z1, int((z1-z0)/5)+1)
        # Bare-earth bridge removal creates deep holes, not a drivable profile.
        # The least concave majorant preserves the intact, smoothly cresting
        # approach samples while spanning those missing-deck holes.
        y = np.array([max(height(np.array([center_x+dx, zz])) for dx in [-3.5,-1.75,0,1.75,3.5]) for zz in z])
        hull=[]
        for i in range(len(z)):
            while len(hull)>1 and (y[hull[-1]]-y[hull[-2]])/(z[hull[-1]]-z[hull[-2]]) < (y[i]-y[hull[-1]])/(z[i]-z[hull[-1]]):
                hull.pop()
            hull.append(i)
        y=np.interp(z,z[hull],y[hull])+.04
        xyz = np.column_stack([np.full_like(z, center_x), y, z])
        if lanes == 3:
            xyz = xyz[::-1].copy()
        length = z1-z0
        direction = (xyz[-1]-xyz[0]) / np.linalg.norm(xyz[-1]-xyz[0])
        normal = np.array([0., 1., 0.]) - direction*direction[1]
        normal /= np.linalg.norm(normal)
        profile = copy.deepcopy(doc['profiles'][0])
        profile.update(span_id=f'KBDU-GSR-D-16-DG-OSM-{way}', source_osm_way_id=way,
                       source_cdot_structure_id='D-16-DG', source_lanes=str(lanes),
                       source_facility='SH 157 ML', source_feature_intersected='BNSF RR',
                       source_year_built=1988, lower_feature_kind='railway',
                       source_width='', surface_width_meters=lanes*3.5+2,
                       minimum_clearance_meters=0, clearance_evidence_values_meters=[],
                       vertical_authority='LIDAR_APPROACH_ANCHORS_RAIL_CLEARANCE_UNVERIFIED',
                       maximum_grade=float(np.max(np.abs(np.diff(y)/np.diff(z)))), profile_length_meters=length,
                       samples_xyz_meters=xyz.flatten().tolist(),
                       sample_normals_xyz=np.tile(normal, (len(xyz), 1)).flatten().tolist(),
                       upper_route_ids=aliases, lower_route_ids=[],
                       bridge_interval_meters=[22., length-22.],
                       suppressed_old_visual_interval_meters=[22., length-22.],
                       minimum_xz_distance_meters=8, approach_join_distances_meters=[])
        doc['profiles'].append(profile)
    doc['road_repair_sources'] = dict(inventory=str(inventory), osm=str(osm),
        railway_bridge='CDOT D-16-DG, 1988, BNSF RR; existing 2020 aerial corroboration',
        elevation='Concave upper envelope of intact lidar roadway/approach samples across missing-deck holes; rail clearance unverified',
        licenses='Existing OSM ODbL and USGS public-domain data; existing CDOT inventory source retained')
    (work/'authority_input.json').write_text(json.dumps(doc, indent=2))
    print('Prepared', len(doc['profiles']), 'profiles, including two railway carriageways')


if __name__ == '__main__':
    p = argparse.ArgumentParser()
    p.add_argument('--work', type=Path, required=True)
    p.add_argument('--inventory', type=Path, required=True)
    p.add_argument('--osm', type=Path, required=True)
    a = p.parse_args()
    build(a.work, a.inventory, a.osm)
