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
        }
    }
}
