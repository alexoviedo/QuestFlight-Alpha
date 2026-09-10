"""Keep publisher canopy topology and heights as auditable grove records.

This offline evidence compiler writes no Unity assets. Geometry is the published
classified-vegetation extent, not a union of estimated individual tree circles.
The separate 1 m height raster is a sampling aid, never the boundary authority.
"""
import argparse
import hashlib
import io
import json
import math
import sys
import zipfile
from pathlib import Path

import cv2
import numpy as np
import rasterio
from affine import Affine
from PIL import Image, ImageDraw
from pyproj import CRS, Transformer
from rasterio.features import rasterize
from rasterio.warp import reproject, Resampling
from shapely import normalize
from shapely.geometry import shape, mapping, box
from shapely.ops import transform, unary_union

REPO = Path(__file__).resolve().parents[2]
LOCAL_CRS = CRS.from_proj4('+proj=aeqd +lat_0=40.03936527 +lon_0=-105.22608958 +datum=WGS84 +units=m +no_defs')
FOOT = 1200 / 3937


def read_extent(path, target_crs=LOCAL_CRS):
    import shapefile
    with zipfile.ZipFile(path) as archive:
        names = {Path(n).suffix: n for n in archive.namelist()}
        source_crs = CRS.from_wkt(archive.read(names['.prj']).decode())
        project = Transformer.from_crs(source_crs.to_2d(), target_crs or source_crs.to_2d(), always_xy=True)
        reader = shapefile.Reader(**{kind: io.BytesIO(archive.read(names['.' + kind]))
                                     for kind in ['shp', 'shx', 'dbf']})
        geometries = []
        for item in reader.iterShapes():
            polygon = transform(project.transform, shape(item.__geo_interface__))
            if not polygon.is_valid:
                raise ValueError('Invalid source polygon; do not silently repair surveyed topology')
            geometries.append(polygon)
    merged = unary_union(geometries)
    polygons = list(merged.geoms) if merged.geom_type == 'MultiPolygon' else [merged]
    return polygons, source_crs, project.description


def imagery(bounds, pixels, root):
    x0, z0, x1, z1 = bounds
    xx = x0 + (np.arange(pixels) + .5) * (x1 - x0) / pixels
    zz = z1 - (np.arange(pixels) + .5) * (z1 - z0) / pixels
    cx = np.floor((xx + 12096) / 672).astype(int)
    rz = np.floor((zz + 12096) / 672).astype(int)
    result = np.zeros((pixels, pixels, 3), dtype=np.uint8)
    for row in np.unique(rz):
        for col in np.unique(cx):
            path = root / f'KBDU_2020_high_{col:03}_{row:03}.jpg'
            with Image.open(path) as source:
                source = np.asarray(source.convert('RGB'))
            iy, ix = np.where(rz == row)[0], np.where(cx == col)[0]
            px = np.clip(((xx[ix] + 12096 - col * 672) * 3 + 16).astype(int), 0, 2047)
            py = np.clip(((672 - (zz[iy] + 12096 - row * 672)) * 3 + 16).astype(int), 0, 2047)
            result[iy[:, None], ix[None, :]] = source[py[:, None], px[None, :]]
    return result


def polygon_id(polygon):
    return 'grove_' + hashlib.sha256(normalize(polygon).wkb).hexdigest()[:16]


