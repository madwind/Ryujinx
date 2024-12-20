using Ryujinx.Common.Configuration.Hid;
using Ryujinx.Common.Configuration.Hid.Controller;
using Ryujinx.Common.Memory;
using System;
using System.Collections.Generic;
using System.Numerics;
using static SDL2.SDL;


namespace Ryujinx.Input.SDL2
{

    public class JoyConPair : IGamepad
    {
        private readonly IGamepad _leftJoyCon;
        private readonly IGamepad _rightJoyCon;
        private StandardControllerInputConfig _configuration;
        public JoyConPair(List<string> gamepadsIds)
        {
            _buttonsUserMapping = new List<ButtonMappingEntry>(20);

            for (int index = 0; index < gamepadsIds.Count; index++)
            {
                if (gamepadsIds[index] == JoyConPair.Id)
                {
                    continue;
                }
                string gamepadName = SDL_GameControllerNameForIndex(index);

                if (gamepadName == leftName)
                {
                    _leftJoyCon = new SDL2Gamepad(SDL_GameControllerOpen(index), gamepadsIds[0]);
                }
                else if (gamepadName == rightName)
                {
                    _rightJoyCon = new SDL2Gamepad(SDL_GameControllerOpen(index), gamepadsIds[0]);
                }
            }
        }

        public GamepadFeaturesFlag Features => _leftJoyCon.Features | _rightJoyCon.Features;

        public static string Id => "JoyConPair";
        public static string leftName => "Nintendo Switch Joy-Con (L)";
        public static string rightName => "Nintendo Switch Joy-Con (R)";
        string IGamepad.Id => Id;

        public string Name => "Joy-Con Pair";

        public bool IsConnected => _leftJoyCon.IsConnected && _rightJoyCon.IsConnected;

        private readonly record struct ButtonMappingEntry(GamepadButtonInputId To, GamepadButtonInputId From)
        {
            public bool IsValid => To is not GamepadButtonInputId.Unbound && From is not GamepadButtonInputId.Unbound;
        }

        private readonly List<ButtonMappingEntry> _buttonsUserMapping;

        private readonly object _userMappingLock = new();

        private readonly StickInputId[] _stickUserMapping = new StickInputId[(int)StickInputId.Count]
{
            StickInputId.Unbound,
            StickInputId.Left,
            StickInputId.Right,
};
        public void Dispose()
        {
            _leftJoyCon?.Dispose();
            _rightJoyCon?.Dispose();
        }

        public (float, float) GetStick(StickInputId inputId)
        {
            if (inputId == StickInputId.Left)
            {
                return _leftJoyCon.GetStick(StickInputId.Left);
            }
            else if (inputId == StickInputId.Right)
            {
                return _rightJoyCon.GetStick(StickInputId.Left);
            }

            return (0, 0);
        }

        public Vector3 GetMotionData(MotionInputId inputId)
        {
            return inputId switch
            {
                MotionInputId.RightAccelerometer => _rightJoyCon.GetMotionData(MotionInputId.Accelerometer),
                MotionInputId.RightGyroscope => _rightJoyCon.GetMotionData(MotionInputId.Gyroscope),
                _ => _leftJoyCon.GetMotionData(inputId)
            };
        }

        public void SetTriggerThreshold(float triggerThreshold)
        {
            _leftJoyCon.SetTriggerThreshold(triggerThreshold);
            _rightJoyCon.SetTriggerThreshold(triggerThreshold);
        }

