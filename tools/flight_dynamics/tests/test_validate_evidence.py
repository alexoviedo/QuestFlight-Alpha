import copy
import hashlib
import json
import importlib.util
import unittest
from pathlib import Path


MODULE_PATH = Path(__file__).resolve().parents[1] / "validate_evidence.py"
SPEC = importlib.util.spec_from_file_location("flight_evidence_validator", MODULE_PATH)
VALIDATOR = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(VALIDATOR)


def scenario_input():
    raw = json.dumps({"initial":{"cas":75}, "mass":{"pilot":170}, "windNedFps":{}, "initialControls":{},
                      "stepSeconds":1/120,"durationSeconds":1,"resolvedWeightPounds":2054,"resolvedCgXInches":41})
    return {"scenario":"test", "definitionJson":raw,"definitionSha256":hashlib.sha256(raw.encode()).hexdigest()}


def evidence(status="Unsupported"):
    scenarios = []
    for scenario_id in sorted(VALIDATOR.REQUIRED_NATIVE_SCENARIOS):
        scenarios.append(
            {
                "scenarioId": scenario_id,
                "backend": "JSBSimNativeDevelopment",
                "status": status,
                "metrics": [{"id": "test", "status": status}],
            }
        )
    return {
        "contractVersion": VALIDATOR.CONTRACT_VERSION,
        "fixedStepSeconds": 1.0 / 120.0,
        "productionConfig": {
            "status": status,
            "effectiveParameterSha256": "a" * 64,
        },
        "nativeInputs": {"status":"Pass", "sourceCommit":"a"*40, "sourceInputsSha256":"a"*64,
                         "unityVersion":"6000.3.8f1", "platform":"WindowsEditor", "apiVersion":2,
                         "bridgeSha256":"b"*64,"jsbsimSha256":"c"*64,"payloadSha256":"d"*64,"aircraftXmlSha256":"e"*64},
        "scenarioInputs": [scenario_input()],
        "scenarios": scenarios,
    }


class FlightEvidenceValidatorTests(unittest.TestCase):
    def test_native_and_scenario_identity_mutations_are_rejected(self):
        baseline=evidence("Pass")
        VALIDATOR.compare_inputs(baseline,copy.deepcopy(baseline))
        for key in ("sourceInputsSha256","bridgeSha256","payloadSha256","aircraftXmlSha256"):
            current=copy.deepcopy(baseline);current["nativeInputs"][key]="f"*64
            with self.subTest(key=key), self.assertRaisesRegex(VALIDATOR.EvidenceError,key):
                VALIDATOR.compare_inputs(baseline,current)
        current=copy.deepcopy(baseline)
        raw=current["scenarioInputs"][0]["definitionJson"].replace('75','76')
        current["scenarioInputs"][0]["definitionJson"]=raw
        with self.assertRaises(VALIDATOR.EvidenceError):VALIDATOR.validate(current)
        current["scenarioInputs"][0]["definitionSha256"]=hashlib.sha256(raw.encode()).hexdigest()
        with self.assertRaisesRegex(VALIDATOR.EvidenceError,"scenario"):
            VALIDATOR.compare_inputs(baseline,current)

    def test_missing_native_identity_cannot_pass(self):
        current=evidence("Pass");current["nativeInputs"]["status"]="Unproven"
        VALIDATOR.validate(current)
        with self.assertRaises(VALIDATOR.EvidenceError):VALIDATOR.validate(current,require_pass=True)
        current=evidence("Pass");current["scenarioInputs"]=[]
        with self.assertRaises(VALIDATOR.EvidenceError):VALIDATOR.validate(current)

    def test_structurally_valid_failure_evidence_is_not_promoted_to_pass(self):
        counts = VALIDATOR.validate(evidence("Unsupported"))
        self.assertEqual(0, counts["Pass"])
        self.assertEqual(
            len(VALIDATOR.REQUIRED_NATIVE_SCENARIOS), counts["Unsupported"]
        )

    def test_require_pass_rejects_unsupported_evidence(self):
        with self.assertRaises(VALIDATOR.EvidenceError):
            VALIDATOR.validate(evidence("Unsupported"), require_pass=True)

    def test_complete_native_pass_and_failure_mutations(self):
        self.assertEqual(len(VALIDATOR.REQUIRED_NATIVE_SCENARIOS), VALIDATOR.validate(evidence("Pass"), require_pass=True)["Pass"])
        for status in ("Fail", "Unsupported", "Unproven", "Error"):
            with self.subTest(status=status):
                data = evidence("Pass")
                data["productionConfig"]["status"] = status
                with self.assertRaises(VALIDATOR.EvidenceError):
                    VALIDATOR.validate(data, require_pass=True)
                data = evidence("Pass")
                data["scenarios"][0]["metrics"][0]["status"] = status
                with self.assertRaises(VALIDATOR.EvidenceError):
                    VALIDATOR.validate(data, require_pass=True)

    def test_unavailable_native_is_structural_only(self):
        data = evidence("Unsupported")
        data["scenarios"] = [{"backend": "JSBSimNativeDevelopment", "scenarioId": "native_backend_availability", "status": "Unsupported", "metrics": [{"id":"availability","status":"Unsupported"}]}]
        self.assertEqual(1, VALIDATOR.validate(data)["Unsupported"])
        with self.assertRaises(VALIDATOR.EvidenceError):
            VALIDATOR.validate(data, require_pass=True)

    def test_bad_identity_duplicate_and_prototype_rejected(self):
        for mutation in ("hash", "duplicate", "prototype"):
            data = evidence("Pass")
            if mutation == "hash": data["productionConfig"]["effectiveParameterSha256"] = "z" * 64
            elif mutation == "duplicate": data["scenarios"].append(data["scenarios"][0])
            else: data["scenarios"][0]["backend"] = "UnityPrototype"
            with self.assertRaises(VALIDATOR.EvidenceError): VALIDATOR.validate(data)

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
