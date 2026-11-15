using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VirtualVessel.Audio;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Core.State;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.Diagnostics.Performance;
using VirtualVessel.ProjectData.Records;
using VirtualVessel.UI.Voice;
using VirtualVessel.Voice.Models;
using VirtualVessel.Voice.Rvc;

namespace VirtualVessel.Voice
{
    // The real-time path: the microphone's audio through the voice and back, block by block (voice detailed design 9). Moved here unchanged; it is performance-sensitive code.
    public sealed partial class VoiceService
    {
        /// <summary>What to show while on: running only when there is a voice to convert. Pure.</summary>
        public static VoiceConversionState AttachedState(bool capturing)
        {
            return capturing ? VoiceConversionState.Running : VoiceConversionState.NoMicrophone;
        }

        /// <summary>Samples to leave out so that no more than <paramref name="maxBlocks"/> wait. Pure.</summary>
        public static int SamplesToDrop(int waiting, int block, int maxBlocks)
        {
            int excess = waiting - maxBlocks * block;
            return excess <= 0 ? 0 : (excess + block - 1) / block * block;
        }

        /// <summary>Mixes interleaved stereo into mono, for a voice that a device may give on one channel. Pure apart from the target.</summary>
        public static void ToMono(ReadOnlySpan<float> stereo, Span<float> mono)
        {
            for (int i = 0; i < mono.Length; i++)
            {
                mono[i] = (stereo[i * ChannelCount] + stereo[i * ChannelCount + 1]) * 0.5f;
            }
        }

        /// <summary>Copies mono to both channels. Pure apart from the target.</summary>
        public static void ToStereo(ReadOnlySpan<float> mono, Span<float> stereo)
        {
            for (int i = 0; i < mono.Length; i++)
            {
                stereo[i * ChannelCount] = mono[i];
                stereo[i * ChannelCount + 1] = mono[i];
            }
        }

        private void Attach()
        {
            _waitingSamples = 0;
            _primed = false;
            _startedAt.Clear();
            _stage.SetEnabled(true);
            _state.Set(AttachedState(_stage.IsCapturing));
        }

        private void Stop()
        {
            if (IsAttached)
            {
                _stage.SetEnabled(false);
            }

            _stream?.Abandon();
        }

        /// <summary>
        /// The delay's parts in milliseconds into <paramref name="parts"/>, in the order of DelayParts, ending
        /// with their sum. Each queue's part is its average fill, which by Little's law is the average wait in it.
        /// Pure apart from writing the caller's buffer, which is reused every frame to avoid garbage.
        /// </summary>
        public static void DelayMs(AudioStageDelay outside, int waitingSamples, double workMs, int algorithmSamples, Span<double> parts)
        {
            const double MsPerSample = 1000.0 / RvcStream.DeviceRate;
            parts[0] = outside.CaptureDeviceMs;
            parts[1] = outside.StageInputMs;
            parts[2] = waitingSamples * MsPerSample;
            parts[3] = workMs;
            parts[4] = algorithmSamples * MsPerSample;
            parts[5] = outside.StageOutputMs;
            parts[6] = outside.MonitorMs;
            parts[7] = outside.OutputBufferMs;
            parts[8] = outside.OutputDeviceMs;
            double total = 0.0;
            for (int i = 0; i < 9; i++)
            {
                total += parts[i];
            }

            parts[9] = total;
        }

        private void Pump()
        {
            Collect();
            if (_stream.Advance(_block, false))
            {
                Deliver();
            }

            SwapIfReady();
            StartBlock();
            RecordDelay();
        }

        // What waits here stays until the next frame, so one sample a frame is its average. Only while the
        // totals record timers, so the call to the engine costs nothing otherwise.
        private void RecordDelay()
        {
            if (_metrics == null || !_metrics.IsDetailedEnabled || _file != null)
            {
                return;
            }

            DelayMs(_stage.Delay, _waitingSamples, _lastWorkMs, _stream.AlgorithmDelaySamples, _delayParts);
            for (int i = 0; i < _delayParts.Length; i++)
            {
                _delayTimers[i].Record(TimeSpan.FromMilliseconds(_delayParts[i]));
            }
        }

