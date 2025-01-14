using Ryujinx.Common.Configuration.Hid;
using Ryujinx.Common.Configuration.Hid.Controller;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using static SDL3.SDL;

namespace Ryujinx.Input.SDL3
{
    class SDL3JoyConPair(SDL3JoyCon left, SDL3JoyCon right) : IGamepad
    {
        private StandardControllerInputConfig _configuration;
        private readonly record struct ButtonMappingEntry(GamepadButtonInputId To, GamepadButtonInputId From)
        {
            public bool IsValid => To is not GamepadButtonInputId.Unbound && From is not GamepadButtonInputId.Unbound;
        }
        private readonly StickInputId[] _stickUserMapping = new StickInputId[(int)StickInputId.Count]
        {
            StickInputId.Unbound, StickInputId.Left, StickInputId.Right,
        };
        public GamepadFeaturesFlag Features => (left?.Features ?? GamepadFeaturesFlag.None) |
                                               (right?.Features ?? GamepadFeaturesFlag.None);

        public const string Id = "JoyConPair";
        private readonly Lock _userMappingLock = new();

        private readonly List<ButtonMappingEntry> _buttonsUserMapping              = new List<ButtonMappingEntry>(20);
        string IGamepad.Id => Id;

        public string Name => "* Nintendo Switch Joy-Con (L/R)";
        public bool IsConnected => left is { IsConnected: true } && right is { IsConnected: true };

        public void Dispose()
        {
            left?.Dispose();
            right?.Dispose();
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

        public Vector3 GetMotionData(MotionInputId inputId)
        {
            return inputId switch
            {
                MotionInputId.Accelerometer or
                    MotionInputId.Gyroscope => left.GetMotionData(inputId),
                MotionInputId.SecondAccelerometer => right.GetMotionData(MotionInputId.Accelerometer),
                MotionInputId.SecondGyroscope => right.GetMotionData(MotionInputId.Gyroscope),
                _ => Vector3.Zero
            };
        }

        public GamepadStateSnapshot GetStateSnapshot()
        {
            return IGamepad.GetStateSnapshot(this);
        }

        public (float, float) GetStick(StickInputId inputId)
        {
            return inputId switch
            {
                StickInputId.Left => left.GetStick(StickInputId.Left),
                StickInputId.Right => right.GetStick(StickInputId.Right),
                _ => (0, 0)
            };
        }

        public bool IsPressed(GamepadButtonInputId inputId)
        {
            return left.IsPressed(inputId) || right.IsPressed(inputId);
        }

        public void Rumble(float lowFrequency, float highFrequency, uint durationMs)
        {
            if (lowFrequency != 0)
            {
                right.Rumble(lowFrequency, lowFrequency, durationMs);
            }

            if (highFrequency != 0)
            {
                left.Rumble(highFrequency, highFrequency, durationMs);
            }

            if (lowFrequency == 0 && highFrequency == 0)
            {
                left.Rumble(0, 0, durationMs);
                right.Rumble(0, 0, durationMs);
            }
        }

        public void SetConfiguration(InputConfig configuration)
        {
            lock (_userMappingLock)
            {
                _configuration = (StandardControllerInputConfig)configuration;

                _buttonsUserMapping.Clear();

                // First update sticks
                _stickUserMapping[(int)StickInputId.Left] = (StickInputId)_configuration.LeftJoyconStick.Joystick;
                _stickUserMapping[(int)StickInputId.Right] = (StickInputId)_configuration.RightJoyconStick.Joystick;

                // Then left joycon
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

                // Finally right joycon
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
                left.SetConfiguration(configuration);
                right.SetConfiguration(configuration);
            }
        }

        public void SetTriggerThreshold(float triggerThreshold)
        {
        }

        public static bool IsCombinable(Dictionary<SDL_JoystickID, string> gamepadsInstanceIdsMapping)
        {
            var gamepadNames = gamepadsInstanceIdsMapping.Keys.Select(id => SDL_GetGamepadNameForID(id)).ToArray();
            return gamepadNames.Contains(SDL3JoyCon.LeftName) && gamepadNames.Contains(SDL3JoyCon.RightName);
        }

        public static IGamepad GetGamepad(Dictionary<SDL_JoystickID, string> gamepadsInstanceIdsMapping)
        {
            var leftPair =
                gamepadsInstanceIdsMapping.FirstOrDefault(pair =>
                    SDL_GetGamepadNameForID(pair.Key) == SDL3JoyCon.LeftName);
            var rightPair =
                gamepadsInstanceIdsMapping.FirstOrDefault(pair =>
                    SDL_GetGamepadNameForID(pair.Key) == SDL3JoyCon.RightName);
            if (leftPair.Key == 0 || rightPair.Key == 0)
            {
                return null;
            }

            return new SDL3JoyConPair(new SDL3JoyCon(leftPair.Key, leftPair.Value),
                new SDL3JoyCon(rightPair.Key, rightPair.Value));
        }
    }
}
