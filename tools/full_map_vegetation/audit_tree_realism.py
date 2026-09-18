"""Audit the generated map against preserved source identities and Unity terrain.

Read-only; original branch pages must be available in the specified Git ref.
Fails if that ref differs from the manifest saved before authoring.
"""
import argparse
import hashlib
import json
import struct
import subprocess
from pathlib import Path
import numpy as np
from enrich_tree_realism import CELL, TREE


def audit(root, repo, ref):
    baseline = root / 'baseline'
    generated = root / 'generated'
    manifest = json.loads((baseline / 'source_branches.json').read_text(encoding='utf-8-sig'))
    current = json.loads((generated / 'manifest.json').read_text())
    assert len(manifest['pages']) == len(current['pages']) == 1296
    process = subprocess.Popen(['git', 'cat-file', '--batch'], cwd=repo,
                               stdin=subprocess.PIPE, stdout=subprocess.PIPE)
    count = 0
    git_dir = Path(subprocess.check_output(['git', 'rev-parse', '--git-common-dir'], cwd=repo, text=True).strip())
    if not git_dir.is_absolute():
        git_dir = repo / git_dir
    try:
        for old, new in zip(manifest['pages'], current['pages']):
            assert old['file'] == new['file']
            path = 'QuestFlight/Assets/StreamingAssets/QuestFlightBranchGroves/' + old['file']
            process.stdin.write((ref + ':' + path + '\n').encode())
            process.stdin.flush()
            info = process.stdout.readline().split()
            assert len(info) == 3 and info[1] == b'blob', info
            source = process.stdout.read(int(info[2]))
            assert process.stdout.read(1) == b'\n'
            if source.startswith(b'version https://git-lfs.github.com/spec/v1'):
                oid = source.decode().split('oid sha256:')[1].splitlines()[0]
                source = (git_dir / 'lfs/objects' / oid[:2] / oid[2:4] / oid).read_bytes()
            assert hashlib.sha256(source).hexdigest() == old['sha256'], old['file']
            target = (generated / new['file']).read_bytes()
            shipped = (repo / path).read_bytes()
            assert target == shipped
            assert hashlib.sha256(target).hexdigest() == new['sha256']
            assert source[:16] == target[:16] and len(source) == len(target)
            a = np.frombuffer(source, CELL, offset=16)
            b = np.frombuffer(target, CELL, offset=16)
            for field in ('cell', 'radius', 'rgb', 'grove'):
                assert np.array_equal(a[field], b[field]), (old['file'], field)
            assert np.array_equal(a['source'] & 1, b['source'] & 1)
            assert np.all((b['source'] >> 1) <= 2)
            terrain = np.fromfile(baseline / 'terrain' / old['file'], '<f4').reshape(-1, 2)
            assert np.array_equal(b['ground'], terrain[:, 0])
            assert np.all(np.isfinite(b['height'])) and np.all((b['height'] >= 4) & (b['height'] <= 40))
            count += len(b)
    finally:
        process.stdin.close()
        process.wait()
    source = (baseline / 'source_trees.bytes').read_bytes()
    target = (generated / 'KBDU_FullMapVegetationHlodV3.bytes').read_bytes()
    nx, nz = struct.unpack_from('<II', source, 28)
    offset = 40 + nx * nz * 8
    assert source[:offset] == target[:offset] and len(source) == len(target)
    a = np.frombuffer(source, TREE, offset=offset)
    b = np.frombuffer(target, TREE, offset=offset)
    for field in ('x', 'z', 'h', 'c', 'rot', 'rgb', 'kind'):
        assert np.array_equal(a[field], b[field]), field
    assert np.array_equal(a['seed'] & 63, b['seed'] & 63)
    assert np.all((b['seed'] >> 6) <= 2)
    terrain = np.fromfile(baseline / 'terrain/tree_roots.bytes', '<f4').reshape(-1, 2)
    assert np.max(np.abs(b['y'] * .01 - terrain[:, 0])) <= .0051
    report = dict(result='PASS', pages=1296, cells=count, trees=len(b),
                  originalGitRef=ref, footprintsIdsCountsAndDimensionsPreserved=True,
                  exactAuthoredTerrainGrounding=True, sourceHashesVerified=True,
                  visualAndQuestAcceptance='NOT_ESTABLISHED_BY_THIS_AUDIT')
    (root / 'data_audit.json').write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps(report))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--root', type=Path, required=True)
    parser.add_argument('--repo', type=Path, required=True)
    parser.add_argument('--ref', default='HEAD')
    args = parser.parse_args()
    audit(args.root, args.repo, args.ref)
