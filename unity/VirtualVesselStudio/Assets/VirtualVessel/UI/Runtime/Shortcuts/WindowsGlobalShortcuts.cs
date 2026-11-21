using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using VirtualVessel.Diagnostics.Logging;

namespace VirtualVessel.UI.Shortcuts
{
    /// <summary>
    /// <see cref="IGlobalShortcuts"/> with Windows' RegisterHotKey. A hot key belongs to the thread that registered
    /// it, so one thread of its own registers them and waits for WM_HOTKEY in its message loop; it does not depend on
    /// Unity's frames, which slow down while the app is behind a game.
    /// </summary>
    public sealed class WindowsGlobalShortcuts : IGlobalShortcuts
    {
        private const int WmHotKey = 0x0312;
        private const int WmQuit = 0x0012;
        private const int WmApply = 0x8001; // WM_APP + 1: the chords or the pause changed.
        private const int ModNoRepeat = 0x4000;
        private const int ErrorHotKeyAlreadyRegistered = 1409;

        private readonly ILog _log;
        private readonly object _gate = new object();
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _started = new ManualResetEventSlim();

        // Wanted by the callers; under _gate.
        private IReadOnlyCollection<KeyChord> _wanted = Array.Empty<KeyChord>();
        private bool _paused;

        // The listening thread's own.
        private readonly Dictionary<int, KeyChord> _registered = new Dictionary<int, KeyChord>();
        private int _nextId = 1;

        private volatile KeyChord[] _refused = Array.Empty<KeyChord>();
        private volatile uint _threadId;
        private int _heldCount;
        private bool _disposed;

        public WindowsGlobalShortcuts(ILog log)
        {
            _log = log;
            _thread = new Thread(Run) { IsBackground = true, Name = "VirtualVessel.GlobalShortcuts" };
            _thread.Start();
            _started.Wait(TimeSpan.FromSeconds(5));
        }

        public event Action<KeyChord> Pressed;

        public event Action RefusedChanged;

        public IReadOnlyCollection<KeyChord> Refused => _refused;

        /// <summary>How many chords Windows has given this listener now; for tests.</summary>
        internal int HeldCount => Volatile.Read(ref _heldCount);

        public bool Paused
        {
            get
            {
                lock (_gate)
                {
                    return _paused;
                }
            }

            set
            {
                lock (_gate)
                {
                    if (_paused == value)
                    {
                        return;
                    }

                    _paused = value;
                }

                Wake();
            }
        }

        public void Listen(IReadOnlyCollection<KeyChord> chords)
        {
            lock (_gate)
            {
                _wanted = (chords ?? Array.Empty<KeyChord>()).Distinct().ToArray();
            }

            Wake();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_threadId != 0)
            {
                PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
            }

            // The thread unregisters its keys before it ends; a key left registered would stay taken from other apps.
            if (!_thread.Join(TimeSpan.FromSeconds(2)))
            {
                _log?.Warning("The global shortcuts' thread did not end in time.");
            }

            _started.Dispose();
        }

        /// <summary>
        /// The chords to let go of and the ones to take, from those held and those wanted (none while paused). Pure.
        /// </summary>
        public static (IReadOnlyList<int> Release, IReadOnlyList<KeyChord> Take) Changes(IReadOnlyDictionary<int, KeyChord> held, IReadOnlyCollection<KeyChord> wanted, bool paused)
        {
            var target = new HashSet<KeyChord>(paused ? Enumerable.Empty<KeyChord>() : wanted);
            var release = held.Where(pair => !target.Contains(pair.Value)).Select(pair => pair.Key).ToList();
            var kept = new HashSet<KeyChord>(held.Where(pair => target.Contains(pair.Value)).Select(pair => pair.Value));
            var take = target.Where(chord => !kept.Contains(chord)).ToList();
            return (release, take);
        }

