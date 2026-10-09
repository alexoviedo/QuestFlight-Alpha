"""Focused binary-evidence contract tests; fake files never establish Unity validation."""
from pathlib import Path
import ast
import collections
from dataclasses import dataclass
import hashlib
import json
import math
import tempfile
import unittest
from typing import Any, Iterable, Mapping

from tools.aerial_clipmap import validate_android_bundle_inventory as validator

ROOT = Path(__file__).resolve().parents[3]


class BundleInventoryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="qf-bundle-contract-")
        self.addCleanup(self.temp.cleanup)
        self.directory = Path(self.temp.name)
        generator = ROOT / "tools/aerial_clipmap/build_full_map_clipmap.py"
        # Reuse exactly the completed diagnostic's isolated tile-spec extraction.
        names = {"canonical_json", "sha256_bytes", "TileSpec", "tier_specs", "global_spec"}
        definitions = [node for node in ast.parse(generator.read_text()).body
                       if isinstance(node, (ast.FunctionDef, ast.ClassDef)) and node.name in names]
        namespace = dict(globals())
        exec(compile(ast.Module(body=definitions, type_ignores=[]), str(generator), "exec"), namespace)
        config = json.loads((ROOT / "tools/aerial_clipmap/config.json").read_text())
        tiles = namespace["tier_specs"](config, "high") + namespace["tier_specs"](config, "medium") + [namespace["global_spec"](config)]
        self.document = {"mode": "full", "source": {"item": {"id": config["source"]["arcgis_item_id"]}},
                         "pages": [dict(tier=t.tier, asset_name=t.asset_name, bundle_name=t.bundle_name, output_pixels=[128,128]) for t in tiles]}
        self.manifest = self.directory / "pages.json"
        self.manifest.write_text(json.dumps(self.document))
        self.grouped = collections.defaultdict(list)
        for page in self.document["pages"]:
            self.grouped[page["bundle_name"]].append(page)
        self.assertEqual(1873, len(tiles))
        self.assertEqual(469, len(self.grouped))
        self.write_bundles(b"UnityFS\0" + bytes(range(64)))

    def write_bundles(self, payload):
        for name, pages in self.grouped.items():
            (self.directory / name).write_bytes(payload)
            (self.directory / f"{name}.manifest").write_text("Assets:\n" + "".join(
                f"- Assets/Generated/FullMapAerialClipmap/{page['tier']}/{page['asset_name']}.jpg\n" for page in pages) + "Dependencies: []\n")

    def proof(self):
        # This synthetic report tests parser binding, not actual binary correctness.
        # Positive real binary load/asset validation is separately exercised in Unity.
        version = next(line.split(": ",1)[1] for line in (ROOT/"QuestFlight/ProjectSettings/ProjectVersion.txt").read_text().splitlines() if line.startswith("m_EditorVersion: "))
        compilers = ["QuestFlight/Assets/Editor/FullMapAerialClipmapAssetAuthoring.cs", "QuestFlight/Assets/Editor/AuthoringBundleVerifier.cs"]
        return {"schema":"questflight.bundle-verification.v1", "build_target":"Android", "verified":True,
                "unity_version":version, "manifest_sha256":validator.sha256_file(self.manifest),
                "compiler_inputs":[{"path":path,"sha256":validator.sha256_file(ROOT/path)} for path in compilers],
                "bundles":[{"name":name,"bytes":(self.directory/name).stat().st_size,"sha256":validator.sha256_file(self.directory/name),
                            "assets":[{"name":page['asset_name'].lower(),"width":128,"height":128,"format":"ASTC_10x10","mipmap_count":8} for page in pages]}
                           for name,pages in self.grouped.items()]}

    def check(self, proof=None):
        path = self.directory / "verification.json"
        if proof is not None: path.write_text(json.dumps(proof))
        return validator.validate(self.manifest,self.directory,path if proof is not None else None)

    def test_completed_zero_byte_counterexample_now_fails(self):
        self.write_bundles(b"")
        result = self.check()
        self.assertFalse(result["preliminary_passed"])
        self.assertFalse(result["passed"])
        self.assertEqual(469, sum("empty, truncated" in failure for failure in result["failures"]))

    def test_declared_membership_alone_never_proves_binary_contents(self):
        result = self.check()
        self.assertTrue(result["preliminary_passed"])
        self.assertFalse(result["passed"])
        self.assertIn("missing byte-bound Unity Android bundle verification report", result["failures"])
        self.assertTrue(self.check(self.proof())["passed"], "Only a report-binding fixture, not a real production bundle claim.")

    def test_correct_global_set_wrong_partition_is_rejected(self):
        names=list(self.grouped)[:2]
        first,second=(self.directory/(name+".manifest") for name in names)
        a,b=first.read_bytes(),second.read_bytes();first.write_bytes(b);second.write_bytes(a)
        result=self.check(self.proof())
        self.assertFalse(result["passed"])
        self.assertEqual(2,sum("wrong page partition" in failure for failure in result["failures"]))

    def test_stale_identity_or_loaded_asset_contract_is_rejected(self):
        for mutation in [lambda proof:proof.update(build_target="StandaloneWindows64"),
                         lambda proof:proof.update(unity_version="old"),
                         lambda proof:proof.update(manifest_sha256="0"*64),
                         lambda proof:proof["compiler_inputs"][0].update(sha256="0"*64),
                         lambda proof:proof["bundles"][0].update(sha256="0"*64),
                         lambda proof:proof["bundles"].pop(),
                         lambda proof:proof["bundles"][0]["assets"].pop(),
                         lambda proof:proof["bundles"][0]["assets"][0].update(width=64),
                         lambda proof:proof["bundles"][0]["assets"][0].update(format="RGBA32"),
                         lambda proof:proof["bundles"][0]["assets"][0].update(mipmap_count=1),
                         lambda proof:proof["bundles"][0]["assets"][0].update(mipmap_count=2),
                         lambda proof:proof.update(bundles=[None])]:
            proof=self.proof();mutation(proof)
            self.assertFalse(self.check(proof)["passed"])


if __name__ == "__main__": unittest.main()
