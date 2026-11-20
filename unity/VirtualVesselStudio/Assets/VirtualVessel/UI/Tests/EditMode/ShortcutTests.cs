using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using VirtualVessel.Audio.Clips;
using VirtualVessel.Audio.Playback;
using VirtualVessel.ProjectData.Records;
using VirtualVessel.UI.Shortcuts;

namespace VirtualVessel.UI.Tests
{
    public sealed class ShortcutTests
    {
        [Test]
        public void AKeyPress_MakesAChord_OnlyWithAModifier()
        {
            Assert.That(KeyChord.FromKey(KeyCode.Alpha1, true, true, false)?.ToString(), Is.EqualTo("Ctrl+Alt+1"));
            Assert.That(KeyChord.FromKey(KeyCode.F5, false, false, true)?.ToString(), Is.EqualTo("Shift+F5"));
            Assert.That(KeyChord.FromKey(KeyCode.Keypad3, true, false, false)?.ToString(), Is.EqualTo("Ctrl+Num3"));
            Assert.That(KeyChord.FromKey(KeyCode.Q, false, false, false), Is.Null, "A game's own key stays the game's.");
            Assert.That(KeyChord.FromKey(KeyCode.LeftControl, true, false, false), Is.Null, "A modifier alone.");
            Assert.That(KeyChord.FromKey(KeyCode.LeftBracket, true, false, false), Is.Null, "Punctuation moves with the keyboard layout.");
        }

        [Test]
        public void AChord_IsWrittenAndReadTheSameWay()
        {
            KeyChord chord = KeyChord.FromKey(KeyCode.A, true, true, true).Value;
            Assert.That(chord.ToString(), Is.EqualTo("Ctrl+Alt+Shift+A"));
            Assert.That(KeyChord.Parse("Ctrl+Alt+Shift+A"), Is.EqualTo(chord));
            Assert.That(KeyChord.Parse(" shift + alt + ctrl + a "), Is.EqualTo(chord), "Case, spaces, and order do not count.");
            Assert.That(chord.VirtualKey, Is.EqualTo(0x41));
            Assert.That(chord.Modifiers, Is.EqualTo(KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Shift));
            Assert.That(KeyChord.Parse("Ctrl+F12")?.VirtualKey, Is.EqualTo(0x7B));
            Assert.That(KeyChord.Parse("Ctrl+Num0")?.VirtualKey, Is.EqualTo(0x60));
            Assert.That(KeyChord.Parse("A"), Is.Null, "No modifier.");
            Assert.That(KeyChord.Parse("Ctrl+Ctrl+A"), Is.Null);
            Assert.That(KeyChord.Parse("Win+A"), Is.Null);
            Assert.That(KeyChord.Parse("Ctrl+Space"), Is.Null);
            Assert.That(KeyChord.Parse(null), Is.Null);
        }

        [Test]
        public void Changes_LetGoAndTake_OnlyWhatDiffers()
        {
            KeyChord one = KeyChord.Parse("Ctrl+Alt+1").Value;
            KeyChord two = KeyChord.Parse("Ctrl+Alt+2").Value;
            KeyChord three = KeyChord.Parse("Ctrl+Alt+3").Value;
            var held = new Dictionary<int, KeyChord> { [7] = one, [8] = two };

            (IReadOnlyList<int> release, IReadOnlyList<KeyChord> take) = WindowsGlobalShortcuts.Changes(held, new[] { two, three }, false);
            Assert.That(release, Is.EqualTo(new[] { 7 }));
            Assert.That(take, Is.EqualTo(new[] { three }));

            (release, take) = WindowsGlobalShortcuts.Changes(held, new[] { two, three }, true);
            Assert.That(release, Is.EquivalentTo(new[] { 7, 8 }), "Paused: all let go.");
            Assert.That(take, Is.Empty);
        }

        // Windows itself: a chord one listener holds is refused to another, and freed when the first pauses.
        [Test]
        public void AChordHeldElsewhere_IsRefused_UntilItIsLetGo()
        {
            KeyChord chord = KeyChord.Parse("Ctrl+Alt+Shift+F11").Value;
            using (var first = new WindowsGlobalShortcuts(null))
            using (var second = new WindowsGlobalShortcuts(null))
            {
                first.Listen(new[] { chord });
                WaitFor(() => first.HeldCount == 1 || first.Refused.Count > 0);
                if (first.HeldCount == 0)
                {
                    Assert.Ignore("Another app on this PC holds Ctrl+Alt+Shift+F11.");
                }

                int changes = 0;
                second.RefusedChanged += () => Interlocked.Increment(ref changes);
                second.Listen(new[] { chord });
                Assert.That(WaitFor(() => second.Refused.Contains(chord)), Is.True, "Held by the first.");
                Assert.That(Volatile.Read(ref changes), Is.GreaterThan(0));

                first.Paused = true;
                Assert.That(WaitFor(() => first.HeldCount == 0), Is.True, "Paused: let go.");
                second.Listen(new[] { chord });
                Assert.That(WaitFor(() => second.Refused.Count == 0), Is.True, "Tried again once it was let go.");
            }
        }

