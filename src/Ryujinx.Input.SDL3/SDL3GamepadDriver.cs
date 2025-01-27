using Ryujinx.Common.Logging;
using Ryujinx.Input.SDL3;
using Ryujinx.SDL3.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using static SDL3.SDL;

namespace Ryujinx.Input.SDL3
{
    public class SDL3GamepadDriver : IGamepadDriver
    {
        private readonly Dictionary<uint, string> _gamepadsInstanceIdsMapping;
        private readonly List<string> _gamepadsIds;
        private readonly Lock _lock = new();

        public ReadOnlySpan<string> GamepadsIds
        {
            get
            {
                lock (_lock)
                {
                    return _gamepadsIds.ToArray();
                }
            }
        }

        public string DriverName => "SDL3";

        public event Action<string> OnGamepadConnected;
        public event Action<string> OnGamepadDisconnected;

        public SDL3GamepadDriver()
        {
            _gamepadsInstanceIdsMapping = new Dictionary<uint, string>();
            _gamepadsIds = new List<string>();

            SDL3Driver.Instance.Initialize();
            SDL3Driver.Instance.OnJoyStickConnected += HandleJoyStickConnected;
            SDL3Driver.Instance.OnJoystickDisconnected += HandleJoyStickDisconnected;
            SDL3Driver.Instance.OnJoyBatteryUpdated += HandleJoyBatteryUpdated;
        }

        private string GenerateGamepadId(uint joystickId)
        {
            int bufferSize = 33;
            Span<byte> pszGuid = stackalloc byte[bufferSize];
            SDL_GUIDToString(SDL_GetJoystickGUIDForID(joystickId), pszGuid, bufferSize);
            var guid = Encoding.UTF8.GetString(pszGuid);

            string id;
            lock (_lock)
            {
                int guidIndex = 0;
                id = guidIndex + guid;

                while (_gamepadsIds.Contains(id))
                {
                    id = (++guidIndex) + "-" + guid;
                }
            }

            return id;
        }

        private KeyValuePair<uint,string> GetGamepadInfoByGamepadId(string id)
        {
            lock (_lock)
            {
                return _gamepadsInstanceIdsMapping.FirstOrDefault(gamepadId  => gamepadId.Value == id);
            }
        }

        private void HandleJoyStickDisconnected(uint joystickId)
        {
            bool joyConPairDisconnected = false;
            if (!_gamepadsInstanceIdsMapping.Remove(joystickId, out string id))
                return;

            lock (_lock)
            {
                _gamepadsIds.Remove(id);
                if (!SDL3JoyConPair.IsCombinable(_gamepadsInstanceIdsMapping))
                {
                    _gamepadsIds.Remove(SDL3JoyConPair.Id);
                    joyConPairDisconnected = true;
                }
            }

            OnGamepadDisconnected?.Invoke(id);
            if (joyConPairDisconnected)
            {
                OnGamepadDisconnected?.Invoke(SDL3JoyConPair.Id);
            }
        }

        private void HandleJoyStickConnected(uint joystickId)
        {
            bool joyConPairConnected = false;

            if (_gamepadsInstanceIdsMapping.ContainsKey(joystickId))
            {
                // Sometimes a JoyStick connected event fires after the app starts even though it was connected before
                // so it is rejected to avoid doubling the entries.
                return;
            }

            string id = GenerateGamepadId(joystickId);
            if (id == null)
            {
                return;
            }

            if (_gamepadsInstanceIdsMapping.TryAdd(joystickId, id))
            {
                lock (_lock)
                {
                    _gamepadsIds.Add(id);

                    if (SDL3JoyConPair.IsCombinable(_gamepadsInstanceIdsMapping))
                    {
                        _gamepadsIds.Remove(SDL3JoyConPair.Id);
                        _gamepadsIds.Add(SDL3JoyConPair.Id);
                        joyConPairConnected = true;
                    }
                }

                OnGamepadConnected?.Invoke(id);
                if (joyConPairConnected)
                {
                    OnGamepadConnected?.Invoke(SDL3JoyConPair.Id);
                }
            }
        }

        private void HandleJoyBatteryUpdated(uint joystickId, SDL_JoyBatteryEvent joyBatteryEvent)
        {
            Logger.Info?.Print(LogClass.Hid,
                $"{SDL_GetGamepadNameForID(joystickId)}, Battery percent: {joyBatteryEvent.percent}");
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                SDL3Driver.Instance.OnJoyStickConnected -= HandleJoyStickConnected;
                SDL3Driver.Instance.OnJoystickDisconnected -= HandleJoyStickDisconnected;

                // Simulate a full disconnect when disposing
                foreach (string id in _gamepadsIds)
                {
                    OnGamepadDisconnected?.Invoke(id);
                }

                lock (_lock)
                {
                    _gamepadsIds.Clear();
                }

                SDL3Driver.Instance.Dispose();
            }
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            Dispose(true);
        }

        public IGamepad GetGamepad(string id)
        {
            if (id == SDL3JoyConPair.Id)
            {
                lock (_lock)
                {
                    return SDL3JoyConPair.GetGamepad(_gamepadsInstanceIdsMapping);
                }
            }

            var gamepadInfo = GetGamepadInfoByGamepadId(id);

            if (SDL3JoyCon.IsJoyCon(gamepadInfo.Key))
            {
                return new SDL3JoyCon(gamepadInfo.Key, gamepadInfo.Value);
            }

            return new SDL3Gamepad(gamepadInfo.Key, gamepadInfo.Value);
        }
    }
}
