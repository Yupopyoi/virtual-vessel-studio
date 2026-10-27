using System;
using VirtualVessel.Diagnostics.Logging;

namespace VirtualVessel.Tracking
{
    /// <summary>
    /// Replaying a developer's recording in place of the camera and the provider (tracking detailed design
    /// 12.4). Developer-only, kept apart from the tracking that users run.
    /// </summary>
    public sealed partial class TrackingService
    {
        /// <summary>
        /// Replays <paramref name="replay"/> in place of the camera and the provider, starting tracking
        /// for it if it was off; a replay already running is replaced. Starting tracking for a replay is
        /// not saved (tracking detailed design 12.4).
        /// </summary>
        internal void BeginReplay(ITrackingProvider replay)
        {
            if (replay == null)
            {
                throw new ArgumentNullException(nameof(replay));
            }

            if (_replay == null)
            {
                _wantedBeforeReplay = _wanted;
                if (_wanted)
                {
                    StopProvider();
                }
            }
            else
            {
                StopReplayProvider();
            }

            _replay = replay;
            ChooseCorrections();
            _wanted = true;
            _processor.Reset();
            _replay.Start(_options.Value);
            _log?.Information("Tracking replays a recording.");
            Refresh();
        }

        /// <summary>Ends the replay and goes back to the state before it: the camera and the provider if tracking was on.</summary>
        internal void EndReplay()
        {
            if (_replay == null)
            {
                return;
            }

            StopReplayProvider();
            _wanted = false;
            _processor.Reset();
            _log?.Information("Tracking stopped replaying.");
            if (_wantedBeforeReplay)
            {
                Start();
            }
            else
            {
                Refresh();
            }

            ReplayEnded?.Invoke();
        }

        private void StopReplayProvider()
        {
            _replay?.Stop();
            _replay?.Dispose();
            _replay = null;
            ChooseCorrections();
        }
    }
}
