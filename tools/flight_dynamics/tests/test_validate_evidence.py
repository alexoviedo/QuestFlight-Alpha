import importlib.util
import unittest
from pathlib import Path


MODULE_PATH = Path(__file__).resolve().parents[1] / "validate_evidence.py"
SPEC = importlib.util.spec_from_file_location("flight_evidence_validator", MODULE_PATH)
VALIDATOR = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(VALIDATOR)


def evidence(status="Unsupported"):
    scenarios = []
    for scenario_id in sorted(VALIDATOR.REQUIRED_PROTOTYPE_SCENARIOS):
        scenarios.append(
            {
                "scenarioId": scenario_id,
                "backend": "UnityPrototype",
                "status": status,
                "metrics": [{"id": "test", "status": status}],
            }
        )
    return {
        "contractVersion": VALIDATOR.CONTRACT_VERSION,
        "fixedStepSeconds": 1.0 / 120.0,
        "productionConfig": {
            "status": "Fail",
            "effectiveParameterSha256": "a" * 64,
        },
        "scenarios": scenarios,
    }


class FlightEvidenceValidatorTests(unittest.TestCase):
    def test_structurally_valid_failure_evidence_is_not_promoted_to_pass(self):
        counts = VALIDATOR.validate(evidence("Unsupported"))
        self.assertEqual(0, counts["Pass"])
        self.assertEqual(
            len(VALIDATOR.REQUIRED_PROTOTYPE_SCENARIOS), counts["Unsupported"]
        )

    def test_require_pass_rejects_unsupported_evidence(self):
        with self.assertRaises(VALIDATOR.EvidenceError):
            VALIDATOR.validate(evidence("Unsupported"), require_pass=True)

    def test_missing_scenario_and_wrong_rate_are_rejected(self):
        data = evidence("Pass")
        data["fixedStepSeconds"] = 1.0 / 60.0
        with self.assertRaises(VALIDATOR.EvidenceError):
            VALIDATOR.validate(data)

        data = evidence("Pass")
        data["scenarios"].pop()
        with self.assertRaises(VALIDATOR.EvidenceError):
            VALIDATOR.validate(data)


if __name__ == "__main__":
    unittest.main()
