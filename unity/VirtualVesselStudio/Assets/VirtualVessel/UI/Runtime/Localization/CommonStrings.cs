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

        /// <summary>Title of the error notice shown when a command fails (UI foundation detailed design 7.8).</summary>
        public const string FailureTitle = "notice.failure.title";

        /// <summary>Plural key: how many notices wait for room (UI foundation detailed design 7.8).</summary>
        public const string MoreNotices = "notice.more";

        /// <summary>Shown under a number field when the typed text is not a number.</summary>
        public const string NumberInvalid = "field.number-invalid";

        /// <summary>Shown under a text field while it takes English input only (components.md 3.7).</summary>
        public const string EnglishOnly = "field.english-only";
    }
}
