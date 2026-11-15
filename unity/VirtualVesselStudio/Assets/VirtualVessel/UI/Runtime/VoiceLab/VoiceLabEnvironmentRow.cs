using System;
using System.Threading;
using UnityEngine.UIElements;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.ProjectData.Records;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Dialogs;

namespace VirtualVessel.UI.VoiceLab
{
    /// <summary>
    /// The training environment's row in the Made voices tab's stored data (screen spec 8; Voice Lab detailed design 11):
    /// its size, and Delete when the app built it. One set up elsewhere, such as a developer's, shows why it is not
    /// deleted here (the user's request, 2026-11-11).
    /// </summary>
    public sealed class VoiceLabEnvironmentRow : VisualElement
    {
        private readonly IVoiceLab _lab;
        private readonly Label _size;
        private readonly StatusChip _failed;
        private CancellationTokenSource _measuring;

        public VoiceLabEnvironmentRow(IVoiceLab lab)
        {
            _lab = lab ?? throw new ArgumentNullException(nameof(lab));
            Row = Ui.SettingRow(VoiceLabStrings.StorageEnvironment);
            Row.HelpKey = VoiceLabStrings.StorageEnvironmentHelp;
            _size = QuietText.Of(new Label());
            _size.AddToClassList(VoiceLabScreen.NoteClass);
            _size.AddToClassList("vv-text-body");
            Row.TextArea.Add(_size);
            DeleteButton = Ui.Button(VoiceLabStrings.Delete);
            DeleteButton.clicked += Delete;
            Row.Add(DeleteButton);
            Add(Row);
            _failed = Ui.Chip(StatusKind.Error, VoiceLabStrings.CrudStorageFailed);
            _failed.style.display = DisplayStyle.None;
            Add(_failed);
            RegisterCallback<AttachToPanelEvent>(_ => Show());
            RegisterCallback<DetachFromPanelEvent>(_ => _measuring?.Cancel());
        }

        public SettingRow Row { get; }

        public ActionButton DeleteButton { get; }

        /// <summary>Shows the state and measures the size again, such as when the tab opens.</summary>
        public async void Show()
        {
            VoiceLabEnvironmentState state = _lab.EnvironmentState;
            style.display = state == VoiceLabEnvironmentState.None ? DisplayStyle.None : DisplayStyle.Flex;
            bool appBuilt = state == VoiceLabEnvironmentState.AppBuilt;
            DeleteButton.SetEnabled(false);
            DeleteButton.TooltipKey = appBuilt ? null : VoiceLabStrings.StorageEnvironmentExternal;
            if (!appBuilt)
            {
                _size.text = string.Empty;
                return;
            }

            _measuring?.Cancel();
            var measuring = _measuring = new CancellationTokenSource();
            _size.text = UiServices.Find(this)?.Localizer?.Get(VoiceLabStrings.StorageMeasuring) ?? string.Empty;
            try
            {
                long bytes = await _lab.EnvironmentSizeAsync(measuring.Token);
                _size.text = VoiceLabLibraryView.SizeText(bytes);
                DeleteButton.SetEnabled(bytes > 0);
            }
            catch (OperationCanceledException)
            {
            }
            catch (System.IO.IOException exception)
            {
                _size.text = string.Empty;
                UiServices.Find(this)?.Log?.Warning($"Measuring the training environment failed: {exception.Message}");
            }
        }

        private async void Delete()
        {
            var dialogs = UiServices.Find(this)?.Dialogs;
            if (dialogs != null && !await dialogs.ConfirmAsync(VoiceLabStrings.DeleteEnvironmentTitle, VoiceLabStrings.DeleteEnvironmentBody, VoiceLabStrings.Delete, ConfirmStyle.Danger))
            {
                return;
            }

            _failed.style.display = DisplayStyle.None;
            DeleteButton.SetEnabled(false);
            CrudResult removed = await _lab.RemoveEnvironmentAsync();
            if (!removed.Ok)
            {
                VoiceLabScreen.ShowFailure(this, _failed, "Deleting the training environment", removed.Error, removed.Detail);
            }

            Show();
        }
    }
}
