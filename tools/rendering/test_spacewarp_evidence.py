import unittest
from evaluate_spacewarp import evaluate
class EvidenceTests(unittest.TestCase):
    def valid(self):
        return dict(deviceModel='Quest 3',source='physical_headset',apkSha256='a'*64,durationSeconds=1200,displayRefreshHz=72,thermalThrottling=False,wornVisualPass=True,compositor=dict(applicationSpaceWarpActive=True,missedFrameRatio=.001,applicationFrameRate=36),frameSamples=[dict(frameTimingAvailable=True,cpuFrameMs=15,gpuFrameMs=23,spaceWarpRequested=True,spaceWarpEnabled=True,spaceWarpSubmissionObserved=True) for _ in range(1200)])
    def test_nonfinite_cannot_pass(self):
        d=self.valid();d['durationSeconds']=float('nan');self.assertFalse(evaluate(d)['passed'])
        d=self.valid();d['compositor']['missedFrameRatio']=float('nan');self.assertFalse(evaluate(d)['passed'])
    def test_missing_cadence(self):
        d=self.valid();del d['compositor']['applicationFrameRate'];self.assertFalse(evaluate(d)['passed'])
    def test_complete_evidence(self):self.assertTrue(evaluate(self.valid())['passed'])
    def test_no_data_cannot_pass(self):self.assertFalse(evaluate({})['passed'])
    def test_simulator_cannot_pass(self):
        d=self.valid();d['source']='simulator';self.assertFalse(evaluate(d)['passed'])
    def test_36fps_is_not_proof_of_spacewarp(self):
        d=self.valid();d['compositor']={};self.assertFalse(evaluate(d)['passed'])
    def test_overloaded_workload(self):
        d=self.valid();d['frameSamples'][0:200]=[dict(d['frameSamples'][0],gpuFrameMs=29)]*200;self.assertFalse(evaluate(d)['passed'])
    def test_missing_visual_acceptance(self):
        d=self.valid();d['wornVisualPass']=False;self.assertFalse(evaluate(d)['passed'])
    def test_missing_motion(self):
        d=self.valid();d['frameSamples'][0]['spaceWarpSubmissionObserved']=False;self.assertFalse(evaluate(d)['passed'])
if __name__=='__main__':unittest.main()
