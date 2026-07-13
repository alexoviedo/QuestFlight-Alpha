using System;
using UnityEngine;

namespace QuestFlightLab.Runtime
{
    [Serializable]
    public class GamepadInputSnapshot
    {
        public bool connected;
        public string deviceName = "none";
        public string displayName = "none";
        public string layout = "none";
        public string manufacturer = "";
        public string product = "";
        public string interfaceName = "";
        public int deviceId = -1;
        public float timestamp;
        public float sampleRateHz;
        public float secondsSinceLastInput;

        public float leftStickX;
        public float leftStickY;
        public float rightStickX;
        public float rightStickY;
        public float leftTrigger;
        public float rightTrigger;
        public float dpadX;
        public float dpadY;

        public bool buttonSouth;
        public bool buttonEast;
        public bool buttonWest;
        public bool buttonNorth;
        public bool leftShoulder;
        public bool rightShoulder;
        public bool startButton;
        public bool selectButton;
        public bool leftStickButton;
        public bool rightStickButton;

        public static GamepadInputSnapshot Disconnected(float now, float sampleRate)
        {
            return new GamepadInputSnapshot
            {
                connected = false,
                timestamp = now,
                sampleRateHz = sampleRate,
                secondsSinceLastInput = 0f
            };
        }

        public bool HasDifferentInput(GamepadInputSnapshot other, float axisEpsilon = 0.01f)
        {
            if (other == null) return true;
            if (connected != other.connected || deviceId != other.deviceId) return true;
            if (Mathf.Abs(leftStickX - other.leftStickX) > axisEpsilon) return true;
            if (Mathf.Abs(leftStickY - other.leftStickY) > axisEpsilon) return true;
            if (Mathf.Abs(rightStickX - other.rightStickX) > axisEpsilon) return true;
            if (Mathf.Abs(rightStickY - other.rightStickY) > axisEpsilon) return true;
            if (Mathf.Abs(leftTrigger - other.leftTrigger) > axisEpsilon) return true;
            if (Mathf.Abs(rightTrigger - other.rightTrigger) > axisEpsilon) return true;
            if (Mathf.Abs(dpadX - other.dpadX) > axisEpsilon) return true;
            if (Mathf.Abs(dpadY - other.dpadY) > axisEpsilon) return true;

            return buttonSouth != other.buttonSouth
                   || buttonEast != other.buttonEast
                   || buttonWest != other.buttonWest
                   || buttonNorth != other.buttonNorth
                   || leftShoulder != other.leftShoulder
                   || rightShoulder != other.rightShoulder
                   || startButton != other.startButton
                   || selectButton != other.selectButton
                   || leftStickButton != other.leftStickButton
                   || rightStickButton != other.rightStickButton;
        }
    }
}
