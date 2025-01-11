using Ryujinx.Common.Configuration.Hid;
using Ryujinx.Common.Configuration.Hid.Controller;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using static SDL3.SDL;

namespace Ryujinx.Input.SDL3
{
    internal class SDL3JoyConPair(IGamepad left, IGamepad right) : IGamepad
    {
        private StandardControllerInputConfig _configuration;

        private readonly StickInputId[] _stickUserMapping =
        [
            StickInputId.Unbound,
            StickInputId.Left,
            StickInputId.Right
        ];

        public GamepadFeaturesFlag Features => (left?.Features ?? GamepadFeaturesFlag.None) |
                                               (right?.Features ?? GamepadFeaturesFlag.None);

        public const string Id = "JoyConPair";
        string IGamepad.Id => Id;

        public string Name => "Nintendo Switch Joy-Con (L/R)";
        public bool IsConnected => left is { IsConnected: true } && right is { IsConnected: true };

        public void Dispose()
        {
            left?.Dispose();
            right?.Dispose();
        }

        public GamepadStateSnapshot GetMappedStateSnapshot()
        {
            return GetStateSnapshot();
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
            left.SetConfiguration(configuration);
            right.SetConfiguration(configuration);
        }

        public void SetTriggerThreshold(float triggerThreshold)
        {
            left.SetTriggerThreshold(triggerThreshold);
            right.SetTriggerThreshold(triggerThreshold);
        }

        public static bool IsCombinable(Dictionary<uint, GamepadInfo> gamepadsInstanceIdsMapping)
        {
            (GamepadInfo leftGamepadInfo, GamepadInfo rightGamepadInfo) = DetectJoyConPair(gamepadsInstanceIdsMapping);
            return leftGamepadInfo != null && rightGamepadInfo != null;
        }

        private static (GamepadInfo leftGamepadInfo, GamepadInfo rightGamepadInfo) DetectJoyConPair(
            Dictionary<uint, GamepadInfo> gamepadsInstanceIdsMapping)
        {
            var leftGamepadInfo = gamepadsInstanceIdsMapping
                .FirstOrDefault(item => SDL_GetGamepadNameForID(item.Key) == SDL3JoyCon.LeftName).Value;
            var rightGamepadInfo = gamepadsInstanceIdsMapping
                .FirstOrDefault(item => SDL_GetGamepadNameForID(item.Key) == SDL3JoyCon.RightName).Value;

            return (leftGamepadInfo, rightGamepadInfo);
        }

        public static IGamepad GetGamepad(Dictionary<uint, GamepadInfo> gamepadsInstanceIdsMapping)
        {
            (GamepadInfo leftGamepadInfo, GamepadInfo rightGamepadInfo) = DetectJoyConPair(gamepadsInstanceIdsMapping);
            if (leftGamepadInfo == null || rightGamepadInfo == null)
            {
                return null;
            }

            return new SDL3JoyConPair(new SDL3JoyCon(leftGamepadInfo), new SDL3JoyCon(rightGamepadInfo));
        }
    }
}
