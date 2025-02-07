using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using static SDL3.SDL;


namespace Ryujinx.SDL3.Common
{
    public class SDL3Driver : IDisposable
    {
        private static SDL3Driver _instance;

        public static SDL3Driver Instance
        {
            get
            {
                _instance ??= new SDL3Driver();

                return _instance;
            }
        }

        public static Action<Action> MainThreadDispatcher { get; set; }

        private const SDL_InitFlags SdlInitFlags = SDL_InitFlags.SDL_INIT_GAMEPAD | SDL_InitFlags.SDL_INIT_AUDIO |
                                                   SDL_InitFlags.SDL_INIT_VIDEO;

        private bool _isRunning;
        private uint _refereceCount;
        private Thread _worker;

        public event Action<uint> OnJoyStickConnected;
        public event Action<uint> OnJoystickDisconnected;
        public event Action<uint, SDL_JoyBatteryEvent> OnJoyBatteryUpdated;

        private ConcurrentDictionary<uint, Action<SDL_Event>> _registeredWindowHandlers;

        private readonly Lock _lock = new();

        private SDL3Driver() { }

        public void Initialize()
        {
            lock (_lock)
            {
                _refereceCount++;

                if (_isRunning)
                {
                    return;
                }

                SDL_SetHint(SDL_HINT_APP_NAME, "Ryujinx");
                // SDL_SetHint(SDL_HINT_JOYSTICK_HIDAPI_PS4_RUMBLE, "1");
                // SDL_SetHint(SDL_HINT_JOYSTICK_HIDAPI_PS5_RUMBLE, "1");
                SDL_SetHint(SDL_HINT_JOYSTICK_ALLOW_BACKGROUND_EVENTS, "1");
                SDL_SetHint(SDL_HINT_JOYSTICK_HIDAPI_SWITCH_HOME_LED, "0");
                SDL_SetHint(SDL_HINT_VIDEO_ALLOW_SCREENSAVER, "1");
                //
                //
                // // NOTE: As of SDL2 2.24.0, joycons are combined by default but the motion source only come from one of them.
                // // We disable this behavior for now.
                SDL_SetHint(SDL_HINT_JOYSTICK_HIDAPI_COMBINE_JOY_CONS, "0");

                if (!SDL_Init(SdlInitFlags))
                {
                    string errorMessage = $"SDL3 initialization failed with error \"{SDL_GetError()}\"";

                    Logger.Error?.Print(LogClass.Application, errorMessage);

                    throw new Exception(errorMessage);
                }

                // First ensure that we only enable joystick events (for connected/disconnected).
                if (!SDL_GamepadEventsEnabled())
                {
                    Logger.Error?.PrintMsg(LogClass.Application,
                        "Couldn't change the state of game controller events.");
                }

                if (!SDL_JoystickEventsEnabled())
                {
                    Logger.Error?.PrintMsg(LogClass.Application,
                        $"Failed to enable joystick event polling: {SDL_GetError()}");
                }

                // Disable all joysticks information, we don't need them no need to flood the event queue for that.
                SDL_SetEventEnabled((uint)SDL_EventType.SDL_EVENT_JOYSTICK_AXIS_MOTION, false);
                SDL_SetEventEnabled((uint)SDL_EventType.SDL_EVENT_JOYSTICK_BALL_MOTION, false);
                SDL_SetEventEnabled((uint)SDL_EventType.SDL_EVENT_JOYSTICK_HAT_MOTION, false);
                SDL_SetEventEnabled((uint)SDL_EventType.SDL_EVENT_JOYSTICK_BUTTON_DOWN, false);
                SDL_SetEventEnabled((uint)SDL_EventType.SDL_EVENT_JOYSTICK_BUTTON_UP, false);

                SDL_SetEventEnabled((uint)SDL_EventType.SDL_EVENT_GAMEPAD_SENSOR_UPDATE, false);

                string gamepadDbPath = Path.Combine(AppDataManager.BaseDirPath, "SDL_GameControllerDB.txt");

                if (File.Exists(gamepadDbPath))
                {
                    SDL_AddGamepadMappingsFromFile(gamepadDbPath);
                }

                _registeredWindowHandlers = new ConcurrentDictionary<uint, Action<SDL_Event>>();
                _worker = new Thread(EventWorker);
                _isRunning = true;
                _worker.Start();
            }
        }

        public bool RegisterWindow(uint windowId, Action<SDL_Event> windowEventHandler)
        {
            return _registeredWindowHandlers.TryAdd(windowId, windowEventHandler);
        }

        public void UnregisterWindow(uint windowId)
        {
            _registeredWindowHandlers.Remove(windowId, out _);
        }

        private void HandleSDLEvent(ref SDL_Event evnt)
        {
            if (evnt.type == (uint)SDL_EventType.SDL_EVENT_GAMEPAD_ADDED)
            {
                var instanceId = evnt.jdevice.which;

                Logger.Debug?.Print(LogClass.Application, $"Added joystick instance id {instanceId}");

                OnJoyStickConnected?.Invoke(instanceId);
            }
            else if (evnt.type == (uint)SDL_EventType.SDL_EVENT_GAMEPAD_REMOVED)
            {
                var instanceId = evnt.jdevice.which;

                Logger.Debug?.Print(LogClass.Application, $"Removed joystick instance id {instanceId}");

                OnJoystickDisconnected?.Invoke(instanceId);
            }
            else if (evnt.type == (uint)SDL_EventType.SDL_EVENT_JOYSTICK_BATTERY_UPDATED)
            {
                OnJoyBatteryUpdated?.Invoke(evnt.jbattery.which, evnt.jbattery);
            }
            else if (evnt.type is >= (uint)SDL_EventType.SDL_EVENT_WINDOW_FIRST
                     and <= (uint)SDL_EventType.SDL_EVENT_WINDOW_LAST
                     or (uint)SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN
                     or (uint)SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP)
            {
                if (_registeredWindowHandlers.TryGetValue(evnt.window.windowID, out Action<SDL_Event> handler))
                {
                    handler(evnt);
                }
            }
        }
        private void PollEventAction()
        {
            while (SDL_PollEvent(out SDL_Event evnt))
            {
                HandleSDLEvent(ref evnt);
            }
        }
        private void EventWorker()
        {
            const int WaitTimeMs = 10;

            while (_isRunning)
            {
                MainThreadDispatcher?.Invoke(PollEventAction);

                Thread.Sleep(WaitTimeMs);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposing)
            {
                return;
            }

            lock (_lock)
            {
                if (_isRunning)
                {
                    _refereceCount--;

                    if (_refereceCount == 0)
                    {
                        _isRunning = false;

                        _worker?.Join();

                        SDL_Quit();

                        OnJoyStickConnected = null;
                        OnJoystickDisconnected = null;
                    }
                }
            }
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            Dispose(true);
        }
    }
}
