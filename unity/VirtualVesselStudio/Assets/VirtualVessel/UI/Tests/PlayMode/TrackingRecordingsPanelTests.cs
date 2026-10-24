using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using VirtualVessel.Core.State;
using VirtualVessel.Core.Time;
using VirtualVessel.Tracking;
using VirtualVessel.Tracking.Recording;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Screens;
using Object = UnityEngine.Object;

namespace VirtualVessel.UI.Tests
{
    /// <summary>The tracking recordings Card on the Diagnostics screen (UI foundation detailed design 7.7).</summary>
    public sealed class TrackingRecordingsPanelTests
    {
        private string _directory;
        private GameObject _host;
        private UiRoot _root;
        private FakeRecordings _recordings;
        private LiveSetupPanelTests.FakeTracking _tracking;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "VirtualVesselTests", Guid.NewGuid().ToString("N"));
            _recordings = new FakeRecordings();
            _tracking = new LiveSetupPanelTests.FakeTracking();
            _host = new GameObject(nameof(TrackingRecordingsPanelTests));
            _host.AddComponent<UIDocument>();
            _root = _host.AddComponent<UiRoot>();
            _root.SetAssets(UnityEditor.AssetDatabase.LoadAssetAtPath<UiAssets>("Assets/VirtualVessel/UI/UiAssets.asset"));
            _root.Build(new UiRootContext(_directory, new StopwatchMonotonicClock(), new UtcSystemClock(), null, null, false, screens: new ScreenServices { Tracking = _tracking, TrackingRecordings = _recordings }));
            _root.Shell.DeveloperMode = true;
            _root.Shell.Open("diagnostics");
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(_host);
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        [UnityTest]
        public IEnumerator Recording_NeedsTracking_AndShowsItsStateWithIconAndText()
        {
            TrackingRecordingsCard card = _root.Content.Q<TrackingRecordingsCard>();
            Assert.That(card, Is.Not.Null, "Placed on the Diagnostics screen.");
            yield return null;
            Assert.That(_recordings.Refreshed, Is.GreaterThan(0), "The list is read when shown.");
            Assert.That(card.RecordButton.enabledSelf, Is.False, "Tracking is not running.");
            Assert.That(card.RecordButton.TooltipKey, Is.EqualTo(RecordingStrings.StartNeedsTracking), "The reason is given.");
            Assert.That(card.State.Status, Is.EqualTo(StatusKind.Idle));

            _tracking.Start();
            Assert.That(card.RecordButton.enabledSelf, Is.True);
            card.DescriptionField.value = "arms forward";
            Submit(card.RecordButton);
            yield return null;

            Assert.That(_recordings.StartedWith, Is.EqualTo("arms forward"));
            Assert.That(card.State.Status, Is.EqualTo(StatusKind.Live));
            Assert.That(card.State.TextKey, Is.EqualTo(RecordingStrings.StateRecording), "Not by color alone: the chip has an icon and text.");
            Assert.That(card.RecordButton.TextKey, Is.EqualTo(RecordingStrings.Stop));
            Assert.That(card.DescriptionField.enabledSelf, Is.False, "The description is fixed while recording.");
            _recordings.Seconds = 65;
            yield return new WaitForSeconds(1.1f);
            Assert.That(card.State.Number, Is.EqualTo("1:05"), "The elapsed time follows.");

            Submit(card.RecordButton);
            yield return null;
            Assert.That(_recordings.Activity.Value, Is.EqualTo(RecordingActivity.Idle));
            Assert.That(card.State.Number, Is.Empty.Or.Null);
        }

        [UnityTest]
        public IEnumerator TheList_ShowsEachRecording_AndOnlyReadableOnesReplay()
        {
            TrackingRecordingsCard card = _root.Content.Q<TrackingRecordingsCard>();
            _recordings.Set(
                new TrackingRecordingInfo("20261022-050000.vvtrack", "<b>arms</b>", new DateTime(2026, 10, 22, 5, 0, 0, DateTimeKind.Utc), 65, 3 * 1024 * 1024, 1950, readable: true),
                TrackingRecordingInfo.Unreadable("20261022-040000.vvtrack", 2048));
            yield return null;

            Assert.That(card.Table.RowCount, Is.EqualTo(2));
            Assert.That(card.Table.GetCell(0, 0).text, Is.EqualTo("<b>arms</b>"));
            Assert.That(card.Table.GetCell(0, 0).enableRichText, Is.False, "A description is data, never rich text.");
            Assert.That(card.Table.GetCell(0, 2).text, Is.EqualTo("1:05"));
            Assert.That(card.Table.GetCell(0, 3).text, Is.EqualTo("3.0 MB"));
            Assert.That(card.Table.GetCell(1, 0).text, Does.Contain("20261022-040000.vvtrack"), "An unreadable file is listed by name.");
            Assert.That(card.TotalText, Does.Contain("2").And.Contain("3.0 MB"));

            Assert.That(card.Choice.value, Is.EqualTo("20261022-050000.vvtrack"), "The newest is chosen first.");
            Assert.That(card.ReplayButton.enabledSelf, Is.True);
            Submit(card.ReplayButton);
            yield return null;
            Assert.That(_recordings.ReplayedId, Is.EqualTo("20261022-050000.vvtrack"));
            Assert.That(card.State.TextKey, Is.EqualTo(RecordingStrings.StateReplaying));
            Assert.That(card.ReplayButton.TextKey, Is.EqualTo(RecordingStrings.StopReplay));
            Assert.That(card.DeleteButton.enabledSelf, Is.False, "The recording being replayed cannot be deleted.");
            _tracking.Start();
            Assert.That(card.RecordButton.enabledSelf, Is.False, "No recording during replay.");
            Assert.That(card.RecordButton.TooltipKey, Is.EqualTo(RecordingStrings.StartDuringReplay));

            Submit(card.ReplayButton);
            yield return null;
            card.Choice.value = "20261022-040000.vvtrack";
            Assert.That(card.ReplayButton.enabledSelf, Is.False, "An unreadable recording cannot be replayed.");
            Assert.That(card.DeleteButton.enabledSelf, Is.True, "But it can be deleted.");
        }

