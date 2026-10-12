namespace VirtualVessel.UI.Localization
{
    /// <summary>String table keys shared by many screens and components.</summary>
    [StringKeys]
    public static class CommonStrings
    {
        public const string Ok = "common.ok";
        public const string Cancel = "common.cancel";
        public const string Close = "common.close";
        public const string Retry = "common.retry";

        /// <summary>Plural key; use with <see cref="ILocalizer.Plural"/>.</summary>
        public const string Items = "common.items";

        /// <summary>Shown when a command fails without a user-facing explanation (UI foundation detailed design 7.5).</summary>
        public const string UnexpectedError = "error.unexpected";
    }
}
