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
    // Loading a voice's models and switching the stream to it once it is warm (voice detailed design 8).
    public sealed partial class VoiceService
    {
        private void BeginLoading()
        {
            if (_wantedVoice == null && _library != null)
            {
                CrudResult<IReadOnlyList<VoiceConversionVoice>> voices = _library.List();
                _wantedVoice = voices.Ok ? VoiceModelLibrary.FirstUsable(voices.Value) : null;
            }

            bool switching = _stream != null;
            VoiceModelFiles files = Locate(_wantedVoice);
            string problem = _stage.IsAvailable ? _loader.Begin(files, _wantedVoice) : "the microphone's stage is unavailable.";
            if (problem == null)
            {
                _loadingPitchHz = FolderOf(_wantedVoice) != null ? files.MedianPitchHz : _library.MedianPitchOf(_wantedVoice);
            }
            else
            {
                _log?.Warning($"Voice conversion cannot load voice '{LogName(_wantedVoice)}': {problem}");
                if (switching)
                {
                    // The voice in use keeps converting.
                    _wantedVoice = _streamVoice;
                }
                else
                {
                    _state.Set(VoiceConversionState.Unavailable);
                }

                return;
            }

            if (!switching)
            {
                _state.Set(VoiceConversionState.Loading);
            }
        }

        private void Tick()
        {
            if (_loader.IsDone)
            {
                FinishLoading();
            }

            if (!IsAttached)
            {
                if (_muting)
                {
                    Discard();
                }

                return;
            }

            if (_next.IsActive)
            {
                _next.Step();
            }
            else if (!_loader.IsLoading && _wantedVoice != _streamVoice)
            {
                BeginLoading();
            }

            _state.Set(_file != null ? VoiceConversionState.PlayingFile : AttachedState(_stage.IsCapturing));
            try
            {
                Pump();
            }
            catch (Exception exception)
            {
                Failure = exception;
                if (MutesOnFailure(_storage.Profile.OnFailure) && _stage.IsAvailable)
                {
                    // The stage stays on and nothing is written: turning it off and on again would let a moment of the
                    // microphone through in between.
                    _log?.Error("Voice conversion failed. The microphone is kept out of the mix until it is turned off.", exception);
                    _stream?.Abandon();
                    _state.Set(VoiceConversionState.Failed);
                    _muting = true;
                }
                else
                {
                    _log?.Error("Voice conversion failed. The microphone goes back to the mix directly.", exception);
                    Stop();
                    _state.Set(VoiceConversionState.Failed);
                }
            }
        }

        private void FinishLoading()
        {
            bool switching = _stream != null && IsAttached;
            RvcStream stream = null;
            string voiceId = _loader.LoadingVoice;
            try
            {
                stream = _loader.Finish(PitchMatch.Applied(_autoPitch, PitchMatch.Semitones(_loadingPitchHz, _meter.MedianHz), _semitones), out voiceId);
                if (switching)
                {
                    // Warmed a little each frame, while the voice in use keeps converting.
                    _next.Begin(stream, voiceId);
                    _log?.Information($"Voice {LogName(voiceId)} loaded; warming up behind voice {LogName(_streamVoice)}.");
                    return;
                }

                _stereo ??= new float[RvcStream.DeviceRate * ChannelCount];
                _waiting ??= new float[RvcStream.DeviceRate];
                _block ??= new float[stream.BlockSamples];
                _output ??= new float[stream.BlockSamples * ChannelCount];
                _blockPitch ??= new float[stream.BlockSamples / BlockSamplesPerFrame];

                // The first runs compile the shaders and take far longer than a block, so they happen
                // here, where nobody is waiting for the audio yet.
                var silence = new float[stream.BlockSamples];
                for (int i = 0; i < VoiceWarmup.Blocks; i++)
                {
                    stream.Process(silence, silence);
                    Array.Clear(silence, 0, silence.Length);
                }

                _stream?.Dispose();
                _stream = stream;
                _streamVoice = voiceId;
                _voicePitchHz = _loadingPitchHz;
                ApplyTone();
                _log?.Information($"Voice conversion is ready with voice {LogName(voiceId)}: block {_stream.BlockSamples / 48} ms, algorithm delay {_stream.AlgorithmDelaySamples / 48} ms.");
                if (_wanted)
                {
                    Attach();
                }
                else
                {
                    _state.Set(VoiceConversionState.Off);
                    ReleaseTick();
                }
            }
            catch (Exception exception)
            {
                stream?.Dispose();
                if (switching)
                {
                    _log?.Error($"Voice {LogName(voiceId)} could not load; voice {LogName(_streamVoice)} keeps converting.", exception);
                    _wantedVoice = _streamVoice;
                    return;
                }

                _log?.Error("The voice conversion models could not start.", exception);
                _stream?.Dispose();
                _stream = null;
                _state.Set(VoiceConversionState.Failed);
            }
        }

        /// <summary>A voice's ID as the log shows it: the prototype's is empty, so it gets a name. Pure.</summary>
        /// <summary>The folder a voice ID names, or null for a registered voice or the prototype's. Pure.</summary>
        public static string FolderOf(string voiceId)
        {
            return voiceId != null && voiceId.StartsWith(FolderPrefix, StringComparison.Ordinal) ? voiceId.Substring(FolderPrefix.Length) : null;
        }

        // Registered voices by their ID, a voice chosen by its folder by the folder.
        private VoiceModelFiles Locate(string voiceId)
        {
            if (_library == null || voiceId == null)
            {
                return VoiceModelFiles.Unusable("There is no voice.");
            }

            string folder = FolderOf(voiceId);
            return folder != null ? _library.LocateFolder(folder) : _library.Locate(voiceId);
        }

        public static string LogName(string voiceId)
        {
            return voiceId == VoiceModelLibrary.PrototypeId ? "prototype" : voiceId ?? "none";
        }

        private void SwapIfReady()
        {
            if (!_next.IsWarm)
            {
                return;
            }

            if (_next.VoiceId != _wantedVoice)
            {
                // Chosen and then changed again before it was ready.
                _next.Dispose();
                return;
            }

            if (!_next.CanTakeOver(_stream.IsBusy))
            {
                return;
            }

            _log?.Information($"Voice {LogName(_next.VoiceId)} takes over from voice {LogName(_streamVoice)}.");
            _streamVoice = _next.VoiceId;
            _voicePitchHz = _loadingPitchHz;
            _stream.Dispose();
            _stream = _next.Take();
            ApplyPitch();
            ApplyTone();
        }
    }
}