        [Test]
        public void Bindings_TakeTheFirstOfTwoWithTheSameKeys()
        {
            var effects = new[]
            {
                Effect("se-a", "Ctrl+Alt+1"),
                Effect("se-b", "ctrl+alt+1"),
                Effect("se-c", "Ctrl+Alt+2"),
                Effect("se-d", null),
                Effect("se-e", "not keys"),
            };

            IReadOnlyDictionary<KeyChord, string> bindings = SoundEffectShortcuts.Bindings(effects);

            Assert.That(bindings.Count, Is.EqualTo(2));
            Assert.That(bindings[KeyChord.Parse("Ctrl+Alt+1").Value], Is.EqualTo("se-a"));
            Assert.That(bindings[KeyChord.Parse("Ctrl+Alt+2").Value], Is.EqualTo("se-c"));
        }

        [Test]
        public void APress_PlaysItsSoundEffect_AndTheKeysFollowTheLibrary()
        {
            var library = new FakeLibrary(Effect("se-a", "Ctrl+Alt+1"));
            var player = new FakePlayer();
            var keys = new FakeKeys();
            var shortcuts = new SoundEffectShortcuts(library, player, keys, null);
            shortcuts.InitializeAsync(CancellationToken.None).Wait();

            Assert.That(keys.Listened.Select(chord => chord.ToString()), Is.EqualTo(new[] { "Ctrl+Alt+1" }));
            keys.Press("Ctrl+Alt+1");
            keys.Press("Ctrl+Alt+9");
            Assert.That(player.Played, Is.EqualTo(new[] { "se-a" }), "Only a chord with a sound effect plays.");

            library.Effects = new[] { Effect("se-a", null), Effect("se-b", "Ctrl+Alt+2") };
            library.Raise(AudioClipKind.SoundEffect);
            Assert.That(keys.Listened.Select(chord => chord.ToString()), Is.EqualTo(new[] { "Ctrl+Alt+2" }));
            keys.Press("Ctrl+Alt+1");
            keys.Press("Ctrl+Alt+2");
            Assert.That(player.Played, Is.EqualTo(new[] { "se-a", "se-b" }));

            shortcuts.ShutdownAsync(CancellationToken.None).Wait();
            Assert.That(keys.Listened, Is.Empty, "Nothing is held after shutdown.");
            keys.Press("Ctrl+Alt+2");
            Assert.That(player.Played.Count, Is.EqualTo(2));
            shortcuts.Dispose();
            Assert.That(keys.Disposed, Is.True);
        }

        private static bool WaitFor(Func<bool> condition)
        {
            DateTime until = DateTime.UtcNow.AddSeconds(3);
            while (!condition() && DateTime.UtcNow < until)
            {
                Thread.Sleep(10);
            }

            return condition();
        }

        private static AudioClipRecord Effect(string id, string shortcut)
        {
            return new AudioClipRecord(id, AudioClipKind.SoundEffect, id, new AudioClipSettings(0f, false, shortcut), new AudioClipFile(id, id, TimeSpan.FromSeconds(1), DateTimeOffset.UnixEpoch));
        }

        private sealed class FakeKeys : IGlobalShortcuts
        {
            public event Action<KeyChord> Pressed;

            public event Action RefusedChanged
            {
                add
                {
                }

                remove
                {
                }
            }

            public IReadOnlyCollection<KeyChord> Refused => Array.Empty<KeyChord>();

            public bool Paused { get; set; }

            public IReadOnlyCollection<KeyChord> Listened { get; private set; } = Array.Empty<KeyChord>();

            public bool Disposed { get; private set; }

            public void Listen(IReadOnlyCollection<KeyChord> chords)
            {
                Listened = chords.ToArray();
            }

            public void Press(string chord)
            {
                Pressed?.Invoke(KeyChord.Parse(chord).Value);
            }

            public void Dispose()
            {
                Disposed = true;
            }
        }

        private sealed class FakePlayer : ISoundEffectPlayer
        {
            public List<string> Played { get; } = new List<string>();

            public void Play(string id)
            {
                Played.Add(id);
            }

            public void StopAll()
            {
            }

            public bool IsReady(string id)
            {
                return true;
            }
        }

        private sealed class FakeLibrary : IAudioClipLibrary
        {
            public FakeLibrary(params AudioClipRecord[] effects)
            {
                Effects = effects;
            }

            public event Action<AudioClipKind> Changed;

            public IReadOnlyList<AudioClipRecord> Effects { get; set; }

            public IReadOnlyList<string> FileExtensions => new[] { ".wav" };

            public void Raise(AudioClipKind kind)
            {
                Changed?.Invoke(kind);
            }

            public CrudResult<IReadOnlyList<AudioClipRecord>> List(AudioClipKind kind)
            {
                return CrudResult<IReadOnlyList<AudioClipRecord>>.Success(kind == AudioClipKind.SoundEffect ? Effects : Array.Empty<AudioClipRecord>());
            }

            public CrudResult<AudioClipRecord> Get(string id)
            {
                throw new NotSupportedException();
            }

            public Task<AudioClipProblem> CheckAsync(AudioClipKind kind, string sourcePath, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task<CrudResult<AudioClipRecord>> AddAsync(AudioClipKind kind, string sourcePath, string name, IReadOnlyDictionary<string, string> names, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public CrudResult<AudioClipRecord> Update(string id, AudioClipChange change)
            {
                throw new NotSupportedException();
            }

            public CrudResult Delete(string id)
            {
                throw new NotSupportedException();
            }

            public CrudResult Move(string id, int position)
            {
                throw new NotSupportedException();
            }
        }
    }
}