        [Test]
        public void Lengths_AndSizes_ReadEasily()
        {
            Assert.That(TrackingRecordingsCard.Length(0), Is.EqualTo("0:00"));
            Assert.That(TrackingRecordingsCard.Length(299.9), Is.EqualTo("4:59"));
            Assert.That(TrackingRecordingsCard.Size(640 * 1024), Is.EqualTo("640 KB"));
            Assert.That(TrackingRecordingsCard.Size(15_200_000), Is.EqualTo("14.5 MB"));

            var info = new TrackingRecordingInfo("20261022-050000.vvtrack", string.Empty, new DateTime(2026, 10, 22, 5, 0, 0, DateTimeKind.Utc), 3, 1024, 90, readable: true);
            TimeZoneInfo tokyo = TimeZoneInfo.CreateCustomTimeZone("test", TimeSpan.FromHours(9), "test", "test");
            Assert.That(TrackingRecordingsCard.Row(info, tokyo, "unreadable", "(none)"), Is.EqualTo(new[] { "(none)", "2026-10-22 14:00:00", "0:03", "1 KB" }), "Dates in the user's local time.");
        }

        private static void Submit(VisualElement element)
        {
            using (NavigationSubmitEvent evt = NavigationSubmitEvent.GetPooled())
            {
                evt.target = element;
                element.SendEvent(evt);
            }
        }

        private sealed class FakeRecordings : ITrackingRecordings
        {
            private readonly ObservableValue<RecordingActivity> _activity = new ObservableValue<RecordingActivity>(RecordingActivity.Idle);
            private readonly ObservableValue<IReadOnlyList<TrackingRecordingInfo>> _list = new ObservableValue<IReadOnlyList<TrackingRecordingInfo>>(Array.Empty<TrackingRecordingInfo>());

            public IReadOnlyObservable<RecordingActivity> Activity => _activity;

            public IReadOnlyObservable<IReadOnlyList<TrackingRecordingInfo>> Recordings => _list;

            public string ActiveId { get; private set; }

            public double RecordedSeconds => Seconds;

            public double Seconds { get; set; }

            public int Refreshed { get; private set; }

            public string StartedWith { get; private set; }

            public string ReplayedId { get; private set; }

            public void Set(params TrackingRecordingInfo[] list)
            {
                _list.Value = list;
            }

            public Task RefreshAsync(CancellationToken cancellationToken)
            {
                Refreshed++;
                return Task.CompletedTask;
            }

            public void StartRecording(string description)
            {
                StartedWith = description;
                ActiveId = "20261022-060000.vvtrack";
                _activity.Value = RecordingActivity.Recording;
            }

            public void StopRecording()
            {
                ActiveId = null;
                _activity.Value = RecordingActivity.Idle;
            }

            public Task StartReplayAsync(string id, CancellationToken cancellationToken)
            {
                ReplayedId = id;
                ActiveId = id;
                _activity.Value = RecordingActivity.Replaying;
                return Task.CompletedTask;
            }

            public void StopReplay()
            {
                ActiveId = null;
                _activity.Value = RecordingActivity.Idle;
            }

            public Task DeleteAsync(string id, CancellationToken cancellationToken)
            {
                _list.Value = _list.Value.Where(info => info.Id != id).ToArray();
                return Task.CompletedTask;
            }

            public Task DeleteAllAsync(CancellationToken cancellationToken)
            {
                _list.Value = Array.Empty<TrackingRecordingInfo>();
                return Task.CompletedTask;
            }

            public void StopAll()
            {
                StopRecording();
                StopReplay();
            }
        }
    }
}
