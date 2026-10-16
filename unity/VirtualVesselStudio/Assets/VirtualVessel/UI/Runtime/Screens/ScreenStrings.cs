using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.Screens
{
    /// <summary>String table keys of the navigation and the screens (UI foundation detailed design 7.7).</summary>
    [StringKeys]
    public static class ScreenStrings
    {
        public const string Live = "nav.live";
        public const string Setup = "nav.setup";
        public const string Lab = "nav.lab";
        public const string Diagnostics = "nav.diagnostics";

        public const string LiveTip = "tip.nav.live";
        public const string SetupTip = "tip.nav.setup";
        public const string LabTip = "tip.nav.lab";
        public const string DiagnosticsTip = "tip.nav.diagnostics";

        /// <summary>Shown on a screen whose content is not built yet.</summary>
        public const string Placeholder = "screen.placeholder";

        /// <summary>Shown in place of a screen that failed to build.</summary>
        public const string Failed = "screen.failed";

        public const string AppSettingsTitle = "settings.title";
        public const string Language = "settings.language";
        public const string Palette = "settings.palette";
        public const string Background = "settings.background";
        public const string Dark = "settings.dark";
        public const string Light = "settings.light";
        public const string Scale = "settings.scale";
        public const string Scale100 = "settings.scale.100";
        public const string Scale110 = "settings.scale.110";
        public const string Scale125 = "settings.scale.125";
        public const string Scale150 = "settings.scale.150";
        public const string Scale175 = "settings.scale.175";
        public const string Scale200 = "settings.scale.200";
        public const string Motion = "settings.motion";
        public const string MotionDescription = "settings.motion.description";
        public const string MotionSystem = "settings.motion.system";
        public const string MotionOn = "settings.motion.on";
        public const string MotionOff = "settings.motion.off";
        public const string Developer = "settings.developer";
        public const string DeveloperDescription = "settings.developer.description";

        public const string Metrics = "diagnostics.metrics";
        public const string DetailedOn = "diagnostics.detailed-on";
        public const string DetailedOff = "diagnostics.detailed-off";
        public const string MetricsUnavailable = "diagnostics.unavailable";
        public const string ColumnModule = "diagnostics.column.module";
        public const string ColumnName = "diagnostics.column.name";
        public const string ColumnKind = "diagnostics.column.kind";
        public const string ColumnCurrent = "diagnostics.column.current";
        public const string ColumnAverage = "diagnostics.column.average";
        public const string ColumnMax = "diagnostics.column.max";
        public const string ColumnP95 = "diagnostics.column.p95";
        public const string ColumnP99 = "diagnostics.column.p99";
        public const string ColumnCount = "diagnostics.column.count";
        public const string ColumnOverBudget = "diagnostics.column.over-budget";
        public const string KindTimer = "diagnostics.kind.timer";
        public const string KindCounter = "diagnostics.kind.counter";
        public const string KindGauge = "diagnostics.kind.gauge";
    }
}
