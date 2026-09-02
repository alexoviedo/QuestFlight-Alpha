import copy
import importlib.util
import json
import pathlib
import sys
import unittest


REPO = pathlib.Path(__file__).resolve().parents[3]
SCRIPT = REPO / "tools/full_map_buildings/apply_airport_gap_fill.py"
sys.path.insert(0, str(SCRIPT.parent))
SPEC = importlib.util.spec_from_file_location("apply_airport_gap_fill", SCRIPT)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)


class AirportGapFillTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.config = MODULE.load(REPO / "tools/full_map_buildings/config.json")
        contract = cls.config["airport_gap_fill"]
        cls.context = MODULE.load(REPO / contract["source_context_path"])
        cls.replacements = MODULE.load(REPO / contract["hero_replacements_path"])

    def test_selection_is_exact_and_preserves_hero_replacements(self):
        selected, exclusions, aerodrome_ids, right_departure_ids = MODULE.select_features(
            self.config, self.context, self.replacements)
        self.assertEqual(len(selected), 34)
        self.assertEqual(len(aerodrome_ids), 22)
        self.assertEqual(len(right_departure_ids), 12)
        self.assertEqual(exclusions, {
            "below_area_threshold": 1,
            "hero_replacement": 2,
            "residential_edge": 3,
        })
        selected_ids = {int(feature["source"]["osm_id"]) for feature, _ in selected}
        hero_ids = {int(item["source_osm_id"]) for item in self.replacements["replacements"]}
        self.assertTrue(selected_ids.isdisjoint(hero_ids))
        self.assertEqual(
            selected_ids - {int(value.split(":")[2]) for value in aerodrome_ids},
            set(self.config["airport_gap_fill"]["right_runway_08_departure_source_ids"]))

    def test_committed_database_contains_only_the_selected_airport_gap(self):
        database = MODULE.load(REPO / "QuestFlight/Assets/Production/Environment/FullMapBuildingsV1/KBDU_FullMapBuildingDatabaseV1.json")
        selected, _, _, _ = MODULE.select_features(self.config, self.context, self.replacements)
        expected = {MODULE.source_identity(feature) for feature, _ in selected}
        actual = {item["source_id"] for page in database["pages"] for item in page.get("buildings", [])
                  if item["source_id"].startswith("OSM:")}
        self.assertEqual(actual, expected)

    def test_reapplication_is_idempotent(self):
        database = MODULE.load(REPO / "QuestFlight/Assets/Production/Environment/FullMapBuildingsV1/KBDU_FullMapBuildingDatabaseV1.json")
        before = json.dumps(database, separators=(",", ":"), ensure_ascii=False)
        updated, report = MODULE.apply(
            self.config, copy.deepcopy(database), self.context, self.replacements)
        after = json.dumps(updated, separators=(",", ":"), ensure_ascii=False)
        self.assertEqual(after, before)
        self.assertEqual(report["restored_building_count"], 34)
        self.assertEqual(report["right_runway_08_departure_building_count"], 12)


if __name__ == "__main__":
    unittest.main()
