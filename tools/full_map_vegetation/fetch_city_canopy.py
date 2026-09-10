"""Fetch bounded, attributed City of Boulder classified-canopy derivatives.

Raw sources stay outside the repository. Download links and the specific
CC BY 4.0 license come from the publisher's 2020 LiDAR web map, not DRCOG's
unclassified point-cloud collection or the city's general CC0 policy.
"""
import argparse
import hashlib
import json
import shutil
from pathlib import Path

import requests

ITEM = '8fb05137a9f049afa0b60d44086b19af'
INDEX = 'https://gis.bouldercolorado.gov/ags_svr1/rest/services/general/LiDARTileIndex/MapServer/0'
BASE = 'https://maps.bouldercolorado.gov/opendata/downloads/'
REPO = Path(__file__).resolve().parents[2]


def sha256(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def download(url, destination, maximum_bytes):
    if destination.exists():
        return dict(url=url, file=destination.name, bytes=destination.stat().st_size,
                    sha256=sha256(destination), reused=True)
    if shutil.disk_usage(destination.parent).free < maximum_bytes + 8 * 1024**3:
        raise RuntimeError('Insufficient disk headroom for this bounded source batch')
    partial = destination.with_suffix(destination.suffix + '.partial')
    with requests.get(url, stream=True, timeout=(20, 90)) as response:
        response.raise_for_status()
        declared = int(response.headers.get('Content-Length', 0))
        if declared > maximum_bytes:
            raise ValueError('Publisher file exceeds bounded download cap')
        count = 0
        with partial.open('wb') as stream:
            for chunk in response.iter_content(1024 * 1024):
                count += len(chunk)
                if count > maximum_bytes:
                    raise ValueError('Streaming download exceeded cap')
                stream.write(chunk)
        if declared and count != declared:
            raise ValueError('Truncated publisher payload')
        partial.replace(destination)
        return dict(url=url, file=destination.name, bytes=count, sha256=sha256(destination),
                    etag=response.headers.get('ETag'), lastModified=response.headers.get('Last-Modified'))


def fetch(output, tile, extent_only=False):
    if output == REPO or REPO in output.parents:
        raise ValueError('Raw canopy evidence must be external to the repository')
    if not tile or any(c not in 'NSEW0123456789' for c in tile):
        raise ValueError('Invalid tile identifier')
    output.mkdir(parents=True, exist_ok=True)
    previous_path = output / f'{tile}_sources.json'
    if previous_path.exists():
        previous = json.loads(previous_path.read_text(encoding='utf-8'))
        for record in previous['products']:
            cached = output / record['file']
            if cached.exists() and sha256(cached) != record['sha256']:
                raise ValueError('Cached source hash changed: ' + record['file'])
    documents = {}
    for name, url in [('publisher', f'https://www.arcgis.com/sharing/rest/content/items/{ITEM}'),
                      ('download_map', f'https://www.arcgis.com/sharing/rest/content/items/{ITEM}/data')]:
        response = requests.get(url, params={'f': 'json'}, timeout=30)
        response.raise_for_status()
        documents[name] = response.json()
        (output / (name + '.json')).write_text(json.dumps(documents[name], indent=2), encoding='utf-8')
    if 'creativecommons.org/licenses/by/4.0' not in documents['publisher'].get('licenseInfo', ''):
        raise ValueError('Publisher license changed; inspect before use')
    link_text = json.dumps(documents['download_map'])
    products = [('height', f'CHM2020/{tile}_chm.tif', 300 * 1024**2),
                ('extent', f'CanopyExtent2020/{tile}_10_extent.zip', 20 * 1024**2)]
    if extent_only:
        products = [p for p in products if p[0] == 'extent']
    metadata = requests.get(INDEX + '/query', params={
        'f': 'json', 'where': f"TileName='{tile}'", 'outFields': '*', 'returnGeometry': 'true'
    }, timeout=30).json()
    if len(metadata.get('features', [])) != 1:
        raise ValueError('Tile metadata missing or ambiguous')
    (output / f'{tile}_index.json').write_text(json.dumps(metadata, indent=2), encoding='utf-8')
    records = ([r for r in previous['products'] if r['semantic'] == 'height']
               if extent_only and previous_path.exists() else [])
    for semantic, relative, limit in products:
        template = BASE + relative.replace(tile, '{TileName}')
        if template not in link_text:
            raise ValueError('Product link absent from publisher map')
        record = download(BASE + relative, output / Path(relative).name, limit)
        record['semantic'] = semantic
        records.append(record)
        print(f'{tile} {semantic}: {record["bytes"]:,} bytes, SHA256 {record["sha256"]}', flush=True)
    manifest = dict(schema='questflight.city-canopy-sources.v1', tile=tile,
                    publisher='City of Boulder; classified by NV5 from DRCOG 2020 LiDAR',
                    license='CC-BY-4.0', licenseUrl='https://creativecommons.org/licenses/by/4.0/',
                    attribution='Canopy height and extent derived from City of Boulder / NV5 / DRCOG 2020 LiDAR, CC BY 4.0. Modified for QuestFlight; no endorsement implied.',
                    publisherItem=ITEM, acquisition='May 2020 per publisher; seasonal differences from orthophotos remain possible',
                    sourceHeight='Publisher 1-foot CHM of medium/high vegetation; verify height units in source metadata',
                    sourceExtent='Publisher vector mask of CHM cells at least 10 feet high',
                    products=records, indexAttributes=metadata['features'][0]['attributes'])
    (output / f'{tile}_sources.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--tile', default='N3W297')
    parser.add_argument('--extent-only', action='store_true')
    args = parser.parse_args()
    fetch(args.output.resolve(), args.tile, args.extent_only)
