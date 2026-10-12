using System;

namespace VirtualVessel.UI.Localization
{
    /// <summary>
    /// Marks a static class whose <c>const string</c> fields are string table keys used from C#.
    /// Tests check that every such key exists in every table (UI foundation detailed design 7.4).
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class StringKeysAttribute : Attribute
    {
    }
}
