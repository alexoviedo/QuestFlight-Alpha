using System;

namespace QuestFlightLab.Flight.Backends
{
    public sealed class FixedStepAccumulator
    {
        private readonly double _stepSeconds;
        private readonly int _maxStepsPerTick;
        private double _accumulatedSeconds;

        public FixedStepAccumulator(double stepSeconds, int maxStepsPerTick)
        {
            if (stepSeconds <= 0.0) throw new ArgumentOutOfRangeException(nameof(stepSeconds));
            if (maxStepsPerTick <= 0) throw new ArgumentOutOfRangeException(nameof(maxStepsPerTick));
            _stepSeconds = stepSeconds;
            _maxStepsPerTick = maxStepsPerTick;
        }

        public double StepSeconds => _stepSeconds;
        public double InterpolationAlpha => Math.Max(0.0, Math.Min(1.0, _accumulatedSeconds / _stepSeconds));
        public double DroppedSeconds { get; private set; }
        public double BacklogSeconds => _accumulatedSeconds;
        public bool CapacityExceeded { get; private set; }

        // Soaring uses a checked callback. Failure stops this consume immediately;
        // unintegrated time remains visible and overload blocks the caller. The
        // established C172 Consume path is unchanged for regression continuity.
        public bool ConsumeChecked(double elapsedSeconds,Func<double,bool> step,out int completed)
        {
            if(step==null)throw new ArgumentNullException(nameof(step));
            completed=0;CapacityExceeded=false;
            if(double.IsNaN(elapsedSeconds)||double.IsInfinity(elapsedSeconds)||elapsedSeconds<0)return false;
            _accumulatedSeconds+=elapsedSeconds;
            while(_accumulatedSeconds+1e-12>=_stepSeconds && completed<_maxStepsPerTick)
            {
                if(!step(_stepSeconds))return false;
                _accumulatedSeconds-=_stepSeconds;completed++;
            }
            CapacityExceeded=_accumulatedSeconds+1e-12>=_stepSeconds;
            return !CapacityExceeded;
        }

        public int Consume(double elapsedSeconds, Action<double> step)
        {
            if (step == null) throw new ArgumentNullException(nameof(step));
            if (elapsedSeconds <= 0.0) return 0;
            _accumulatedSeconds += elapsedSeconds;
            int count = 0;
            while (_accumulatedSeconds + 1e-12 >= _stepSeconds && count < _maxStepsPerTick)
            {
                step(_stepSeconds);
                _accumulatedSeconds -= _stepSeconds;
                count++;
            }

            if (_accumulatedSeconds >= _stepSeconds)
            {
                double retained = _accumulatedSeconds % _stepSeconds;
                DroppedSeconds += _accumulatedSeconds - retained;
                _accumulatedSeconds = retained;
            }

            return count;
        }

        public void Reset()
        {
            _accumulatedSeconds = 0.0;
            DroppedSeconds = 0.0;
            CapacityExceeded=false;
        }
    }
}
