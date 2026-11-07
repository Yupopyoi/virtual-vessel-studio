using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.VoiceLab
{
    /// <summary>
    /// String table keys of the Voice Lab screen (system design 14.13 to 14.18). The shelf's keys are built
    /// from its ids (<see cref="VoiceLabShelf"/>): "lab.group.&lt;id&gt;", "lab.word.&lt;id&gt;" with ".phrase",
    /// and "lab.example.&lt;id&gt;"; the read-back sentence's are "lab.ask.*".
    /// </summary>
    [StringKeys]
    public static class VoiceLabStrings
    {
        public const string StepBase = "lab.step.base";
        public const string StepTimbre = "lab.step.timbre";
        public const string StepBuild = "lab.step.build";
        public const string StepTry = "lab.step.try";
        public const string StepRegister = "lab.step.register";

        public const string NotSetUpTitle = "lab.not-set-up.title";
        public const string NotSetUpBody = "lab.not-set-up.body";
        public const string Stop = "lab.stop";

        public const string BaseLead = "lab.base.lead";
        public const string Mood = "lab.base.mood";
        public const string SamplesMissing = "lab.base.samples-missing";
        public const string MakeSamples = "lab.base.make-samples";
        public const string MakingSamples = "lab.base.making-samples";
        public const string SamplesFailed = "lab.base.samples-failed";
        public const string PlaySample = "lab.base.play-sample";
        public const string SampleNotMade = "lab.base.sample-not-made";
        public const string Examples = "lab.base.examples";
        public const string ExamplesNote = "lab.base.examples-note";
        public const string Shelf = "lab.base.shelf";
        public const string ShelfNote = "lab.base.shelf-note";
        public const string PickOne = "lab.base.pick-one";
        public const string PickAny = "lab.base.pick-any";
        public const string Ask = "lab.base.ask";
        public const string WordsNote = "lab.base.words-note";
        public const string Instruction = "lab.base.instruction";
        public const string MoreMood = "lab.base.more-mood";
        public const string Words = "lab.base.words";
        public const string WordsHint = "lab.base.words-hint";
        public const string Make = "lab.base.make";
        public const string MakeNote = "lab.base.make-note";
        public const string Making = "lab.base.making";
        public const string Loading = "lab.base.loading";
        public const string Count = "lab.base.count";
        public const string CountTip = "lab.base.count.tip";
        public const string Candidates = "lab.base.candidates";
        public const string SortMade = "lab.base.sort.made";
        public const string SortLow = "lab.base.sort.low";
        public const string SortHigh = "lab.base.sort.high";
        public const string SortTip = "lab.base.sort.tip";
        public const string MakeMore = "lab.base.more";
        public const string MakeNear = "lab.base.near";
        public const string MakeNearTip = "lab.base.near.tip";
        public const string CandidatesNote = "lab.base.candidates-note";
        public const string MakeFailed = "lab.base.failed";
        public const string ToTimbre = "lab.base.next";

        public const string TimbreLead = "lab.timbre.lead";
        public const string Timbre = "lab.timbre.title";
        public const string TimbrePitch = "lab.timbre.pitch";
        public const string TimbrePitchTip = "lab.timbre.pitch.tip";
        public const string TimbrePitchLow = "lab.timbre.pitch.low";
        public const string TimbrePitchHigh = "lab.timbre.pitch.high";
        public const string TimbreAge = "lab.timbre.age";
        public const string TimbreAgeTip = "lab.timbre.age.tip";
        public const string TimbreAgeLow = "lab.timbre.age.low";
        public const string TimbreAgeHigh = "lab.timbre.age.high";
        public const string TimbreBrightness = "lab.timbre.brightness";
        public const string TimbreBrightnessTip = "lab.timbre.brightness.tip";
        public const string TimbreBrightnessLow = "lab.timbre.brightness.low";
        public const string TimbreBrightnessHigh = "lab.timbre.brightness.high";
        public const string TimbreSoftness = "lab.timbre.softness";
        public const string TimbreSoftnessTip = "lab.timbre.softness.tip";
        public const string TimbreSoftnessLow = "lab.timbre.softness.low";
        public const string TimbreSoftnessHigh = "lab.timbre.softness.high";
        public const string TimbreBreathiness = "lab.timbre.breathiness";
        public const string TimbreBreathinessTip = "lab.timbre.breathiness.tip";
        public const string TimbreBreathinessLow = "lab.timbre.breathiness.low";
        public const string TimbreBreathinessHigh = "lab.timbre.breathiness.high";
        public const string Compare = "lab.timbre.compare";
        public const string ListenBase = "lab.timbre.listen-base";
        public const string ListenShaped = "lab.timbre.listen-shaped";
        public const string Shaping = "lab.timbre.shaping";
        public const string Reshaping = "lab.timbre.reshaping";
        public const string Hold = "lab.timbre.hold";
        public const string HoldTip = "lab.timbre.hold.tip";
        public const string HeldNote = "lab.timbre.held-note";
        public const string HeldName = "lab.timbre.held-name";
        public const string Restore = "lab.timbre.restore";
        public const string RemoveHeld = "lab.timbre.remove-held";
        public const string PitchChange = "lab.timbre.pitch-change";
        public const string FarTitle = "lab.timbre.far";
        public const string FarBody = "lab.timbre.far-body";
        public const string MechanicalTitle = "lab.timbre.mechanical";
        public const string MechanicalBody = "lab.timbre.mechanical-body";
        public const string TimbreNote = "lab.timbre.note";
        public const string Reset = "lab.timbre.reset";
        public const string ShapeFailed = "lab.timbre.failed";
        public const string ToBuild = "lab.timbre.next";

        public const string BuildLead = "lab.build.lead";
        public const string BuildNote = "lab.build.note";
        public const string BuildPreparing = "lab.build.preparing";
        public const string BuildLearning = "lab.build.learning";
        public const string BuildFinishing = "lab.build.finishing";
        public const string BuildStart = "lab.build.start";
        public const string BuildStreaming = "lab.build.streaming";
        public const string BuildStreamingBody = "lab.build.streaming-body";
        public const string BuildFailed = "lab.build.failed";
        public const string BuildOutOfMemory = "lab.build.out-of-memory";
        public const string BuildCancelled = "lab.build.cancelled";

        public const string TryLead = "lab.try.lead";
        public const string TryLater = "lab.try.later";
        public const string BackToTimbre = "lab.try.back";
        public const string ToRegister = "lab.try.next";

        public const string RegisterLead = "lab.register.lead";
        public const string Register = "lab.register.title";
        public const string VoiceName = "lab.register.name";
        public const string VoiceNameHint = "lab.register.name-hint";
        public const string NameRequired = "lab.register.name-required";
        public const string SummaryBase = "lab.register.summary-base";
        public const string SummaryTimbre = "lab.register.summary-timbre";
        public const string RegisterNote = "lab.register.note";
        public const string RegisterButton = "lab.register.button";
        public const string Registered = "lab.register.done";
        public const string RegisterFailed = "lab.register.failed";
        public const string MakeAnother = "lab.register.again";

        public const string TabMake = "lab.tab.make";
        public const string TabMade = "lab.tab.made";
        public const string LibraryLead = "lab.library.lead";
        public const string LibraryEmpty = "lab.library.empty";
        public const string LibraryFailed = "lab.library.failed";
        public const string LibraryVoices = "lab.library.voices";
        public const string LibraryRuns = "lab.library.runs";
        public const string Rename = "lab.library.rename";
        public const string SaveName = "lab.library.save-name";
        public const string Delete = "lab.library.delete";
        public const string DeleteVoiceTitle = "lab.library.delete-voice";
        public const string DeleteVoiceBody = "lab.library.delete-voice.body";
        public const string DeleteRunTitle = "lab.library.delete-run";
        public const string DeleteRunBody = "lab.library.delete-run.body";
        public const string DeleteWhileMaking = "lab.library.delete-making";
        public const string RunCompleted = "lab.library.run.completed";
        public const string RunCancelled = "lab.library.run.cancelled";
        public const string RunFailed = "lab.library.run.failed";
        public const string RunMaking = "lab.library.run.making";
        public const string RegisteredAs = "lab.library.registered-as";
        public const string Remake = "lab.library.remake";
        public const string Comment = "lab.library.comment";
        public const string PlayBase = "lab.library.play-base";
        public const string QuotedWords = "lab.library.quoted";
        public const string BaseGone = "lab.base.base-gone";
        public const string BaseGoneBody = "lab.base.base-gone.body";
        public const string RemadeBase = "lab.timbre.remade-base";
    }
}
