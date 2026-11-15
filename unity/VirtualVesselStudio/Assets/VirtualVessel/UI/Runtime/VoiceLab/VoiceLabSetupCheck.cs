using System;
using System.Threading;
using UnityEngine.UIElements;
using VirtualVessel.ProjectData.Records;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Dialogs;
using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.VoiceLab
{
    /// <summary>
    /// The check of the PC before preparing (screen spec voice-lab 2.1): the GPU, its driver, the folder to build in with
    /// its free space, and the connection, each with OK or what to do; the folder can be changed. The terms open once
    /// all are met.
    /// </summary>
    internal sealed class VoiceLabSetupCheck
    {
        private readonly IVoiceLabSetup _setup;
        private readonly IFileDialog _files;
        private readonly Label _gpuValue;
        private readonly Label _driverValue;
        private readonly Label _locationValue;
        private readonly Label _onlineValue;
        private readonly StatusChip[] _chips = new StatusChip[4];
        private readonly Label[] _problems = new Label[4];
        private readonly KeyedText[] _problemTexts = new KeyedText[4];
        private readonly StatusChip _locationFailed;
        private CancellationTokenSource _checking;

        /// <param name="files">Picks the folder to build in; null keeps the default.</param>
        /// <param name="toTerms">Opens the terms, once the PC meets every condition.</param>
        public VoiceLabSetupCheck(IVoiceLabSetup setup, IFileDialog files, Action toTerms)
        {
            _setup = setup;
            _files = files;
            Card = Ui.Card(VoiceLabStrings.SetupTitle);
            Card.Add(VoiceLabScreen.Lead(VoiceLabStrings.SetupLead));
            _gpuValue = Row(0, VoiceLabStrings.SetupGpu, VoiceLabStrings.SetupGpuHelp, null);
            _driverValue = Row(1, VoiceLabStrings.SetupDriver, VoiceLabStrings.SetupDriverHelp, null);
            ChangeButton = Ui.Button(VoiceLabStrings.SetupChange);
            ChangeButton.clicked += ChangeLocation;
            ChangeButton.SetEnabled(files != null);
            _locationValue = Row(2, VoiceLabStrings.SetupLocation, VoiceLabStrings.SetupLocationHelp, ChangeButton);
            _locationFailed = Ui.Chip(StatusKind.Error, VoiceLabStrings.SetupLocationFailed);
            _locationFailed.AddToClassList(VoiceLabScreen.GapClass);
            _locationFailed.style.display = DisplayStyle.None;
            Card.Add(_locationFailed);
            _onlineValue = Row(3, VoiceLabStrings.SetupOnline, VoiceLabStrings.SetupOnlineHelp, null);
            ToTermsButton = Ui.Button(VoiceLabStrings.SetupToTerms, ButtonVariant.Primary);
            ToTermsButton.clicked += toTerms;
            ToTermsButton.SetEnabled(false);
            Card.Add(VoiceLabScreen.Actions(ToTermsButton));
            Card.Add(Ui.Text(VoiceLabStrings.SetupAgreeNote, VoiceLabScreen.NoteClass, "vv-text-body"));
            Card.Add(Ui.Text(VoiceLabStrings.SetupTimeNote, VoiceLabScreen.NoteClass, "vv-text-body"));
        }

        public Card Card { get; }

        public ActionButton ChangeButton { get; }

        public ActionButton ToTermsButton { get; }

        /// <summary>The last check of the PC; null until one completed.</summary>
        public VoiceLabPcCheck LastCheck { get; private set; }

        public bool Checking => _checking != null;

        /// <summary>Looks at the PC again; a check still running is replaced.</summary>
        public async void Start()
        {
            _checking?.Cancel();
            var checking = _checking = new CancellationTokenSource();
            ToTermsButton.SetEnabled(false);
            foreach (StatusChip chip in _chips)
            {
                chip.Status = StatusKind.Idle;
                chip.TextKey = VoiceLabStrings.SetupChecking;
            }

            Show(null);
            VoiceLabPcCheck check;
            try
            {
                check = await _setup.CheckPcAsync(checking.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            finally
            {
                if (_checking == checking)
                {
                    _checking = null;
                }

                checking.Dispose();
            }

            // A newer check, such as for another folder, replaced this one.
            if (checking.IsCancellationRequested)
            {
                return;
            }

            LastCheck = check;
            Show(check);
        }

        public void Cancel()
        {
            _checking?.Cancel();
            _checking = null;
        }

        /// <summary>Writes the values again, such as in another language.</summary>
        public void Relabel()
        {
            Show(LastCheck);
        }

        private Label Row(int index, string nameKey, string helpKey, VisualElement control)
        {
            SettingRow row = Ui.SettingRow(nameKey);
            row.HelpKey = helpKey;
            Label value = VoiceLabSetupView.TextLabel(VoiceLabScreen.NoteClass);
            row.TextArea.Add(value);
            var controls = new VisualElement();
            controls.AddToClassList(VoiceLabSetupView.ControlsClass);
            _chips[index] = Ui.Chip(StatusKind.Idle, VoiceLabStrings.SetupChecking);
            controls.Add(_chips[index]);
            if (control != null)
            {
                controls.Add(control);
            }

            row.Add(controls);
            Card.Add(row);
            Label problem = _problems[index] = VoiceLabSetupView.TextLabel(VoiceLabSetupView.ProblemClass);
            _problemTexts[index] = new KeyedText(problem, text => problem.text = text);
            problem.style.display = DisplayStyle.None;
            Card.Add(problem);
            return value;
        }

        private void Show(VoiceLabPcCheck check)
        {
            ILocalizer localizer = UiServices.Find(Card)?.Localizer;
            string unknown = localizer?.Get(VoiceLabStrings.SetupUnknown) ?? string.Empty;
            _gpuValue.text = check == null ? string.Empty : check.Gpu ?? unknown;
            _driverValue.text = check == null ? string.Empty : check.Driver ?? unknown;
            _locationValue.text = VoiceLabSetupText.LocationText(localizer, check?.Location ?? _setup.Location, check?.FreeBytes);
            _onlineValue.text = check == null ? string.Empty : localizer?.Get(check.Online ? VoiceLabStrings.SetupOnlineOk : VoiceLabStrings.SetupOnlineNo) ?? string.Empty;
            if (check == null)
            {
                foreach (Label problem in _problems)
                {
                    problem.style.display = DisplayStyle.None;
                }

                return;
            }

            ShowRow(0, check.GpuOk, check.Gpu == null ? VoiceLabStrings.SetupGpuMissing : VoiceLabStrings.SetupGpuOld);
            ShowRow(1, check.DriverOk, VoiceLabStrings.SetupDriverOld);
            ShowRow(2, check.SpaceOk, VoiceLabStrings.SetupLocationLow);
            ShowRow(3, check.Online, VoiceLabStrings.SetupOnlineProblem);
            ToTermsButton.SetEnabled(check.AllOk);
        }

        private void ShowRow(int index, bool ok, string problemKey)
        {
            _chips[index].Status = ok ? StatusKind.Success : StatusKind.Error;
            _chips[index].TextKey = ok ? VoiceLabStrings.SetupCheckOk : VoiceLabStrings.SetupCheckNg;
            _problemTexts[index].Key = problemKey;
            _problems[index].style.display = VoiceLabSetupView.Show(!ok);
        }

        private async void ChangeLocation()
        {
            if (_files == null)
            {
                return;
            }

            string picked = await _files.PickFolderAsync(UiServices.Find(Card)?.Localizer?.Get(VoiceLabStrings.SetupLocationPickTitle));
            if (string.IsNullOrEmpty(picked))
            {
                return;
            }

            CrudResult set = _setup.SetLocation(picked);
            _locationFailed.style.display = DisplayStyle.None;
            if (!set.Ok)
            {
                VoiceLabScreen.ShowFailure(Card, _locationFailed, "Choosing where to build Voice Lab", set.Error, set.Detail);
                _locationFailed.TextKey = VoiceLabStrings.SetupLocationFailed;
            }

            Start();
        }
    }
}
