using Ryujinx.Audio.Backends.Common;
using Ryujinx.Audio.Common;
using Ryujinx.Common.Logging;
using Ryujinx.Common.Memory;
using Ryujinx.Memory;
using System;
using System.Buffers;
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
            _queuedBuffers = new ConcurrentQueue<SDL3AudioBuffer>();
            _ringBuffer = new DynamicRingBuffer();
            _callbackDelegate = Update;
            _bytesPerFrame = BackendHelper.GetSampleSize(RequestedSampleFormat) * (int)RequestedChannelCount;
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

                nint newOutputStream = SDL3HardwareDeviceDriver.OpenStream(RequestedSampleFormat, RequestedSampleRate,
                    RequestedChannelCount, _sampleCount, _callbackDelegate);

                _hasSetupError = newOutputStream == 0;

                if (!_hasSetupError)
                {
                    if (_outputStream != 0)
                    {
                        SDL_DestroyAudioStream(_outputStream);
                    }

                    _outputStream = newOutputStream;

                    // SDL_PauseAudioDevice(_outputStream, _started ? 0 : 1);
                    SDL_ResumeAudioStreamDevice(_outputStream);

                    Logger.Info?.Print(LogClass.Audio,
                        $"New audio stream setup with a target sample count of {_sampleCount}");
                }
            }
        }

        private unsafe void Update(nint userdata, nint stream, int additional_amount, int total_amount)
        {
            Span<byte> streamSpan = new((void*)stream, additional_amount);

            int maxFrameCount = (int)GetSampleCount(additional_amount);
            int bufferedFrames = _ringBuffer.Length / _bytesPerFrame;

            int frameCount = Math.Min(bufferedFrames, maxFrameCount);

            if (frameCount == 0)
            {
                // SDL3 left the responsibility to the user to clear the buffer.
                streamSpan.Clear();

                return;
            }

            using SpanOwner<byte> samplesOwner = SpanOwner<byte>.Rent(frameCount * _bytesPerFrame);
            using SpanOwner<byte> destinationOwner = SpanOwner<byte>.Rent(frameCount * _bytesPerFrame);
            
            Span<byte> samples = samplesOwner.Span;
            Span<byte> destinationBuffer = destinationOwner.Span;

            _ringBuffer.Read(samples, 0, samples.Length);

            fixed (byte* pSrc = samples)
            fixed (byte* pDst = destinationBuffer)
            {
                nint pStreamSrc = (nint)pSrc;
                nint pStreamDst = (nint)pDst;

                // Zero the dest buffer
                streamSpan.Clear();
                
                // Apply volume to written data
                SDL_MixAudio(pStreamDst, pStreamSrc, _nativeSampleFormat, (uint)samples.Length, _driver.Volume);
                SDL_PutAudioStreamData(stream, pStreamSrc, samples.Length);
            }

            ulong sampleCount = GetSampleCount(samples.Length);

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
            EnsureAudioStreamSetup(buffer);

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
