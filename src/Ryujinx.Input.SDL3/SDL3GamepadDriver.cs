using Ryujinx.Common.Logging;
using Ryujinx.Input.SDL3;
using Ryujinx.SDL3.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using static SDL3.SDL;

namespace Ryujinx.Input.SDl3
{
    public class SDL3GamepadDriver : IGamepadDriver
    {
        private readonly Dictionary<uint, GamepadInfo> _gamepadsInstanceIdsMapping;
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
            _gamepadsInstanceIdsMapping = new Dictionary<uint, GamepadInfo>();
            _gamepadsIds = new List<string>();

            SDL3Driver.Instance.Initialize();
            SDL3Driver.Instance.OnJoyStickConnected += HandleJoyStickConnected;
            SDL3Driver.Instance.OnJoystickDisconnected += HandleJoyStickDisconnected;
            SDL3Driver.Instance.OnJoyBatteryUpdated += HandleJoyBatteryUpdated;

            // IntPtr joystickArray = SDL_GetJoysticks(out int count);
            //
            // var joystickIDs = new int[count];
            // Marshal.Copy(joystickArray, joystickIDs, 0, count);
            //
            // for (int i = 0; i < count; i++)
            // {
            //     HandleJoyStickConnected((uint)joystickIDs[i]);
            // }
        }

        private string GenerateGamepadId(uint joystickIndex)
        {
            int bufferSize = 33;
            Span<byte> pszGUID = stackalloc byte[bufferSize];
            SDL_GUIDToString(SDL_GetJoystickGUIDForID(joystickIndex), pszGUID, bufferSize);
            var guid = Encoding.UTF8.GetString(pszGUID);

            // if (guid == new SDL_GUID())
            // {
            //     return null;
            // }

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

        private GamepadInfo GetJoystickIndexByGamepadId(string id)
        {
            lock (_lock)
            {
                return _gamepadsInstanceIdsMapping.FirstOrDefault(x => x.Value.driverId == id).Value;
            }
        }

        private void HandleJoyStickDisconnected(uint joystickInstanceId)
        {
            bool joyConPairDisconnected = false;
            if (!_gamepadsInstanceIdsMapping.Remove(joystickInstanceId, out GamepadInfo gamepadInfo))
                return;

            lock (_lock)
            {
                _gamepadsIds.Remove(gamepadInfo.driverId);
                SDL_CloseGamepad(gamepadInfo.gamepadHandle);
                if (!SDL3JoyConPair.IsCombinable(_gamepadsInstanceIdsMapping))
                {
                    _gamepadsIds.Remove(SDL3JoyConPair.Id);
                    joyConPairDisconnected = true;
                }
            }

            OnGamepadDisconnected?.Invoke(gamepadInfo.driverId);
            if (joyConPairDisconnected)
            {
                OnGamepadDisconnected?.Invoke(SDL3JoyConPair.Id);
            }
        }

        private void HandleJoyStickConnected(uint gamepadInstanceId)
        {
            bool joyConPairConnected = false;

            if (_gamepadsInstanceIdsMapping.ContainsKey(gamepadInstanceId))
            {
                // Sometimes a JoyStick connected event fires after the app starts even though it was connected before
                // so it is rejected to avoid doubling the entries.
                return;
            }

            string id = GenerateGamepadId(gamepadInstanceId);
            if (id == null)
            {
                return;
            }

            if (_gamepadsInstanceIdsMapping.TryAdd(gamepadInstanceId, new GamepadInfo(id, SDL_OpenGamepad(gamepadInstanceId))))
            {
                lock (_lock)
                {
                    if (gamepadInstanceId <= _gamepadsIds.FindLastIndex(_ => true))
                    {
                        // _gamepadsIds.Insert(joystickDeviceId, id);
                    }
                    else
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

        private void HandleJoyBatteryUpdated(uint joystickDeviceId, SDL_JoyBatteryEvent joyBatteryEvent)
        {
            Logger.Info?.Print(LogClass.Hid,
                $"{SDL_GetGamepadNameForID(joystickDeviceId)}, Battery percent: {joyBatteryEvent.percent}");
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

            var gamepadInfo = GetJoystickIndexByGamepadId(id);
            if (gamepadInfo == null)
            {
                return null;
            }

            if (SDL3JoyCon.IsJoyCon(gamepadInfo.gamepadHandle))
            {
                return new SDL3JoyCon(gamepadInfo);
            }

            return new SDL3Gamepad(gamepadInfo);
        }
    }
}