        public void SetConfiguration(InputConfig configuration)
        {
            lock (_userMappingLock)
            {
                _configuration = (StandardControllerInputConfig)configuration;

                _leftJoyCon.SetConfiguration(configuration);
                _rightJoyCon.SetConfiguration(configuration);

                _buttonsUserMapping.Clear();

                // First update sticks
                _stickUserMapping[(int)StickInputId.Left] = (StickInputId)_configuration.LeftJoyconStick.Joystick;
                _stickUserMapping[(int)StickInputId.Right] = (StickInputId)_configuration.RightJoyconStick.Joystick;

                // Then left joycon
                _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.LeftStick, (GamepadButtonInputId)_configuration.LeftJoyconStick.StickButton));
                _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.DpadUp, (GamepadButtonInputId)_configuration.LeftJoycon.DpadUp));
                _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.DpadDown, (GamepadButtonInputId)_configuration.LeftJoycon.DpadDown));
                _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.DpadLeft, (GamepadButtonInputId)_configuration.LeftJoycon.DpadLeft));
                _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.DpadRight, (GamepadButtonInputId)_configuration.LeftJoycon.DpadRight));
                _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.Minus, (GamepadButtonInputId)_configuration.LeftJoycon.ButtonMinus));
                _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.LeftShoulder, (GamepadButtonInputId)_configuration.LeftJoycon.ButtonL));
                _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.LeftTrigger, (GamepadButtonInputId)_configuration.LeftJoycon.ButtonZl));
                _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.SingleRightTrigger0, (GamepadButtonInputId)_configuration.LeftJoycon.ButtonSr));
                _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.SingleLeftTrigger0, (GamepadButtonInputId)_configuration.LeftJoycon.ButtonSl));

                // Finally right joycon
                _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.RightStick, (GamepadButtonInputId)_configuration.RightJoyconStick.StickButton));
                _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.A, (GamepadButtonInputId)_configuration.RightJoycon.ButtonA));
                _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.B, (GamepadButtonInputId)_configuration.RightJoycon.ButtonB));
                _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.X, (GamepadButtonInputId)_configuration.RightJoycon.ButtonX));
                _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.Y, (GamepadButtonInputId)_configuration.RightJoycon.ButtonY));
                _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.Plus, (GamepadButtonInputId)_configuration.RightJoycon.ButtonPlus));
                _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.RightShoulder, (GamepadButtonInputId)_configuration.RightJoycon.ButtonR));
                _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.RightTrigger, (GamepadButtonInputId)_configuration.RightJoycon.ButtonZr));
                _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.SingleRightTrigger1, (GamepadButtonInputId)_configuration.RightJoycon.ButtonSr));
                _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.SingleLeftTrigger1, (GamepadButtonInputId)_configuration.RightJoycon.ButtonSl));

                SetTriggerThreshold(_configuration.TriggerThreshold);
            }

        }

        public void Rumble(float lowFrequency, float highFrequency, uint durationMs)
        {
            if (lowFrequency != 0)
            {
                _rightJoyCon.Rumble(lowFrequency, highFrequency, durationMs);
            }
            if (highFrequency != 0)
            {
                _leftJoyCon.Rumble(lowFrequency, highFrequency, durationMs);
            }
            if (lowFrequency == 0 && highFrequency == 0)
            {
                _leftJoyCon.Rumble(lowFrequency, highFrequency, durationMs);
                _rightJoyCon.Rumble(lowFrequency, highFrequency, durationMs);
            }
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

                result.SetStick(StickInputId.Left, leftStickY, -leftStickX);
                result.SetStick(StickInputId.Right, -rightStickY, rightStickX);
            }

            return result;
        }

        public GamepadStateSnapshot GetStateSnapshot()
        {
            return GetPairStateSnapshot();
        }

        private GamepadStateSnapshot GetPairStateSnapshot()
        {
            // NOTE: Update Array size if JoystickInputId is changed.
            Array3<Array2<float>> joysticksState = default;

            for (StickInputId inputId = StickInputId.Left; inputId < StickInputId.Count; inputId++)
            {
                Array2<float> state = default;
                (float state0, float state1) = GetStick(inputId);
                state[0] = state0;
                state[1] = state1;

                joysticksState[(int)inputId] = state;
            }

            // NOTE: Update Array size if GamepadInputId is changed.
            Array28<bool> buttonsState = default;

            for (GamepadButtonInputId inputId = GamepadButtonInputId.A; inputId < GamepadButtonInputId.Count; inputId++)
            {
                buttonsState[(int)inputId] = IsPressed(inputId);

            }

            // Then left joycon


            return new GamepadStateSnapshot(joysticksState, buttonsState);
        }

        public bool IsPressed(GamepadButtonInputId inputId)
        {
            return inputId switch
            {
                // 左 Joy-Con 按键映射
                GamepadButtonInputId.LeftStick => _leftJoyCon.IsPressed(GamepadButtonInputId.LeftStick),
                GamepadButtonInputId.DpadUp => _leftJoyCon.IsPressed(GamepadButtonInputId.Y),
                GamepadButtonInputId.DpadDown => _leftJoyCon.IsPressed(GamepadButtonInputId.A),
                GamepadButtonInputId.DpadLeft => _leftJoyCon.IsPressed(GamepadButtonInputId.B),
                GamepadButtonInputId.DpadRight => _leftJoyCon.IsPressed(GamepadButtonInputId.X),
                GamepadButtonInputId.Minus => _leftJoyCon.IsPressed(GamepadButtonInputId.Start),
                GamepadButtonInputId.LeftShoulder => _leftJoyCon.IsPressed(GamepadButtonInputId.Paddle2),
                GamepadButtonInputId.LeftTrigger => _leftJoyCon.IsPressed(GamepadButtonInputId.Paddle4),
                GamepadButtonInputId.SingleRightTrigger0 => _leftJoyCon.IsPressed(GamepadButtonInputId.LeftShoulder),
                GamepadButtonInputId.SingleLeftTrigger0 => _leftJoyCon.IsPressed(GamepadButtonInputId.RightShoulder),

                // 右 Joy-Con 按键映射
                GamepadButtonInputId.RightStick => _rightJoyCon.IsPressed(GamepadButtonInputId.LeftStick),
                GamepadButtonInputId.A => _rightJoyCon.IsPressed(GamepadButtonInputId.B),
                GamepadButtonInputId.B => _rightJoyCon.IsPressed(GamepadButtonInputId.Y),
                GamepadButtonInputId.X => _rightJoyCon.IsPressed(GamepadButtonInputId.A),
                GamepadButtonInputId.Y => _rightJoyCon.IsPressed(GamepadButtonInputId.X),
                GamepadButtonInputId.Plus => _rightJoyCon.IsPressed(GamepadButtonInputId.Start),
                GamepadButtonInputId.RightShoulder => _rightJoyCon.IsPressed(GamepadButtonInputId.Paddle1),
                GamepadButtonInputId.RightTrigger => _rightJoyCon.IsPressed(GamepadButtonInputId.Paddle3),
                GamepadButtonInputId.SingleRightTrigger1 => _rightJoyCon.IsPressed(GamepadButtonInputId.LeftShoulder),
                GamepadButtonInputId.SingleLeftTrigger1 => _rightJoyCon.IsPressed(GamepadButtonInputId.RightShoulder),

                // 默认情况返回 false
                _ => false
            };
        }
    }
}
