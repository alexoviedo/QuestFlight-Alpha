"""Bounded offline canopy experiment. Outputs stay external; no product writes.

Uses the accepted V3 placements and verified orthophoto page, never a coarse
runtime mask as the footprint. Heights/trunks inherit estimated source records.
This is a prototype compiler, not an approved map-wide replacement.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import struct
from pathlib import Path

import cv2
import numpy as np
from scipy.spatial import Delaunay

from build_full_map_vegetation import BuildingIndex, canopy_mask, rasterize_buildings
from compile_vegetation_hlod_v3 import HEADER, PAGE, INSTANCE, SemanticExclusion


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def placements(path, bounds):
    data = path.read_bytes()
    magic, version, xmin, zmin, size, quantum, columns, rows, count = HEADER.unpack_from(data)
    if (magic, version) != (b"QFFMVG3\0", 3):
        raise ValueError("Expected accepted V3 placement database")
    start = HEADER.size + columns * rows * PAGE.size
    if len(data) != start + count * INSTANCE.size:
        raise ValueError("Invalid placement byte length")
    result = []
    for page in range(columns * rows):
        offset, length = PAGE.unpack_from(data, HEADER.size + page * PAGE.size)
        for local in range(length):
            record = INSTANCE.unpack_from(data, start + (offset + local) * INSTANCE.size)
            x = xmin + page % columns * size + record[0] * quantum
            z = zmin + page // columns * size + record[1] * quantum
            if record[10] != 3 and bounds[0] <= x <= bounds[2] and bounds[1] <= z <= bounds[3]:
                result.append(dict(id=offset + local, x=x, z=z, y=record[2] * .01,
                                   height=record[3] * .01, crown=record[4] * .01,
                                   color=list(record[6:9]), kind=record[10]))
    return result


def mesh_patch(mask, records, xmin, zmax, mpp, spacing=4.):
    """Triangulate only supported canopy, retaining holes and concave boundaries."""
    contours, _ = cv2.findContours(mask, cv2.RETR_LIST, cv2.CHAIN_APPROX_SIMPLE)
    boundary = [cv2.approxPolyDP(c, .7 / mpp, True).reshape(-1, 2) for c in contours if len(c) >= 3]
    if not boundary:
        raise ValueError("Empty canopy boundary")
    yy, xx = np.mgrid[0:mask.shape[0]:spacing / mpp, 0:mask.shape[1]:spacing / mpp]
    grid = np.column_stack((xx.ravel(), yy.ravel()))
    points = np.unique(np.vstack(boundary + [grid]), axis=0)
    ij = np.rint(points).astype(int)
    valid = (ij[:, 0] >= 0) & (ij[:, 0] < mask.shape[1]) & (ij[:, 1] >= 0) & (ij[:, 1] < mask.shape[0])
    points = points[valid & (mask[np.clip(ij[:, 1], 0, mask.shape[0]-1), np.clip(ij[:, 0], 0, mask.shape[1]-1)] > 0)]
    triangulation = Delaunay(points)
    triangles = []
    # Test full raster coverage of every triangle; centroid-only tests bridge holes.
    for tri in triangulation.simplices:
        coords = np.rint(points[tri]).astype(np.int32)
        x, y, w, h = cv2.boundingRect(coords)
        coverage = np.zeros((h, w), np.uint8)
        cv2.fillConvexPoly(coverage, coords - [x, y], 1)
        if np.all(mask[y:y+h, x:x+w][coverage != 0] != 0):
            triangles.append(tri.tolist())
    if not triangles:
        raise ValueError("No triangles preserve the source footprint")
    world = np.column_stack((xmin + points[:, 0] * mpp, zmax - points[:, 1] * mpp))
    centers = np.array([[r['x'], r['z']] for r in records])
    radii = np.array([r['crown'] * .5 for r in records])
    d2 = np.sum((world[:, None, :] - centers[None, :, :]) ** 2, axis=2)
    normalized = d2 / np.maximum(radii[None, :] ** 2, 1)
    ground = np.array([r['y'] for r in records])
    heights = np.array([r['height'] for r in records])
    crowns = ground + heights * (.53 + .47 * np.sqrt(np.maximum(0, 1 - normalized)))
    crowns[normalized > 1.4] = -1e9
    nearest = np.argmin(normalized, axis=1)
    owner = np.argmax(crowns, axis=1)
    unsupported = np.max(crowns, axis=1) < -1e8
    owner[unsupported] = nearest[unsupported]
    top = crowns[np.arange(len(points)), owner]
    top[unsupported] = ground[owner[unsupported]] + heights[owner[unsupported]] * .55
    bottom = ground[owner] + heights[owner] * .50
    vertices = [[float(x), float(y), float(z)] for (x, z), y in zip(world, top)]
    colors = [[0.] + [c/255 for c in records[i]['color']] for i in owner]
    indices = []
    edges = {}
    for tri in triangles:
        a, b, c = tri
        # World Z reverses raster Y. Explicitly enforce upward winding.
        if np.cross(np.array(vertices[b])-vertices[a], np.array(vertices[c])-vertices[a])[1] < 0:
            b, c = c, b
        indices.extend([a, b, c])
        for edge in ((a,b), (b,c), (c,a)):
            key = tuple(sorted(edge))
            edges.setdefault(key, []).append(edge)
    # Rounded, tapered underside at the boundary; never extend a wall to ground.
    for uses in edges.values():
        if len(uses) != 1:
            continue
        a, b = uses[0]
        base = len(vertices)
        for index in (a, b):
            center = centers[owner[index]]
            xz = world[index] * .88 + center * .12
            vertices.append([float(xz[0]), float(bottom[index]), float(xz[1])])
            colors.append(colors[index])
        indices.extend([a, base, base+1, a, base+1, b])
    canopy_triangles = len(indices)//3
    distance = cv2.distanceTransform(mask, cv2.DIST_L2, 5) * mpp
    trunks = 0
    for record in records:
        px = int(round((record['x']-xmin)/mpp))
        py = int(round((zmax-record['z'])/mpp))
        if distance[py, px] > record['crown'] * .7:
            continue
        base = len(vertices)
        radius = max(.12, record['height']*.013)
        for y, scale in ((record['y'], 1.), (record['y']+record['height']*.57, .55)):
            for angle in np.arange(5)*np.pi*2/5:
                vertices.append([record['x']+np.cos(angle)*radius*scale, y, record['z']+np.sin(angle)*radius*scale])
                colors.append([1., .3, .24, .18])
        for i in range(5):
            j = (i+1)%5
            indices.extend([base+i, base+i+5, base+j+5, base+i, base+j+5, base+j])
        trunks += 1
    # Discard unreferenced points so budget accounting matches the actual mesh.
    used = sorted(set(indices))
    remap = {old: new for new, old in enumerate(used)}
    return dict(vertices=[dict(x=round(vertices[i][0],4), y=round(vertices[i][1],4), z=round(vertices[i][2],4)) for i in used],
                colors=[dict(r=colors[i][0],g=colors[i][1],b=colors[i][2],a=colors[i][3]) for i in used],
                indices=[remap[i] for i in indices], canopy_triangles=canopy_triangles, trunks=trunks)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--repo', type=Path, required=True)
    parser.add_argument('--imagery', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    repo = args.repo.resolve()
    output = args.output.resolve()
    if output == repo or repo in output.parents:
        raise ValueError('Raw imagery and experimental meshes must stay outside the repository')
    output.mkdir(parents=True, exist_ok=True)
    root = repo/'QuestFlight/Assets'
    manifest_path = root/'StreamingAssets/QuestFlightAerialClipmap/clipmap_manifest.json'
    manifest = json.loads(manifest_path.read_text())
    page = next(p for p in manifest['pages'] if p['tier']=='high' and p['column']==17 and p['row']==16)
    source = args.imagery/(page['asset_name']+'.jpg')
    if digest(source) != page['sha256']:
        raise ValueError('Prototype requires the exact accepted imagery page')
    source_bgr = cv2.imread(str(source))
    gutter = page['gutter_pixels']
    analysis = cv2.resize(source_bgr[gutter:-gutter,gutter:-gutter], (1008,1008), interpolation=cv2.INTER_AREA)
    high = manifest['tiers']['high']
    xmin = high['grid_min_x']+17*high['tile_inner_size_meters']
    zmin = high['grid_min_z']+16*high['tile_inner_size_meters']
    zmax = zmin+high['tile_inner_size_meters']
    mpp = high['tile_inner_size_meters']/analysis.shape[0]
    building_path = root/'Production/Environment/FullMapBuildingsV1/KBDU_FullMapBuildingDatabaseV1.json'
    building = rasterize_buildings(BuildingIndex(building_path),xmin,zmin,xmin+672,zmax,analysis.shape[:2])
    mask, confidence = canopy_mask(analysis, building)
    database = root/'Production/Environment/FullMapVegetationV3/KBDU_FullMapVegetationHlodV3.bytes'
    exclusion_path = root/'Production/Environment/FullMapVegetationV3/KBDU_VegetationSemanticExclusionV1.png'
    exclusion = SemanticExclusion(exclusion_path)
    records = placements(database, [xmin,zmin,xmin+672,zmax])
    support = np.zeros_like(mask)
    for record in records:
        px = round((record['x']-xmin)/mpp)
        py = round((zmax-record['z'])/mpp)
        cv2.circle(support, (px,py), max(1,round(record['crown']*.55/mpp)), 255, -1)
    mask[support==0] = 0
    yy, xx = np.indices(mask.shape)
    exsize = exclusion.excluded.shape[0]
    exx = np.clip(np.rint((xmin+xx*mpp+12000)/24000*(exsize-1)).astype(int),0,exsize-1)
    exy = np.clip(np.rint((12000-(zmax-yy*mpp))/24000*(exsize-1)).astype(int),0,exsize-1)
    mask[exclusion.excluded[exy,exx]] = 0
    count, labels, stats, centers = cv2.connectedComponentsWithStats(mask)
    groups = []
    for label in range(1,count):
        if not 150 <= stats[label,cv2.CC_STAT_AREA]*mpp*mpp <= 5000:
            continue
        members = []
        for r in records:
            px,py = round((r['x']-xmin)/mpp),round((zmax-r['z'])/mpp)
            if 0<=px<1008 and 0<=py<1008 and labels[py,px]==label and r['kind']!=2:
                members.append(r)
        center = (xmin+centers[label][0]*mpp,zmax-centers[label][1]*mpp)
        if len(members)>=6 and -914<center[1]<-680 and -650<center[0]<-135:
            groups.append((len(members),label,members))
    if not groups:
        raise ValueError('No supported dense patch in the existing Valmont reference bounds')
    _, label, members = max(groups,key=lambda g:(g[0],-g[1]))
    selected = np.uint8(labels==label)*255
    mesh = mesh_patch(selected,members,xmin,zmax,mpp)
    mesh['source_records'] = members
    mesh['schema'] = 'questflight.hybrid-canopy-prototype.v1'
    mesh['source_identity'] = [dict(path=str(p),sha256=digest(p)) for p in (source,manifest_path,database,building_path,exclusion_path)]
    mesh['height_provenance'] = 'Accepted V3 record estimates; no new measured height claim'
    mesh['metrics'] = dict(source_crowns=len(members), footprint_area_m2=int(np.count_nonzero(selected))*mpp*mpp,
                           triangles=len(mesh['indices'])//3, vertices=len(mesh['vertices']),
                           proposed_vertex_index_bytes=len(mesh['vertices'])*36+len(mesh['indices'])*2,
                           additional_materials=0, additional_chunk_draws=1,
                           note='Static prototype only; actual replaced draws and triangles require selected-view accounting')
    (output/'prototype.json').write_text(json.dumps(mesh,separators=(',',':')))
    preview = analysis.copy()
    preview[selected>0] = preview[selected>0]*.55 + np.array([30,110,30])*.45
    cv2.imwrite(str(output/'footprint.png'),preview)
    cv2.imwrite(str(output/'footprint_mask.png'),selected)
    print(json.dumps(mesh['metrics']))


if __name__ == '__main__':
    main()