        private void StartBlock()
        {
            int block = _stream.BlockSamples;
            if (_stream.CanStart && _waitingSamples >= block)
            {
                int drop = SamplesToDrop(_waitingSamples, block, MaxWaitingBlocks);
                if (drop > 0)
                {
                    Consume(drop);
                    _dropped += drop / block;
                    _droppedCounter?.Add(drop / block);
                }

                // The microphone is cleaned as it is captured, if the user has it on (audio detailed design 8.6). A file
                // in its place is cleaned here the same way, so it sounds as the microphone would. The block is a whole
                // number of the denoiser's 10 ms frames, so nothing waits for it.
                if (_file != null && _stage.Denoises)
                {
                    _stage.Denoise(_waiting, block);
                }

                _startedAt.Enqueue(System.Diagnostics.Stopwatch.GetTimestamp());
                _stream.Start(new ReadOnlySpan<float>(_waiting, 0, block));
                Consume(block);
            }
        }

        // The user's pitch, from the frames of the block just delivered that no longer change, until the meter keeps
        // its median. Its buffer is reused: this runs every block.
        private void MeasurePitch()
        {
            if (_meter.IsLocked)
            {
                return;
            }

            _stream.CopySettledPitch(_blockPitch);
            if (_meter.Add(_blockPitch))
            {
                ApplyPitch();
            }

            if (_meter.IsLocked)
            {
                // Kept for this PC, so the next start matches the pitch from its first block.
                SaveUserPitch(_meter.MedianHz);
            }
        }

        private void Collect()
        {
            int room = _waiting.Length - _waitingSamples;
            int read = _stage.Read(_stereo, Math.Min(room, _stereo.Length / ChannelCount));
            if (_file != null)
            {
                // What the microphone captured is dropped, so it does not arrive late after the file.
                CollectFile(room);
                return;
            }

            ToMono(new ReadOnlySpan<float>(_stereo, 0, read * ChannelCount), new Span<float>(_waiting, _waitingSamples, read));
            _waitingSamples += read;
        }

        // At real time, like a microphone, so that it can be heard as it will sound on a stream.
        private void CollectFile(int room)
        {
            _fileClock.Start();
            long due = _fileClock.ElapsedMilliseconds * RvcStream.DeviceRate / 1000;
            int take = (int)Math.Min(room, Math.Min(due - _fileRead, _file.Length - _fileRead));
            Array.Copy(_file, _fileRead, _waiting, _waitingSamples, take);
            _fileRead += take;
            _waitingSamples += take;
            if (_fileRead == _file.Length)
            {
                _file = null;
            }
        }

        private void Consume(int samples)
        {
            _waitingSamples -= samples;
            Array.Copy(_waiting, samples, _waiting, 0, _waitingSamples);
        }

        private void Deliver()
        {
            // In place on the block the voice made: no copy, no allocation, and no latency (CLAUDE.md 23).
            _tone.Process(_block);
            ToStereo(_block, _output);
            if (!_primed)
            {
                _primed = true;
                var margin = new float[MarginFrames * ChannelCount];
                _stage.Write(margin, MarginFrames);
            }

            _stage.Write(_output, _block.Length);
            _blocks++;
            MeasurePitch();
            long started = _startedAt.Count > 0 ? _startedAt.Dequeue() : System.Diagnostics.Stopwatch.GetTimestamp();
            double work = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            _workTimer?.Record(TimeSpan.FromMilliseconds(work));
            _lastWorkMs = work;
            RvcTimings timings = _stream.Timings;
            _issueTimer?.Record(TimeSpan.FromMilliseconds(timings.Issue));
            double[] stages = { timings.Prepare, timings.Features, timings.Pitch, timings.Generator, timings.Finish };
            for (int i = 0; i < stages.Length; i++)
            {
                _stageTimers[i]?.Record(TimeSpan.FromMilliseconds(stages[i]));
            }

            LastTimings = timings;
            _stats = new VoiceConversionStats(_stream.BlockSamples / 48.0, work, _stream.Timings.Issue, _blocks, _dropped);
        }
    }
}
