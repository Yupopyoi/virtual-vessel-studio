using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using VirtualVessel.Avatar;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.UI.Binding;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Dialogs;
using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.Screens
{
    /// <summary>String table keys of the avatar Card (docs/&lt;lang&gt;/ui/screens/setup.md 5).</summary>
    [StringKeys]
    public static class AvatarStrings
    {
        public const string Title = "setup.avatar.title";
        public const string Unavailable = "setup.avatar.unavailable";
        public const string Use = "setup.avatar.use";
        public const string None = "setup.avatar.none";
        public const string Unusable = "setup.avatar.unusable";
        public const string NoThumbnail = "setup.avatar.nothumbnail";
        public const string Format = "setup.avatar.format";
        public const string FormatValue = "setup.avatar.format.value";
        public const string FormatConverted = "setup.avatar.format.converted";
        public const string Register = "setup.avatar.register";
        public const string RegisterButton = "setup.avatar.register.button";
        public const string Remove = "setup.avatar.remove";
        public const string RemoveButton = "setup.avatar.remove.button";
        public const string FileFilter = "setup.avatar.filter";
        public const string FileTitle = "setup.avatar.filetitle";
        public const string StateNone = "setup.avatar.state.none";
        public const string StateLoading = "setup.avatar.state.loading";
        public const string StateReady = "setup.avatar.state.ready";
        public const string StateFailed = "setup.avatar.state.failed";
        public const string ProblemNotVrm = "setup.avatar.problem.notvrm";
        public const string ProblemTooLarge = "setup.avatar.problem.toolarge";
        public const string ProblemMissingBones = "setup.avatar.problem.missingbones";
        public const string ProblemLoadFailed = "setup.avatar.problem.loadfailed";
        public const string ProblemAlreadyImported = "setup.avatar.problem.already";
        public const string ProblemStorage = "setup.avatar.problem.storage";
        public const string ConfirmTitle = "setup.avatar.confirm.title";
        public const string ConfirmBody = "setup.avatar.confirm.body";
        public const string ConfirmNotice = "setup.avatar.confirm.notice";
        public const string ConfirmButton = "setup.avatar.confirm.button";
        public const string RemoveTitle = "setup.avatar.remove.title";
        public const string RemoveBody = "setup.avatar.remove.body";
        public const string LicenseTitle = "setup.avatar.license.title";
        public const string LicenseName = "setup.avatar.license.name";
        public const string LicenseAuthors = "setup.avatar.license.authors";
        public const string LicensePermission = "setup.avatar.license.permission";
        public const string LicenseCommercial = "setup.avatar.license.commercial";
        public const string LicenseViolent = "setup.avatar.license.violent";
        public const string LicenseSexual = "setup.avatar.license.sexual";
        public const string LicensePolitical = "setup.avatar.license.political";
        public const string LicenseAntisocial = "setup.avatar.license.antisocial";
        public const string LicenseCredit = "setup.avatar.license.credit";
        public const string LicenseRedistribution = "setup.avatar.license.redistribution";
        public const string LicenseModification = "setup.avatar.license.modification";
        public const string LicenseLegacy = "setup.avatar.license.legacy";
        public const string LicenseUrl = "setup.avatar.license.url";
        public const string LicenseOtherUrl = "setup.avatar.license.otherurl";
        public const string LicenseOpen = "setup.avatar.license.open";
        public const string ValueOnlyAuthor = "setup.avatar.value.onlyauthor";
        public const string ValueLicensedPerson = "setup.avatar.value.licensedperson";
        public const string ValueEveryone = "setup.avatar.value.everyone";
        public const string ValueNonProfit = "setup.avatar.value.nonprofit";
        public const string ValuePersonalProfit = "setup.avatar.value.personalprofit";
        public const string ValueCorporation = "setup.avatar.value.corporation";
        public const string ValueAllowed = "setup.avatar.value.allowed";
        public const string ValueNotAllowed = "setup.avatar.value.notallowed";
        public const string ValueNotStated = "setup.avatar.value.notstated";
        public const string ValueRequired = "setup.avatar.value.required";
        public const string ValueNotRequired = "setup.avatar.value.notrequired";
        public const string ValueModification = "setup.avatar.value.modification";
        public const string ValueModificationRedistribution = "setup.avatar.value.modificationredistribution";
    }

    /// <summary>One line of a model's terms: the name, and either a string table key or text from the model.</summary>
    public readonly struct LicenseLine
    {
        public LicenseLine(string nameKey, string valueKey, string text, string url = null)
        {
            NameKey = nameKey;
            ValueKey = valueKey;
            Text = text;
            Url = url;
        }

        public string NameKey { get; }

        public string ValueKey { get; }

        /// <summary>Written by the model's author; shown as it is.</summary>
        public string Text { get; }

        /// <summary>A link the user may open, when it is one this application opens (https).</summary>
        public string Url { get; }
    }

    /// <summary>
    /// Avatars on the setup screen (docs/&lt;lang&gt;/ui/screens/setup.md 5): which avatar to use,
    /// registering a model file, unregistering, and the model's terms of use set by its author.
    /// </summary>
    public sealed class AvatarCard : VisualElement
    {
        public const string UssClass = "vv-avatar-card";
        public const string ThumbnailClass = UssClass + "__thumbnail";
        public const string LicenseClass = UssClass + "__license";
        public const string LicenseLineClass = UssClass + "__license-line";

        private readonly IAvatarService _avatars;
        private readonly IFileDialog _files;
        private readonly KeyedText _problemText;
        private readonly Label _problem;
        private readonly Image _thumbnail;
        private readonly Label _noThumbnail;
        private readonly Label _format;
        private readonly VisualElement _license;
        private ILocalizer _localizer;
        private UiServices _services;
        private Texture2D _thumbnailTexture;
        private string _shownThumbnail;
        private bool _busy;

        public AvatarCard(IAvatarService avatars, IFileDialog files)
        {
            _avatars = avatars ?? throw new ArgumentNullException(nameof(avatars));
            _files = files;
            AddToClassList(UssClass);
            AddToClassList(CaptureSourceCard.UssClass);
            Card card = Ui.Card(AvatarStrings.Title);
            Add(card);

            Status = Ui.Chip(StatusKind.Idle, AvatarStrings.StateNone);
            card.HeaderEnd.Add(Status);
            _problem = new Label();
            _problem.AddToClassList(CaptureSourceCard.ProblemClass);
            _problem.AddToClassList("vv-text-body");
            _problemText = new KeyedText(_problem, text =>
            {
                _problem.text = text;
                _problem.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
            });
            card.Add(_problem);

            _thumbnail = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            _thumbnail.AddToClassList(ThumbnailClass);
            _noThumbnail = Ui.Text(AvatarStrings.NoThumbnail, CaptureSourceCard.PreviewEmptyClass, "vv-text-caption");
            _thumbnail.Add(_noThumbnail);
            card.Add(_thumbnail);

            Choice = Ui.Select(null);
            Choice.RegisterValueChangedCallback(evt => _ = SelectAsync(string.IsNullOrEmpty(evt.newValue) ? null : evt.newValue));
            card.Add(Row(AvatarStrings.Use, Choice));

            _format = new Label();
            _format.AddToClassList(CaptureSourceCard.InfoClass);
            _format.AddToClassList("vv-text-body");
            card.Add(Row(AvatarStrings.Format, _format));

            RegisterButton = Ui.Button(AvatarStrings.RegisterButton, ButtonVariant.Primary);
            RegisterButton.clicked += () => _ = RegisterAsync();
            RegisterButton.SetEnabled(files != null);
            card.Add(Row(AvatarStrings.Register, RegisterButton));

            RemoveButton = Ui.Button(AvatarStrings.RemoveButton, ButtonVariant.Danger);
            RemoveButton.clicked += () => _ = RemoveAsync();
            card.Add(Row(AvatarStrings.Remove, RemoveButton));

            card.Add(Ui.Text(AvatarStrings.LicenseTitle, "vv-bold", LicenseClass + "-title"));
            _license = new VisualElement();
            _license.AddToClassList(LicenseClass);
            card.Add(_license);

            PanelLifetime.WhileAttached(this, services => Attach(services));
        }

        public StatusChip Status { get; }

        /// <summary>The problem's explanation as shown, for tests.</summary>
        public string ProblemKey => _problemText.Key;

        public SelectField Choice { get; }

        public ActionButton RegisterButton { get; }

        public ActionButton RemoveButton { get; }

        /// <summary>The terms shown for the avatar in use, for tests.</summary>
        public VisualElement License => _license;

        /// <summary>The chip and the explanation for a state: shape, color, and text together (design-system.md 3.4). Pure.</summary>
        public static (StatusKind Kind, string StateKey, string ProblemKey) Describe(AvatarState state, AvatarProblem problem)
        {
            return state switch
            {
                AvatarState.Loading => (StatusKind.Idle, AvatarStrings.StateLoading, null),
                AvatarState.Ready => (StatusKind.Success, AvatarStrings.StateReady, ProblemKeyOf(problem)),
                AvatarState.Error => (StatusKind.Error, AvatarStrings.StateFailed, ProblemKeyOf(problem) ?? AvatarStrings.ProblemLoadFailed),
                _ => (StatusKind.Idle, AvatarStrings.StateNone, ProblemKeyOf(problem)),
            };
        }

        /// <summary>The explanation of a problem, or null for none. Pure.</summary>
        public static string ProblemKeyOf(AvatarProblem problem)
        {
            return problem switch
            {
                AvatarProblem.NotVrm => AvatarStrings.ProblemNotVrm,
                AvatarProblem.TooLarge => AvatarStrings.ProblemTooLarge,
                AvatarProblem.MissingBones => AvatarStrings.ProblemMissingBones,
                AvatarProblem.LoadFailed => AvatarStrings.ProblemLoadFailed,
                AvatarProblem.AlreadyImported => AvatarStrings.ProblemAlreadyImported,
                AvatarProblem.StorageFailed => AvatarStrings.ProblemStorage,
                _ => null,
            };
        }

        /// <summary>The choices of the avatar list: "none" first, then each avatar by name. Pure.</summary>
        public static IReadOnlyList<SelectChoice> Choices(IReadOnlyList<AvatarEntry> avatars)
        {
            var choices = new List<SelectChoice> { SelectChoice.FromKey(string.Empty, AvatarStrings.None) };
            foreach (AvatarEntry entry in avatars)
            {
                choices.Add(entry.IsUsable && !string.IsNullOrWhiteSpace(entry.DisplayName)
                    ? SelectChoice.FromText(entry.Id, entry.DisplayName)
                    : SelectChoice.FromKey(entry.Id, AvatarStrings.Unusable));
            }

            return choices;
        }

        /// <summary>
        /// The lines of a model's terms of use (avatar detailed design 6.4). Conditions the file does not
        /// state say so rather than being left out, so a missing condition is never read as allowed. Pure.
        /// </summary>
        public static IReadOnlyList<LicenseLine> LicenseLines(string modelName, AvatarLicense license)
        {
            license ??= new AvatarLicense();
            var lines = new List<LicenseLine>();
            if (!string.IsNullOrWhiteSpace(modelName))
            {
                lines.Add(new LicenseLine(AvatarStrings.LicenseName, null, modelName));
            }

            lines.Add(license.Authors != null && license.Authors.Count > 0
                ? new LicenseLine(AvatarStrings.LicenseAuthors, null, string.Join(", ", license.Authors))
                : new LicenseLine(AvatarStrings.LicenseAuthors, AvatarStrings.ValueNotStated, null));
            lines.Add(new LicenseLine(AvatarStrings.LicensePermission, license.AvatarPermission switch
            {
                AvatarPermission.Everyone => AvatarStrings.ValueEveryone,
                AvatarPermission.OnlySeparatelyLicensedPerson => AvatarStrings.ValueLicensedPerson,
                AvatarPermission.OnlyAuthor => AvatarStrings.ValueOnlyAuthor,
                _ => AvatarStrings.ValueNotStated,
            }, null));
            lines.Add(new LicenseLine(AvatarStrings.LicenseCommercial, license.CommercialUsage switch
            {
                CommercialUsage.Corporation => AvatarStrings.ValueCorporation,
                CommercialUsage.PersonalProfit => AvatarStrings.ValuePersonalProfit,
                CommercialUsage.PersonalNonProfit => AvatarStrings.ValueNonProfit,
                _ => AvatarStrings.ValueNotStated,
            }, null));
            lines.Add(new LicenseLine(AvatarStrings.LicenseViolent, Allowed(license.AllowExcessivelyViolentUsage), null));
            lines.Add(new LicenseLine(AvatarStrings.LicenseSexual, Allowed(license.AllowExcessivelySexualUsage), null));
            lines.Add(new LicenseLine(AvatarStrings.LicensePolitical, Allowed(license.AllowPoliticalOrReligiousUsage), null));
            lines.Add(new LicenseLine(AvatarStrings.LicenseAntisocial, Allowed(license.AllowAntisocialOrHateUsage), null));
            lines.Add(new LicenseLine(AvatarStrings.LicenseCredit, license.CreditRequired switch
            {
                true => AvatarStrings.ValueRequired,
                false => AvatarStrings.ValueNotRequired,
                null => AvatarStrings.ValueNotStated,
            }, null));
            lines.Add(new LicenseLine(AvatarStrings.LicenseRedistribution, Allowed(license.AllowRedistribution), null));
            lines.Add(new LicenseLine(AvatarStrings.LicenseModification, license.Modification switch
            {
                ModificationPermission.AllowModificationRedistribution => AvatarStrings.ValueModificationRedistribution,
                ModificationPermission.AllowModification => AvatarStrings.ValueModification,
                ModificationPermission.Prohibited => AvatarStrings.ValueNotAllowed,
                _ => AvatarStrings.ValueNotStated,
            }, null));
            if (!string.IsNullOrWhiteSpace(license.LegacyLicenseName))
            {
                lines.Add(new LicenseLine(AvatarStrings.LicenseLegacy, null, license.LegacyLicenseName));
            }

            AddUrl(lines, AvatarStrings.LicenseUrl, license.LicenseUrl);
            AddUrl(lines, AvatarStrings.LicenseOtherUrl, license.OtherLicenseUrl);
            AddUrl(lines, AvatarStrings.LicenseOtherUrl, license.LegacyOtherPermissionUrl);
            return lines;
        }

        /// <summary>
        /// Whether a link from a model file may be opened in the browser: only https, so a model cannot
        /// make the application open a local file or another program (avatar detailed design 13). Pure.
        /// </summary>
        public static bool IsOpenableUrl(string url)
        {
            return Uri.TryCreate(url?.Trim(), UriKind.Absolute, out Uri uri) && uri.Scheme == Uri.UriSchemeHttps;
        }

        /// <summary>"VRM 1.0", or VRM 0.x with a note that it is converted while loading. Pure.</summary>
        public static (string Key, (string Name, object Value)[] Args) FormatText(AvatarProfile profile)
        {
            if (profile == null)
            {
                return (null, Array.Empty<(string, object)>());
            }

            string format = profile.Format?.ToUpperInvariant() ?? string.Empty;
            return (profile.Converted ? AvatarStrings.FormatConverted : AvatarStrings.FormatValue, new (string, object)[] { ("format", format), ("version", profile.FormatVersion ?? string.Empty) });
        }

        private static string Allowed(bool? value)
        {
            return value switch
            {
                true => AvatarStrings.ValueAllowed,
                false => AvatarStrings.ValueNotAllowed,
                null => AvatarStrings.ValueNotStated,
            };
        }

        private static void AddUrl(List<LicenseLine> lines, string nameKey, string url)
        {
            if (!string.IsNullOrWhiteSpace(url))
            {
                lines.Add(new LicenseLine(nameKey, null, url.Trim(), IsOpenableUrl(url) ? url.Trim() : null));
            }
        }

        private IDisposable Attach(UiServices services)
        {
            _services = services;
            _localizer = services?.Localizer;
            Show();

            // The format and the terms are filled in from the model, so they are refreshed by hand.
            Action<LocaleInfo> onLocale = _ => Show();
            if (_localizer != null)
            {
                _localizer.LocaleChanged += onLocale;
            }

            var subscriptions = new List<IDisposable>
            {
                _avatars.Avatars.Subscribe(_ => Show()),
                _avatars.CurrentAvatarId.Subscribe(_ => Show()),
                _avatars.State.Subscribe(_ => Show()),
                _avatars.Problem.Subscribe(_ => Show()),
                new DisposeAction(ReleaseThumbnail),
                new DisposeAction(() =>
                {
                    if (_localizer != null)
                    {
                        _localizer.LocaleChanged -= onLocale;
                    }
                }),
            };
            return new DisposeAll(subscriptions);
        }

        private void Show()
        {
            (StatusKind kind, string stateKey, string problemKey) = Describe(_avatars.State.Value, _avatars.Problem.Value);
            Status.Status = kind;
            Status.TextKey = stateKey;
            _problemText.Key = problemKey;

            IReadOnlyList<AvatarEntry> avatars = _avatars.Avatars.Value;
            string current = _avatars.CurrentAvatarId.Value;
            Choice.SetChoices(Choices(avatars));
            Choice.SetValueWithoutNotify(current ?? string.Empty);

            AvatarEntry entry = null;
            foreach (AvatarEntry candidate in avatars)
            {
                if (candidate.Id == current)
                {
                    entry = candidate;
                }
            }

            (string formatKey, (string Name, object Value)[] args) = FormatText(entry?.Profile);
            _format.text = formatKey == null ? "—" : _localizer?.Get(formatKey, args) ?? formatKey;
            ShowThumbnail(entry?.ThumbnailPath);
            ShowLicense(_license, entry?.Profile == null ? null : LicenseLines(entry.Profile.DisplayName, entry.Profile.License), interactive: true);
            bool loading = _avatars.State.Value == AvatarState.Loading;
            RegisterButton.SetEnabled(_files != null && !_busy && !loading);
            RemoveButton.SetEnabled(current != null && !_busy && !loading);
            Choice.SetEnabled(!_busy);
        }

        private void ShowLicense(VisualElement into, IReadOnlyList<LicenseLine> lines, bool interactive)
        {
            into.Clear();
            if (lines == null)
            {
                into.Add(Ui.Text(AvatarStrings.ValueNotStated, CaptureSourceCard.InfoClass, "vv-text-body"));
                return;
            }

            foreach (LicenseLine line in lines)
            {
                var value = new Label(line.ValueKey != null ? _localizer?.Get(line.ValueKey) ?? line.ValueKey : line.Text);
                value.AddToClassList("vv-text-body");
                value.AddToClassList(LicenseLineClass);

                // The author's text is shown as written, never parsed as markup.
                value.enableRichText = false;
                SettingRow row = Row(line.NameKey, value);
                if (interactive && line.Url != null)
                {
                    string url = line.Url;
                    ActionButton open = Ui.Button(AvatarStrings.LicenseOpen);
                    open.clicked += () => UnityEngine.Application.OpenURL(url);
                    row.Add(open);
                }

                into.Add(row);
            }
        }

        private void ShowThumbnail(string path)
        {
            if (path == _shownThumbnail)
            {
                return;
            }

            ReleaseThumbnail();
            _shownThumbnail = path;
            if (path != null)
            {
                try
                {
                    var texture = new Texture2D(2, 2) { name = "AvatarThumbnail" };
                    if (texture.LoadImage(File.ReadAllBytes(path)))
                    {
                        _thumbnailTexture = texture;
                    }
                    else
                    {
                        UnityEngine.Object.Destroy(texture);
                    }
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    // A missing thumbnail shows the placeholder; the avatar itself is unaffected.
                    _services?.Log?.Warning("Could not read an avatar thumbnail.", exception);
                }
            }

            _thumbnail.image = _thumbnailTexture;
            _noThumbnail.style.display = _thumbnailTexture == null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void ReleaseThumbnail()
        {
            if (_thumbnailTexture != null)
            {
                UnityEngine.Object.Destroy(_thumbnailTexture);
            }

            _thumbnailTexture = null;
            _shownThumbnail = null;
            _thumbnail.image = null;
        }

        private async Task SelectAsync(string avatarId)
        {
            await Guarded(() => _avatars.SelectAsync(avatarId, CancellationToken.None));
        }

        private async Task RegisterAsync()
        {
            await Guarded(async () =>
            {
                var options = new FileDialogOptions
                {
                    Title = _localizer?.Get(AvatarStrings.FileTitle),
                    Filters = new[] { new FileFilter(_localizer?.Get(AvatarStrings.FileFilter) ?? "VRM", _avatars.Extensions) },
                };
                string path = await _files.OpenAsync(options);
                if (path == null)
                {
                    return;
                }

                AvatarImportResult result = await _avatars.ImportAsync(path, ConfirmAsync, CancellationToken.None);
                if (!result.Imported && !result.Declined)
                {
                    _services?.ShowFailure?.Invoke(ProblemKeyOf(result.Problem) ?? AvatarStrings.ProblemLoadFailed);
                }
            });
        }

        private async Task<bool> ConfirmAsync(AvatarModelInfo info)
        {
            if (_services?.Dialogs == null)
            {
                return true;
            }

            var details = new ScrollView(ScrollViewMode.Vertical);
            ShowLicense(details, LicenseLines(info.Name, info.License), interactive: false);
            string body = info.License.NeedsNotice ? AvatarStrings.ConfirmNotice : AvatarStrings.ConfirmBody;
            return await _services.Dialogs.ConfirmAsync(AvatarStrings.ConfirmTitle, body, AvatarStrings.ConfirmButton, ConfirmStyle.Normal, CancellationToken.None, details);
        }

        private async Task RemoveAsync()
        {
            string current = _avatars.CurrentAvatarId.Value;
            if (current == null)
            {
                return;
            }

            await Guarded(async () =>
            {
                if (_services?.Dialogs != null && !await _services.Dialogs.ConfirmAsync(AvatarStrings.RemoveTitle, AvatarStrings.RemoveBody, AvatarStrings.RemoveButton, ConfirmStyle.Danger))
                {
                    return;
                }

                await _avatars.RemoveAsync(current, CancellationToken.None);
            });
        }

        /// <summary>Runs one operation at a time, with the buttons disabled, and reports what fails unexpectedly.</summary>
        private async Task Guarded(Func<Task> operation)
        {
            if (_busy)
            {
                return;
            }

            _busy = true;
            Show();
            try
            {
                await operation();
            }
            catch (Exception exception)
            {
                _services?.Log?.Error("An avatar operation failed.", exception);
                _services?.ShowFailure?.Invoke(AvatarStrings.ProblemLoadFailed);
            }
            finally
            {
                _busy = false;
                Show();
            }
        }

        private static SettingRow Row(string nameKey, VisualElement control)
        {
            SettingRow row = Ui.SettingRow(nameKey);
            row.Add(control);
            return row;
        }
    }
}
