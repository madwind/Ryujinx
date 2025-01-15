using Ryujinx.Audio.Common;
using Ryujinx.Audio.Integration;
using Ryujinx.Common.Logging;
using Ryujinx.Memory;
using Ryujinx.SDL3.Common;
using System;
using System.Collections.Concurrent;
using System.Threading;
using static Ryujinx.Audio.Integration.IHardwareDeviceDriver;
using static SDL3.SDL;

namespace Ryujinx.Audio.Backends.SDL3
{
    public class SDL3HardwareDeviceDriver : IHardwareDeviceDriver
    {
        private readonly ManualResetEvent _updateRequiredEvent;
        private readonly ManualResetEvent _pauseEvent;
        private readonly ConcurrentDictionary<SDL3HardwareDeviceSession, byte> _sessions;

        private readonly bool _supportSurroundConfiguration;

        public float Volume { get; set; }

        public SDL3HardwareDeviceDriver()
        {
            _updateRequiredEvent = new ManualResetEvent(false);
            _pauseEvent = new ManualResetEvent(true);
            _sessions = new ConcurrentDictionary<SDL3HardwareDeviceSession, byte>();

            SDL3Driver.Instance.Initialize();

            if (!SDL_GetAudioDeviceFormat(SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, out var spec, out int sample_frames))
            {
                Logger.Error?.Print(LogClass.Application,
                    $"SDL_GetDefaultAudioInfo failed with error \"{SDL_GetError()}\"");

                _supportSurroundConfiguration = true;
            }
            else
            {
                _supportSurroundConfiguration = spec.channels >= 6;
            }

            Volume = 1f;
        }

        public static bool IsSupported => IsSupportedInternal();

        private static bool IsSupportedInternal()
        {
            var device = OpenStream(SampleFormat.PcmInt16, Constants.TargetSampleRate, Constants.ChannelCountMax,
                Constants.TargetSampleCount, null);

            if (device != 0)
            {
                SDL_DestroyAudioStream(device);
            }

            return device != 0;
        }

        public ManualResetEvent GetUpdateRequiredEvent()
        {
            return _updateRequiredEvent;
        }

        public ManualResetEvent GetPauseEvent()
        {
            return _pauseEvent;
        }

        public IHardwareDeviceSession OpenDeviceSession(Direction direction, IVirtualMemoryManager memoryManager,
            SampleFormat sampleFormat, uint sampleRate, uint channelCount)
        {
            if (channelCount == 0)
            {
                channelCount = 2;
            }

            if (sampleRate == 0)
            {
                sampleRate = Constants.TargetSampleRate;
            }

            if (direction != Direction.Output)
            {
                throw new NotImplementedException("Input direction is currently not implemented on SDL3 backend!");
            }

            SDL3HardwareDeviceSession session = new(this, memoryManager, sampleFormat, sampleRate, channelCount);

            _sessions.TryAdd(session, 0);

            return session;
        }

        internal bool Unregister(SDL3HardwareDeviceSession session)
        {
            return _sessions.TryRemove(session, out _);
        }

        private static SDL_AudioSpec GetSDL3Spec(SampleFormat requestedSampleFormat, uint requestedSampleRate,
            uint requestedChannelCount, uint sampleCount)
        {
            return new SDL_AudioSpec
            {
                channels = (byte)requestedChannelCount,
                format = GetSDL3Format(requestedSampleFormat),
                freq = (int)requestedSampleRate,
            };
        }

        internal static SDL_AudioFormat GetSDL3Format(SampleFormat format)
        {
            return format switch
            {
                SampleFormat.PcmInt8 => SDL_AudioFormat.SDL_AUDIO_S8,
                SampleFormat.PcmInt16 => SDL_AudioFormat.SDL_AUDIO_S16,
                SampleFormat.PcmInt32 => SDL_AudioFormat.SDL_AUDIO_S32,
                SampleFormat.PcmFloat => SDL_AudioFormat.SDL_AUDIO_F32,
                _ => throw new ArgumentException($"Unsupported sample format {format}"),
            };
        }

        internal static nint OpenStream(SampleFormat requestedSampleFormat, uint requestedSampleRate,
            uint requestedChannelCount, uint sampleCount, SDL_AudioStreamCallback callback)
        {       
            SDL_AudioSpec spec = GetSDL3Spec(requestedSampleFormat, requestedSampleRate, requestedChannelCount,
                sampleCount);

            var stream = SDL_OpenAudioDeviceStream(SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, ref spec,null,IntPtr.Zero);

            if (stream == 0)
            {
                Logger.Error?.Print(LogClass.Application,
                    $"SDL3 open audio device initialization failed with error \"{SDL_GetError()}\"");

                return 0;
            }


            return stream;
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (SDL3HardwareDeviceSession session in _sessions.Keys)
                {
                    session.Dispose();
                }

                SDL3Driver.Instance.Dispose();

                _pauseEvent.Dispose();
            }
        }

        public bool SupportsSampleRate(uint sampleRate)
        {
            return true;
        }

        public bool SupportsSampleFormat(SampleFormat sampleFormat)
        {
            return sampleFormat != SampleFormat.PcmInt24;
        }

        public bool SupportsChannelCount(uint channelCount)
        {
            if (channelCount == 6)
            {
                return _supportSurroundConfiguration;
            }

            return true;
        }

        public bool SupportsDirection(Direction direction)
        {
            // TODO: add direction input when supported.
            return direction == Direction.Output;
        }
    }
}
