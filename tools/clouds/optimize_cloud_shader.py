"""Apply the same conservative sampling to cloud color and XR motion programs."""
from pathlib import Path

def transform(source):
    if 'CloudVolumeSampling.cginc' in source:
        return source
    source=source.replace('        _DensitySlices (', '''        _DensityVolume ("Offline density volume", 3D) = "" {}
        _CloudOccupancy ("Conservative occupied cells", 3D) = "" {}
        _CloudVolumeSampling ("Use density volume", Float) = 0
        _CloudEmptySkipping ("Skip empty cells", Float) = 0
        _DensitySlices (''',1)
    source=source.replace('            sampler2D _DensitySlices;', '            #include "CloudVolumeSampling.cginc"\n            sampler2D _DensitySlices;')
    source=source.replace('                coordinate = saturate(coordinate);', '                if (_CloudVolumeSampling > .5) return SampleCloudVolume(coordinate);\n                coordinate = saturate(coordinate);')
    source=source.replace('                    half lightTransmittance;\n                    half density', '''                    int emptySamples = EmptyCloudSamples(sampleLocal, rayDirectionLocal, input.volumeParameters.x, stepLength);
                    if (emptySamples > 0)
                    {
                        emptySamples = min(emptySamples, MaximumMarchSteps-stepIndex);
                        // Repeated addition matches the original floating point lattice exactly.
                        for (int skipped=0; skipped<emptySamples; skipped++) sampleDistance += stepLength;
                        stepIndex += emptySamples-1;
                        continue;
                    }
                    half lightTransmittance;
                    half density''')
    assert source.count('int emptySamples =') == 2, 'Both cloud passes must be patched together'
    return source

if __name__ == '__main__':
    path=Path('QuestFlight/Assets/Production/Environment/CloudsV1/BoulderCloudShell.shader')
    path.write_text(transform(path.read_text(encoding='utf-8-sig')),encoding='utf-8')