def build(sources, output, tile, images, center=(-282., -828.)):
    if output.resolve() == REPO or REPO in output.resolve().parents:
        raise ValueError('Source evidence must remain outside the repository')
    output.mkdir(parents=True, exist_ok=True)
    source_manifest = json.loads((sources / f'{tile}_sources.json').read_text())
    for item in source_manifest['products']:
        with (sources / item['file']).open('rb') as stream:
            if hashlib.file_digest(stream, 'sha256').hexdigest() != item['sha256']:
                raise ValueError('Source changed: ' + item['file'])
    polygons, polygon_crs, operation = read_extent(sources / f'{tile}_10_extent.zip')
    vector_union = unary_union(polygons)
    xmin, zmin, xmax, zmax = vector_union.bounds
    xmin, zmin, xmax, zmax = math.floor(xmin), math.floor(zmin), math.ceil(xmax), math.ceil(zmax)
    affine = Affine(1, 0, xmin, 0, -1, zmax)
    heights = np.full((zmax - zmin, xmax - xmin), np.nan, dtype=np.float32)
    support = rasterize([(p, 1) for p in polygons], out_shape=heights.shape, transform=affine, dtype='uint8')
    with rasterio.open(sources / f'{tile}_chm.tif') as source:
        if abs(source.transform.a - 1) > 1e-6 or abs(source.transform.e + 1) > 1e-6:
            raise ValueError('Publisher CHM is not the expected one-foot grid')
        if 'foot' not in source.crs.linear_units.lower():
            raise ValueError('Unknown CHM units')
        # CHM values are relative vegetation heights, not NAVD88 elevations.
        # Strip the vertical CRS so GDAL cannot apply an absolute-height shift
        # or a second vertical unit conversion to this one-band raster.
        reproject(rasterio.band(source, 1), heights, src_transform=source.transform,
                  src_crs=CRS.from_wkt(source.crs.to_wkt()).to_2d().to_wkt(), src_nodata=source.nodata,
                  dst_transform=affine, dst_crs=LOCAL_CRS.to_wkt(), dst_nodata=np.nan,
                  resampling=Resampling.nearest)
        heights *= FOOT
        source_bounds = dict(zip(['left', 'bottom', 'right', 'top'], source.bounds))
        source_crs_wkt = source.crs.to_wkt()
        to_local = Transformer.from_crs(CRS.from_wkt(source_crs_wkt).to_2d(), LOCAL_CRS, always_xy=True)
        source_footprint = transform(to_local.transform, box(*source.bounds))
    records = []
    for p in polygons:
        if p.area < 4:
            continue
        x0, y0, x1, y1 = p.bounds
        a = max(0, math.floor(x0) - xmin); b = min(heights.shape[1], math.ceil(x1) - xmin)
        c = max(0, zmax - math.ceil(y1)); d = min(heights.shape[0], zmax - math.floor(y0))
        mask = rasterize([(p, 1)], out_shape=(d-c, b-a), transform=affine * Affine.translation(a,c), dtype='uint8')
        values = heights[c:d, a:b][mask != 0]
        values = values[np.isfinite(values)]
        records.append(dict(id=polygon_id(p), geometry=mapping(p), areaMeters2=p.area,
                            holes=len(p.interiors), touchesSourceEdge=p.distance(source_footprint.boundary) < 1.,
                            heightP50=float(np.median(values)) if len(values) else None,
                            heightP95=float(np.quantile(values,.95)) if len(values) else None,
                            measuredHeightSamples=len(values), membersAreNotTreeCenters=True))
    np.savez_compressed(output / 'height_evidence.npz', height=heights, support=support,
                        xmin=xmin, zmax=zmax, spacing=1.)
    (output / 'groves.geojson').write_text(json.dumps(dict(type='FeatureCollection',
        coordinateReference=LOCAL_CRS.to_wkt(), features=[dict(type='Feature', geometry=r['geometry'],
        properties={k:v for k,v in r.items() if k!='geometry'}) for r in records])), encoding='utf-8')
    # Paired evidence at the exact previous trial location, now much wider.
    bounds = [center[0]-160, center[1]-160, center[0]+160, center[1]+160]
    photo = imagery(bounds, 960, images)
    Image.fromarray(photo).save(output / 'source.png')
    crop_affine = Affine(1/3,0,bounds[0],0,-1/3,bounds[3])
    selected = [(p,1) for p in polygons if p.intersects(box(*bounds))]
    crop_mask = (rasterize(selected, out_shape=(960,960), transform=crop_affine, dtype='uint8')
                 if selected else np.zeros((960,960), dtype=np.uint8))
    coverage = rasterize([(source_footprint,1)], out_shape=(960,960), transform=crop_affine, dtype='uint8')
    contours,_ = cv2.findContours(crop_mask, cv2.RETR_CCOMP, cv2.CHAIN_APPROX_SIMPLE)
    overlay = photo.copy(); cv2.drawContours(overlay,contours,-1,(20,255,220),2)
    yy, xx = np.indices(coverage.shape)
    overlay[(coverage == 0) & ((xx+yy)%24 < 5)] = (255,75,180)
    Image.fromarray(overlay).save(output / 'extent_overlay.png')
    Image.fromarray(crop_mask*255).save(output / 'extent_mask.png')
    Image.fromarray(coverage*255).save(output / 'source_coverage.png')
    native_grid = np.meshgrid(bounds[0]+(np.arange(960)+.5)/3, bounds[3]-(np.arange(960)+.5)/3)
    xx = np.clip((native_grid[0]-xmin).astype(int),0,heights.shape[1]-1)
    zz = np.clip((zmax-native_grid[1]).astype(int),0,heights.shape[0]-1)
    crop_height = heights[zz,xx].copy()
    crop_height[coverage == 0] = np.nan
    cmap = cv2.applyColorMap(np.uint8(np.clip(np.nan_to_num(crop_height)/25,0,1)*255),cv2.COLORMAP_TURBO)[:,:,::-1]
    cmap[crop_mask==0] = 20
    cmap[coverage==0] = (110,35,85)
    Image.fromarray(cmap).save(output / 'height.png')
    focused = [r for r in records if shape(r['geometry']).intersects(box(-325,-860,-245,-800))]
    (output / 'pilot_components.json').write_text(json.dumps(focused, indent=2),encoding='utf-8')
    report = dict(schema='questflight.connected-grove-evidence.v1', source=source_manifest,
        unitScaleMeters=FOOT, sourceHorizontalAndVerticalCrs=source_crs_wkt,
        sourceBounds=source_bounds, localBounds=[xmin,zmin,xmax,zmax],
        sourceFootprint=mapping(source_footprint),
        reviewBounds=bounds, reviewCoverageFraction=float(coverage.mean()),
        sourceComponents=len(polygons), retainedComponents=len(records),
        minimumRetainedAreaMeters2=4, retainedAreaMeters2=sum(r['areaMeters2'] for r in records),
        connectivity='Exact union of touching publisher extent polygons; holes preserved, no gap dilation, no per-tree circles.',
        height='Publisher normalized medium/high vegetation CHM in US survey feet converted once to metres, horizontally reprojected with 2D CRS and nearest sampled at 1 m; nodata remains NaN. No absolute vertical-datum transformation.',
        referenceDate='2020 LiDAR versus 2020 orthophotos; exact dates differ.',
        annotationGate='PENDING_INDEPENDENT_REFERENCE_LABELS',
        status='SOURCE_COMPILED_REQUIRES_INSPECTION_NOT_PRODUCT_ACCEPTANCE')
    (output / 'evidence.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print(json.dumps({k:report[k] for k in ['sourceComponents','retainedComponents','retainedAreaMeters2','localBounds']}),flush=True)


if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('--sources',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--images',type=Path,required=True)
    parser.add_argument('--tile',default='N3W297')
    parser.add_argument('--deps',type=Path)
    parser.add_argument('--center',type=float,nargs=2,default=(-282.,-828.))
    args=parser.parse_args()
    if args.deps:sys.path.insert(0,str(args.deps))
    build(args.sources,args.output,args.tile,args.images,args.center)