        private void Wake()
        {
            if (_threadId != 0)
            {
                PostThreadMessage(_threadId, WmApply, IntPtr.Zero, IntPtr.Zero);
            }
        }

        private void Run()
        {
            // The first PeekMessage gives the thread its message queue, so posts from now on are not lost.
            PeekMessage(out WindowMessage message, IntPtr.Zero, 0, 0, 0);
            _threadId = GetCurrentThreadId();
            _started.Set();
            var refused = new List<KeyChord>();
            Apply(refused);
            try
            {
                while (GetMessage(out message, IntPtr.Zero, 0, 0) > 0)
                {
                    if (message.Message == WmHotKey && _registered.TryGetValue(message.WParam.ToInt32(), out KeyChord chord))
                    {
                        Raise(chord);
                    }
                    else if (message.Message == WmApply)
                    {
                        Apply(refused);
                    }
                }
            }
            finally
            {
                foreach (int id in _registered.Keys)
                {
                    UnregisterHotKey(IntPtr.Zero, id);
                }

                _registered.Clear();
                Volatile.Write(ref _heldCount, 0);
            }
        }

        private void Apply(List<KeyChord> refused)
        {
            IReadOnlyCollection<KeyChord> wanted;
            bool paused;
            lock (_gate)
            {
                wanted = _wanted;
                paused = _paused;
            }

            (IReadOnlyList<int> release, IReadOnlyList<KeyChord> take) = Changes(_registered, wanted, paused);
            foreach (int id in release)
            {
                UnregisterHotKey(IntPtr.Zero, id);
                _registered.Remove(id);
            }

            // Chords another app held are tried again each time, as it may have let them go.
            refused.Clear();
            foreach (KeyChord chord in take)
            {
                int id = _nextId++;
                if (RegisterHotKey(IntPtr.Zero, id, (uint)chord.Modifiers | ModNoRepeat, (uint)chord.VirtualKey))
                {
                    _registered[id] = chord;
                }
                else
                {
                    int error = Marshal.GetLastWin32Error();
                    refused.Add(chord);
                    if (error != ErrorHotKeyAlreadyRegistered)
                    {
                        _log?.Warning($"Windows did not take the shortcut {chord} (error {error}).");
                    }
                }
            }

            Volatile.Write(ref _heldCount, _registered.Count);
            KeyChord[] before = _refused;
            if (!before.SequenceEqual(refused))
            {
                _refused = refused.ToArray();
                if (refused.Count > 0)
                {
                    _log?.Information($"{refused.Count} shortcut(s) are held by another app: {string.Join(", ", refused)}.");
                }

                RaiseRefusedChanged();
            }
        }

        // A listener's failure must not end the thread, or every shortcut would stop.
        private void Raise(KeyChord chord)
        {
            try
            {
                Pressed?.Invoke(chord);
            }
            catch (Exception exception)
            {
                _log?.Warning($"The shortcut {chord} failed.", exception);
            }
        }

        private void RaiseRefusedChanged()
        {
            try
            {
                RefusedChanged?.Invoke();
            }
            catch (Exception exception)
            {
                _log?.Warning("Telling of the shortcuts held by another app failed.", exception);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowMessage
        {
            public IntPtr Window;
            public int Message;
            public IntPtr WParam;
            public IntPtr LParam;
            public uint Time;
            public int PointX;
            public int PointY;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr window, int id);

        [DllImport("user32.dll", EntryPoint = "GetMessageW")]
        private static extern int GetMessage(out WindowMessage message, IntPtr window, uint filterMin, uint filterMax);

        [DllImport("user32.dll", EntryPoint = "PeekMessageW")]
        private static extern bool PeekMessage(out WindowMessage message, IntPtr window, uint filterMin, uint filterMax, uint remove);

        [DllImport("user32.dll", EntryPoint = "PostThreadMessageW", SetLastError = true)]
        private static extern bool PostThreadMessage(uint threadId, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();
    }
}
