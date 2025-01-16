using Ryujinx.Common.Configuration.Hid;
using Ryujinx.Common.Configuration.Hid.Controller;
using Ryujinx.Common.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using static SDL3.SDL;

namespace Ryujinx.Input.SDL3
{
    class SDL3JoyCon : IGamepad
    {
        private bool HasConfiguration => _configuration != null;

        private readonly record struct ButtonMappingEntry(GamepadButtonInputId To, GamepadButtonInputId From)
        {
            public bool IsValid => To is not GamepadButtonInputId.Unbound && From is not GamepadButtonInputId.Unbound;
        }

        private StandardControllerInputConfig _configuration;

        private static readonly Dictionary<GamepadButtonInputId, SDL_GamepadButton> _leftButtonsDriverDict = new()
        {
            { GamepadButtonInputId.LeftStick, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_STICK },
            { GamepadButtonInputId.DpadUp, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_WEST },
            { GamepadButtonInputId.DpadDown, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_EAST },
            { GamepadButtonInputId.DpadLeft, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH },
            { GamepadButtonInputId.DpadRight, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_NORTH },
            { GamepadButtonInputId.Minus, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_START },
            { GamepadButtonInputId.LeftShoulder, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_PADDLE1 },
            { GamepadButtonInputId.LeftTrigger, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_PADDLE2 },
            { GamepadButtonInputId.SingleRightTrigger0, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_SHOULDER },
            { GamepadButtonInputId.SingleLeftTrigger0, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_SHOULDER },
        };

        private static readonly Dictionary<GamepadButtonInputId, SDL_GamepadButton> _rightButtonsDriverDict = new()
        {
            { GamepadButtonInputId.RightStick, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_STICK },
            { GamepadButtonInputId.A, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH },
            { GamepadButtonInputId.B, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_WEST },
            { GamepadButtonInputId.X, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_EAST },
            { GamepadButtonInputId.Y, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_NORTH },
            { GamepadButtonInputId.Plus, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_START },
            { GamepadButtonInputId.RightShoulder, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_PADDLE1 },
            { GamepadButtonInputId.RightTrigger, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_PADDLE2 },
            { GamepadButtonInputId.SingleRightTrigger1, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_SHOULDER },
            { GamepadButtonInputId.SingleLeftTrigger1, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_SHOULDER }
        };

        private readonly SDL_GamepadButton[] _buttonsDriverMapping;
        private readonly Lock _userMappingLock = new();

        private readonly List<ButtonMappingEntry> _buttonsUserMapping;

        private readonly StickInputId[] _stickUserMapping = new StickInputId[(int)StickInputId.Count]
        {
            StickInputId.Unbound, StickInputId.Left, StickInputId.Right,
        };

        public GamepadFeaturesFlag Features { get; }

        private nint _gamepadHandle;

        private readonly SDL_GamepadType _gamepadType;

        public SDL3JoyCon(uint joystickId, string driverId)
        {
            _gamepadHandle = SDL_OpenGamepad(joystickId);
            _buttonsUserMapping = new List<ButtonMappingEntry>(10);

            Name = SDL_GetGamepadName(_gamepadHandle);
            Id = driverId;
            Features = GetFeaturesFlag();

            // Enable motion tracking
            if (Features.HasFlag(GamepadFeaturesFlag.Motion))
            {
                if (!SDL_SetGamepadSensorEnabled(_gamepadHandle, SDL_SensorType.SDL_SENSOR_ACCEL, true))
                {
                    Logger.Error?.Print(LogClass.Hid,
                        $"Could not enable data reporting for SensorType {SDL_SensorType.SDL_SENSOR_ACCEL}.");
                }

                if (!SDL_SetGamepadSensorEnabled(_gamepadHandle, SDL_SensorType.SDL_SENSOR_GYRO, true))
                {
                    Logger.Error?.Print(LogClass.Hid,
                        $"Could not enable data reporting for SensorType {SDL_SensorType.SDL_SENSOR_GYRO}.");
                }
            }

            _gamepadType = SDL_GetGamepadType(_gamepadHandle);

            _buttonsDriverMapping = _gamepadType switch
            {
                SDL_GamepadType.SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_JOYCON_LEFT => ToSDLButtonMapping(
                    _leftButtonsDriverDict),
                SDL_GamepadType.SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_JOYCON_RIGHT => ToSDLButtonMapping(
                    _rightButtonsDriverDict),
                _ => throw new InvalidOperationException($"Unexpected JoyConType value: {_gamepadType}")
            };
        }

