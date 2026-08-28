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

        // Optional, position-holding flight controls. Standard gamepads leave
        // these false and retain the established rate/button behavior.
        public bool externalFlightController;
        public bool hasThrottlePosition;
        public float throttlePosition;
        public bool throttlePositionChanged;
        public bool hasMixturePosition;
        public float mixturePosition;
        public bool mixturePositionChanged;
        public bool hasFlapsPosition;
        public float flapsPosition;
        public bool flapsPositionChanged;

        public static GamepadInputSnapshot Disconnected(float now, float sampleRate)
        {
            GamepadInputSnapshot snapshot = new GamepadInputSnapshot();
            snapshot.SetDisconnected(now, sampleRate);
            return snapshot;
        }

        public void SetDisconnected(float now, float sampleRate)
        {
            connected = false;
            deviceName = "none";
            displayName = "none";
            layout = "none";
            manufacturer = string.Empty;
            product = string.Empty;
            interfaceName = string.Empty;
            deviceId = -1;
            timestamp = now;
            sampleRateHz = sampleRate;
            secondsSinceLastInput = 0f;
            leftStickX = 0f;
            leftStickY = 0f;
            rightStickX = 0f;
            rightStickY = 0f;
            leftTrigger = 0f;
            rightTrigger = 0f;
            dpadX = 0f;
            dpadY = 0f;
            buttonSouth = false;
            buttonEast = false;
            buttonWest = false;
            buttonNorth = false;
            leftShoulder = false;
            rightShoulder = false;
            startButton = false;
            selectButton = false;
            leftStickButton = false;
            rightStickButton = false;
            externalFlightController = false;
            hasThrottlePosition = false;
            throttlePosition = 0f;
            throttlePositionChanged = false;
            hasMixturePosition = false;
            mixturePosition = 0f;
            mixturePositionChanged = false;
            hasFlapsPosition = false;
            flapsPosition = 0f;
            flapsPositionChanged = false;
        }

        public void CopyInputFrom(GamepadInputSnapshot source)
        {
            if (source == null)
            {
                SetDisconnected(0f, 0f);
                return;
            }

            connected = source.connected;
            deviceName = source.deviceName;
            displayName = source.displayName;
            layout = source.layout;
            manufacturer = source.manufacturer;
            product = source.product;
            interfaceName = source.interfaceName;
            deviceId = source.deviceId;
            timestamp = source.timestamp;
            sampleRateHz = source.sampleRateHz;
            secondsSinceLastInput = source.secondsSinceLastInput;
            leftStickX = source.leftStickX;
            leftStickY = source.leftStickY;
            rightStickX = source.rightStickX;
            rightStickY = source.rightStickY;
            leftTrigger = source.leftTrigger;
            rightTrigger = source.rightTrigger;
            dpadX = source.dpadX;
            dpadY = source.dpadY;
            buttonSouth = source.buttonSouth;
            buttonEast = source.buttonEast;
            buttonWest = source.buttonWest;
            buttonNorth = source.buttonNorth;
            leftShoulder = source.leftShoulder;
            rightShoulder = source.rightShoulder;
            startButton = source.startButton;
            selectButton = source.selectButton;
            leftStickButton = source.leftStickButton;
            rightStickButton = source.rightStickButton;
            externalFlightController = source.externalFlightController;
            hasThrottlePosition = source.hasThrottlePosition;
            throttlePosition = source.throttlePosition;
            throttlePositionChanged = source.throttlePositionChanged;
            hasMixturePosition = source.hasMixturePosition;
            mixturePosition = source.mixturePosition;
            mixturePositionChanged = source.mixturePositionChanged;
            hasFlapsPosition = source.hasFlapsPosition;
            flapsPosition = source.flapsPosition;
            flapsPositionChanged = source.flapsPositionChanged;
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
            if (externalFlightController != other.externalFlightController) return true;
            if (hasThrottlePosition != other.hasThrottlePosition) return true;
            if (Mathf.Abs(throttlePosition - other.throttlePosition) > axisEpsilon) return true;
            if (hasMixturePosition != other.hasMixturePosition) return true;
            if (Mathf.Abs(mixturePosition - other.mixturePosition) > axisEpsilon) return true;
            if (hasFlapsPosition != other.hasFlapsPosition) return true;
            if (Mathf.Abs(flapsPosition - other.flapsPosition) > axisEpsilon) return true;

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
