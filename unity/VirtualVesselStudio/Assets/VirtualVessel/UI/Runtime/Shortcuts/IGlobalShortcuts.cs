using System;
using System.Collections.Generic;

namespace VirtualVessel.UI.Shortcuts
{
    /// <summary>
    /// Keys that work whichever app is in front, so a sound effect plays while the user is in a game (BGM and SE
    /// detailed design 8.3; system design 14.27). Windows tells only the app that holds a chord when it is pressed;
    /// nothing else is read from the keyboard.
    /// </summary>
    public interface IGlobalShortcuts : IDisposable
    {
        /// <summary>Raised on the listening thread, not the main thread, when a chord listened to is pressed.</summary>
        event Action<KeyChord> Pressed;

        /// <summary>Raised on the listening thread after <see cref="Refused"/> changed.</summary>
        event Action RefusedChanged;

        /// <summary>The chords listened to but held by another app, so they do nothing here. Any thread.</summary>
        IReadOnlyCollection<KeyChord> Refused { get; }

        /// <summary>
        /// While true nothing is listened to, so a chord the user presses to assign it reaches this app's window and
        /// is not played.
        /// </summary>
        bool Paused { get; set; }

        /// <summary>Replaces the chords listened to. Any thread; applied shortly after on the listening thread.</summary>
        void Listen(IReadOnlyCollection<KeyChord> chords);
    }
}