        private static SDL_GamepadButton[] ToSDLButtonMapping(
            Dictionary<GamepadButtonInputId, SDL_GamepadButton> buttonsDriverDict)
        {
            return Enumerable.Range(0, (int)GamepadButtonInputId.Count)
                .Select(i =>
                    buttonsDriverDict.GetValueOrDefault((GamepadButtonInputId)i,
                        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_INVALID))
                .ToArray();
        }

        private GamepadFeaturesFlag GetFeaturesFlag()
        {
            GamepadFeaturesFlag result = GamepadFeaturesFlag.None;
            if (SDL_GamepadHasSensor(_gamepadHandle, SDL_SensorType.SDL_SENSOR_ACCEL) &&
                SDL_GamepadHasSensor(_gamepadHandle, SDL_SensorType.SDL_SENSOR_GYRO))
            {
                result |= GamepadFeaturesFlag.Motion;
            }

            if (SDL_RumbleGamepad(_gamepadHandle, 0, 0, 100))
            {
                result |= GamepadFeaturesFlag.Rumble;
            }

            return result;
        }

        public string Id { get; }
        public string Name { get; }
        public bool IsConnected => SDL_GamepadConnected(_gamepadHandle);

        private void Dispose(bool disposing)
        {
            if (disposing && _gamepadHandle != nint.Zero)
            {
                SDL_CloseGamepad(_gamepadHandle);
                _gamepadHandle = nint.Zero;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }


        public void SetTriggerThreshold(float triggerThreshold)
        {
        }

        public void Rumble(float lowFrequency, float highFrequency, uint durationMs)
        {
            if (!Features.HasFlag(GamepadFeaturesFlag.Rumble))
                return;

            ushort lowFrequencyRaw = (ushort)(lowFrequency * ushort.MaxValue);
            ushort highFrequencyRaw = (ushort)(highFrequency * ushort.MaxValue);

            if (!SDL_RumbleGamepad(_gamepadHandle, lowFrequencyRaw, highFrequencyRaw, durationMs))
            {
                Logger.Error?.Print(LogClass.Hid, "Rumble is not supported on this game controller.");
            }
        }

        public Vector3 GetMotionData(MotionInputId inputId)
        {
            SDL_SensorType sensorType = inputId switch
            {
                MotionInputId.Accelerometer => SDL_SensorType.SDL_SENSOR_ACCEL,
                MotionInputId.Gyroscope => SDL_SensorType.SDL_SENSOR_GYRO,
                _ => SDL_SensorType.SDL_SENSOR_INVALID
            };

            if (!Features.HasFlag(GamepadFeaturesFlag.Motion) || sensorType is SDL_SensorType.SDL_SENSOR_INVALID)
                return Vector3.Zero;

            const int ElementCount = 3;

            unsafe
            {
                float* values = stackalloc float[ElementCount];

                if (!SDL_GetGamepadSensorData(_gamepadHandle, sensorType, values, ElementCount))
                {
                    return Vector3.Zero;
                }

                Vector3 value = _gamepadType switch
                {
                    SDL_GamepadType.SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_JOYCON_LEFT => new Vector3(-values[2], values[1], values[0]),
                    SDL_GamepadType.SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_JOYCON_RIGHT => new Vector3(values[2], values[1], -values[0]),
                    _ => throw new ArgumentOutOfRangeException($"Unexpected JoyConType value: {_gamepadType}")
                };

                return inputId switch
                {
                    MotionInputId.Gyroscope => RadToDegree(value),
                    MotionInputId.Accelerometer => GsToMs2(value),
                    _ => value
                };
            }
        }

        private static Vector3 RadToDegree(Vector3 rad) => rad * (180 / MathF.PI);
        private static Vector3 GsToMs2(Vector3 gs) => gs / SDL_STANDARD_GRAVITY;

        public void SetConfiguration(InputConfig configuration)
        {
            lock (_userMappingLock)
            {
                _configuration = (StandardControllerInputConfig)configuration;

                _buttonsUserMapping.Clear();

                // First update sticks
                _stickUserMapping[(int)StickInputId.Left] = (StickInputId)_configuration.LeftJoyconStick.Joystick;
                _stickUserMapping[(int)StickInputId.Right] = (StickInputId)_configuration.RightJoyconStick.Joystick;


                switch (_gamepadType)
                {
                    case SDL_GamepadType.SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_JOYCON_LEFT:
                        _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.LeftStick,
                            (GamepadButtonInputId)_configuration.LeftJoyconStick.StickButton));
                        _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.DpadUp,
                            (GamepadButtonInputId)_configuration.LeftJoycon.DpadUp));
                        _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.DpadDown,
                            (GamepadButtonInputId)_configuration.LeftJoycon.DpadDown));
                        _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.DpadLeft,
                            (GamepadButtonInputId)_configuration.LeftJoycon.DpadLeft));
                        _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.DpadRight,
                            (GamepadButtonInputId)_configuration.LeftJoycon.DpadRight));
                        _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.Minus,
                            (GamepadButtonInputId)_configuration.LeftJoycon.ButtonMinus));
                        _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.LeftShoulder,
                            (GamepadButtonInputId)_configuration.LeftJoycon.ButtonL));
                        _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.LeftTrigger,
                            (GamepadButtonInputId)_configuration.LeftJoycon.ButtonZl));
                        _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.SingleRightTrigger0,
                            (GamepadButtonInputId)_configuration.LeftJoycon.ButtonSr));
                        _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.SingleLeftTrigger0,
                            (GamepadButtonInputId)_configuration.LeftJoycon.ButtonSl));
                        break;
                    case SDL_GamepadType.SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_JOYCON_RIGHT:
                        _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.RightStick,
                            (GamepadButtonInputId)_configuration.RightJoyconStick.StickButton));
                        _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.A,
                            (GamepadButtonInputId)_configuration.RightJoycon.ButtonA));
                        _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.B,
                            (GamepadButtonInputId)_configuration.RightJoycon.ButtonB));
                        _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.X,
                            (GamepadButtonInputId)_configuration.RightJoycon.ButtonX));
                        _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.Y,
                            (GamepadButtonInputId)_configuration.RightJoycon.ButtonY));
                        _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.Plus,
                            (GamepadButtonInputId)_configuration.RightJoycon.ButtonPlus));
                        _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.RightShoulder,
                            (GamepadButtonInputId)_configuration.RightJoycon.ButtonR));
                        _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.RightTrigger,
                            (GamepadButtonInputId)_configuration.RightJoycon.ButtonZr));
                        _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.SingleRightTrigger1,
                            (GamepadButtonInputId)_configuration.RightJoycon.ButtonSr));
                        _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.SingleLeftTrigger1,
                            (GamepadButtonInputId)_configuration.RightJoycon.ButtonSl));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                SetTriggerThreshold(_configuration.TriggerThreshold);
            }
        }

        public GamepadStateSnapshot GetStateSnapshot()
        {
            return IGamepad.GetStateSnapshot(this);
        }

        public GamepadStateSnapshot GetMappedStateSnapshot()
        {
            GamepadStateSnapshot rawState = GetStateSnapshot();
            GamepadStateSnapshot result = default;

            lock (_userMappingLock)
            {
                if (_buttonsUserMapping.Count == 0)
                    return rawState;


                // ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
                foreach (ButtonMappingEntry entry in _buttonsUserMapping)
                {
                    if (!entry.IsValid)
                        continue;

                    // Do not touch state of button already pressed
                    if (!result.IsPressed(entry.To))
                    {
                        result.SetPressed(entry.To, rawState.IsPressed(entry.From));
                    }
                }

                (float leftStickX, float leftStickY) = rawState.GetStick(_stickUserMapping[(int)StickInputId.Left]);
                (float rightStickX, float rightStickY) = rawState.GetStick(_stickUserMapping[(int)StickInputId.Right]);

                result.SetStick(StickInputId.Left, leftStickX, leftStickY);
                result.SetStick(StickInputId.Right, rightStickX, rightStickY);
            }

            return result;
        }


        private static float ConvertRawStickValue(short value)
        {
            const float ConvertRate = 1.0f / (short.MaxValue + 0.5f);

            return value * ConvertRate;
        }

        private JoyconConfigControllerStick<GamepadInputId, Common.Configuration.Hid.Controller.StickInputId>
            GetLogicalJoyStickConfig(StickInputId inputId)
        {
            switch (inputId)
            {
                case StickInputId.Left:
                    if (_configuration.RightJoyconStick.Joystick ==
                        Common.Configuration.Hid.Controller.StickInputId.Left)
                        return _configuration.RightJoyconStick;
                    else
                        return _configuration.LeftJoyconStick;
                case StickInputId.Right:
                    if (_configuration.LeftJoyconStick.Joystick ==
                        Common.Configuration.Hid.Controller.StickInputId.Right)
                        return _configuration.LeftJoyconStick;
                    else
                        return _configuration.RightJoyconStick;
            }

            return null;
        }


        public (float, float) GetStick(StickInputId inputId)
        {
            if (inputId == StickInputId.Unbound)
                return (0.0f, 0.0f);

            if (inputId == StickInputId.Left && _gamepadType == SDL_GamepadType.SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_JOYCON_RIGHT ||
                inputId == StickInputId.Right && _gamepadType == SDL_GamepadType.SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_JOYCON_LEFT)
            {
                return (0.0f, 0.0f);
            }

            (short stickX, short stickY) = GetStickXY();

            float resultX = ConvertRawStickValue(stickX);
            float resultY = -ConvertRawStickValue(stickY);

            if (HasConfiguration)
            {
                var joyconStickConfig = GetLogicalJoyStickConfig(inputId);

                if (joyconStickConfig != null)
                {
                    if (joyconStickConfig.InvertStickX)
                        resultX = -resultX;

                    if (joyconStickConfig.InvertStickY)
                        resultY = -resultY;

                    if (joyconStickConfig.Rotate90CW)
                    {
                        float temp = resultX;
                        resultX = resultY;
                        resultY = -temp;
                    }
                }
            }

            return inputId switch
            {
                StickInputId.Left when _gamepadType == SDL_GamepadType.SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_JOYCON_LEFT => (resultY, -resultX),
                StickInputId.Right when _gamepadType == SDL_GamepadType.SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_JOYCON_RIGHT => (-resultY, resultX),
                _ => (0.0f, 0.0f)
            };
        }

        private (short, short) GetStickXY()
        {
            return (
                SDL_GetGamepadAxis(_gamepadHandle, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX),
                SDL_GetGamepadAxis(_gamepadHandle, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTY));
        }

        public bool IsPressed(GamepadButtonInputId inputId)
        {
            var button = _buttonsDriverMapping[(int)inputId];
            if (button == SDL_GamepadButton.SDL_GAMEPAD_BUTTON_INVALID)
            {
                return false;
            }

            if (SDL_GetGamepadButton(_gamepadHandle, button))
            {
                Console.WriteLine(_gamepadType+": " + button+" => "+inputId);
            }
            return SDL_GetGamepadButton(_gamepadHandle, button);
        }

        public static bool IsJoyCon(uint joystickId)
        {
            var gamepadName = SDL_GetGamepadTypeForID(joystickId);
            return gamepadName is SDL_GamepadType.SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_JOYCON_LEFT
                or SDL_GamepadType.SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_JOYCON_RIGHT;
        }
    }
}
