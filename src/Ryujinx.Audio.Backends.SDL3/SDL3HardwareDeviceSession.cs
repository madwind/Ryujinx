using Ryujinx.Audio.Backends.Common;
using Ryujinx.Audio.Common;
using Ryujinx.Common.Memory;
using Ryujinx.Memory;
using System;
using System.Collections.Concurrent;
using System.Threading;
using static SDL3.SDL;

namespace Ryujinx.Audio.Backends.SDL3
{
    class SDL3HardwareDeviceSession : HardwareDeviceSessionOutputBase
    {
        private readonly SDL3HardwareDeviceDriver _driver;
        private readonly ConcurrentQueue<SDL3AudioBuffer> _queuedBuffers;
        private readonly DynamicRingBuffer _ringBuffer;
        private ulong _playedSampleCount;
        private readonly ManualResetEvent _updateRequiredEvent;
        private nint _outputStream;
        private bool _hasSetupError;
        private readonly SDL_AudioStreamCallback _callbackDelegate;
        private readonly int _bytesPerFrame;
        private bool _started;
        private float _volume;
        private readonly SDL_AudioFormat _nativeSampleFormat;

        public SDL3HardwareDeviceSession(SDL3HardwareDeviceDriver driver, IVirtualMemoryManager memoryManager,
            SampleFormat requestedSampleFormat, uint requestedSampleRate, uint requestedChannelCount) : base(
            memoryManager, requestedSampleFormat, requestedSampleRate, requestedChannelCount)
        {
            _driver = driver;
            _updateRequiredEvent = _driver.GetUpdateRequiredEvent();
            _queuedBuffers = new ConcurrentQueue<SDL3AudioBuffer>();
            _ringBuffer = new DynamicRingBuffer();
            _callbackDelegate = Update;
            _bytesPerFrame = BackendHelper.GetSampleSize(RequestedSampleFormat) * (int)RequestedChannelCount;
            _nativeSampleFormat = SDL3HardwareDeviceDriver.GetSDL3Format(RequestedSampleFormat);
            _started = false;
            _volume = 1f;
        }

        private void EnsureAudioStreamSetup()
        {
            bool needAudioSetup = _outputStream == 0 && !_hasSetupError;

            if (needAudioSetup)
            {
                nint newOutputStream = SDL3HardwareDeviceDriver.OpenStream(RequestedSampleFormat, RequestedSampleRate,
                    RequestedChannelCount, _callbackDelegate);

                _hasSetupError = newOutputStream == 0;

                if (!_hasSetupError)
                {
                    if (_outputStream != 0)
                    {
                        SDL_DestroyAudioStream(_outputStream);
                    }

                    _outputStream = newOutputStream;

                    if (_started)
                    {
                        SDL_ResumeAudioStreamDevice(_outputStream);
                    }
                    else
                    {
                        SDL_PauseAudioStreamDevice(_outputStream);
                    }
                }
            }
        }

        private unsafe void Update(nint userdata, nint stream, int additionalAmount, int totalAmount)
        {
            int maxFrameCount = (int)GetSampleCount(additionalAmount);
            int bufferedFrames = _ringBuffer.Length / _bytesPerFrame;

            int frameCount = Math.Min(bufferedFrames, maxFrameCount);

            if (frameCount == 0)
            {
                return;
            }

            using SpanOwner<byte> samplesOwner = SpanOwner<byte>.Rent(frameCount * _bytesPerFrame);
            Span<byte> samples = samplesOwner.Span;
            int samplesLength = samples.Length;
            _ringBuffer.Read(samples, 0, samplesLength);

            fixed (byte* p = samples)
            {
                nint pBuffer = (nint)p;
                SDL_PutAudioStreamData(stream, pBuffer, samplesLength);
            }

            ulong sampleCount = GetSampleCount(samplesLength);

            ulong availaibleSampleCount = sampleCount;

            bool needUpdate = false;

            while (availaibleSampleCount > 0 && _queuedBuffers.TryPeek(out SDL3AudioBuffer driverBuffer))
            {
                ulong sampleStillNeeded = driverBuffer.SampleCount - Interlocked.Read(ref driverBuffer.SamplePlayed);
                ulong playedAudioBufferSampleCount = Math.Min(sampleStillNeeded, availaibleSampleCount);

                ulong currentSamplePlayed =
                    Interlocked.Add(ref driverBuffer.SamplePlayed, playedAudioBufferSampleCount);
                availaibleSampleCount -= playedAudioBufferSampleCount;

                if (currentSamplePlayed == driverBuffer.SampleCount)
                {
                    _queuedBuffers.TryDequeue(out _);

                    needUpdate = true;
                }

                Interlocked.Add(ref _playedSampleCount, playedAudioBufferSampleCount);
            }

            // Notify the output if needed.
            if (needUpdate)
            {
                _updateRequiredEvent.Set();
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
            EnsureAudioStreamSetup();

            if (_outputStream != 0)
            {
                SDL3AudioBuffer driverBuffer = new(buffer.DataPointer, GetSampleCount(buffer));

                _ringBuffer.Write(buffer.Data, 0, buffer.Data.Length);

                _queuedBuffers.Enqueue(driverBuffer);
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
            if (_outputStream != 0)
            {
                SDL_SetAudioStreamGain(_outputStream, _volume);
            }
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
            if (!_queuedBuffers.TryPeek(out SDL3AudioBuffer driverBuffer))
            {
                return true;
            }

            return driverBuffer.DriverIdentifier != buffer.DataPointer;
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
