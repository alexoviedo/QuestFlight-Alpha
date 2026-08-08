using UnityEngine;

namespace QuestFlightLab.Training.FlightLessonV1
{
    public struct FlightLessonControlCommand
    {
        public float aileron;
        public float elevator;
        public float rudder;
        public float throttle;
        public float mixture;
        public float trim;
        public float flaps;
        public float leftToeBrake;
        public float rightToeBrake;
    }

    public struct FlightLessonPlayerInput
    {
        public FlightLessonControlCommand command;
        public bool primaryAxesAvailable;
        public bool throttleOwned;
        public bool mixtureOwned;
        public bool trimOwned;
        public bool flapsOwned;
        public bool brakesOwned;
    }

    public struct FlightLessonAssistanceMixSettings
    {
        public float primaryAxisDeadzone;
        public float primaryAxisFullAuthority;

        public static FlightLessonAssistanceMixSettings Default => new FlightLessonAssistanceMixSettings
        {
            primaryAxisDeadzone = 0.08f,
            primaryAxisFullAuthority = 0.65f
        };
    }

    /// <summary>
    /// Pure shared-control mixer. It never discovers devices, owns controls, or writes
    /// aircraft state. Explicit ownership for cockpit levers prevents accidental fights.
    /// </summary>
    public static class FlightLessonAssistanceMixer
    {
        public static FlightLessonControlCommand Mix(
            in FlightLessonControlCommand guidance,
            in FlightLessonPlayerInput player,
            in FlightLessonAssistanceMixSettings settings)
        {
            FlightLessonControlCommand result = guidance;
            if (player.primaryAxesAvailable)
            {
                result.aileron = MixAxis(guidance.aileron, player.command.aileron, settings);
                result.elevator = MixAxis(guidance.elevator, player.command.elevator, settings);
                result.rudder = MixAxis(guidance.rudder, player.command.rudder, settings);
            }

            if (player.throttleOwned) result.throttle = Mathf.Clamp01(player.command.throttle);
            if (player.mixtureOwned) result.mixture = Mathf.Clamp01(player.command.mixture);
            if (player.trimOwned) result.trim = Mathf.Clamp(player.command.trim, -1f, 1f);
            if (player.flapsOwned) result.flaps = Mathf.Clamp01(player.command.flaps);
            if (player.brakesOwned)
            {
                result.leftToeBrake = Mathf.Clamp01(player.command.leftToeBrake);
                result.rightToeBrake = Mathf.Clamp01(player.command.rightToeBrake);
            }

            return Clamp(result);
        }

        public static float PlayerAuthority(float manualAxis, in FlightLessonAssistanceMixSettings settings)
        {
            float low = Mathf.Max(0f, settings.primaryAxisDeadzone);
            float high = Mathf.Max(low + 0.001f, settings.primaryAxisFullAuthority);
            float normalized = Mathf.InverseLerp(low, high, Mathf.Abs(manualAxis));
            return normalized * normalized * (3f - 2f * normalized);
        }

        private static float MixAxis(float guidance, float manual, in FlightLessonAssistanceMixSettings settings)
        {
            return Mathf.Lerp(guidance, manual, PlayerAuthority(manual, settings));
        }

        private static FlightLessonControlCommand Clamp(FlightLessonControlCommand command)
        {
            command.aileron = Mathf.Clamp(command.aileron, -1f, 1f);
            command.elevator = Mathf.Clamp(command.elevator, -1f, 1f);
            command.rudder = Mathf.Clamp(command.rudder, -1f, 1f);
            command.throttle = Mathf.Clamp01(command.throttle);
            command.mixture = Mathf.Clamp01(command.mixture);
            command.trim = Mathf.Clamp(command.trim, -1f, 1f);
            command.flaps = Mathf.Clamp01(command.flaps);
            command.leftToeBrake = Mathf.Clamp01(command.leftToeBrake);
            command.rightToeBrake = Mathf.Clamp01(command.rightToeBrake);
            return command;
        }
    }
}
