using Ryujinx.Audio.Backends.Common;
using Ryujinx.Audio.Common;
using Ryujinx.Common.Logging;
using Ryujinx.Memory;
using System;
using System.Threading;
using static SDL3.SDL;

namespace Ryujinx.Audio.Backends.SDL3
{
    class SDL3HardwareDeviceSession : HardwareDeviceSessionOutputBase
    {
        private readonly SDL3HardwareDeviceDriver _driver;
        private ulong _playedSampleCount;
        private readonly ManualResetEvent _updateRequiredEvent;
        private nint _outputStream;
        private bool _hasSetupError;
        private uint _sampleCount;
        private bool _started;
        private float _volume;
        private readonly SDL_AudioFormat _nativeSampleFormat;

        public SDL3HardwareDeviceSession(SDL3HardwareDeviceDriver driver, IVirtualMemoryManager memoryManager,
            SampleFormat requestedSampleFormat, uint requestedSampleRate, uint requestedChannelCount) : base(
            memoryManager, requestedSampleFormat, requestedSampleRate, requestedChannelCount)
        {
            _driver = driver;
            _updateRequiredEvent = _driver.GetUpdateRequiredEvent();
            _nativeSampleFormat = SDL3HardwareDeviceDriver.GetSDL3Format(RequestedSampleFormat);
            _sampleCount = uint.MaxValue;
            _started = false;
            _volume = 1f;
        }

        private void EnsureAudioStreamSetup(AudioBuffer buffer)
        {
            uint bufferSampleCount = (uint)GetSampleCount(buffer);
            bool needAudioSetup = (_outputStream == 0 && !_hasSetupError) ||
                                  (bufferSampleCount >= Constants.TargetSampleCount &&
                                   bufferSampleCount < _sampleCount);

            if (needAudioSetup)
            {
                _sampleCount = Math.Max(Constants.TargetSampleCount, bufferSampleCount);

                var newOutputStream = SDL3HardwareDeviceDriver.OpenStream(RequestedSampleFormat, RequestedSampleRate,
                    RequestedChannelCount);

                _hasSetupError = newOutputStream == 0;

                if (!_hasSetupError)
                {
                    if (_outputStream != 0)
                    {
                        SDL_DestroyAudioStream(_outputStream);
                    }

                    _outputStream = newOutputStream;
                    SDL_ResumeAudioStreamDevice(_outputStream);
                    Logger.Info?.Print(LogClass.Audio,
                        $"New audio stream setup with a target sample count of {_sampleCount}");
                }
            }
        }

        public override ulong GetPlayedSampleCount()
        {
            return Interlocked.Read(ref _playedSampleCount);
        }

        public override float GetVolume()
        {
            return _volume;
        }

        public override void PrepareToClose() { }

        public override void QueueBuffer(AudioBuffer buffer)
        {
            EnsureAudioStreamSetup(buffer);
            if (_outputStream != 0)
            {
                if (SDL_GetAudioStreamAvailable(_outputStream) < int.MaxValue)
                {
                    unsafe
                    {
                        fixed (byte* samplesPtr = buffer.Data)
                        {
                            var len = buffer.Data.Length;
                            IntPtr src = (IntPtr)samplesPtr;
                            byte* dst = stackalloc byte[len];
                            IntPtr dstPtr = (IntPtr)dst;
                            SDL_MixAudio(dstPtr, src, _nativeSampleFormat, (uint)len, _driver.Volume);
                            SDL_PutAudioStreamData(_outputStream, dstPtr, len);
                        }
                    }
                }
            }
            else
            {
                Interlocked.Add(ref _playedSampleCount, GetSampleCount(buffer));

                _updateRequiredEvent.Set();
            }
        }

        public override void SetVolume(float volume)
        {
            _volume = volume;
        }

        public override void Start()
        {
            if (!_started)
            {
                if (_outputStream != 0)
                {
                    SDL_ResumeAudioStreamDevice(_outputStream);
                }

                _started = true;
            }
        }

        public override void Stop()
        {
            if (_started)
            {
                if (_outputStream != 0)
                {
                    SDL_PauseAudioStreamDevice(_outputStream);
                }

                _started = false;
            }
        }

        public override void UnregisterBuffer(AudioBuffer buffer) { }

        public override bool WasBufferFullyConsumed(AudioBuffer buffer)
        {
            return true;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing && _driver.Unregister(this))
            {
                PrepareToClose();
                Stop();

                if (_outputStream != 0)
                {
                    SDL_DestroyAudioStream(_outputStream);
                }
            }
        }

        public override void Dispose()
        {
            Dispose(true);
        }
    }
}
