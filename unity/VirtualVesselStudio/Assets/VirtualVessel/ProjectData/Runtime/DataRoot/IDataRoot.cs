namespace VirtualVessel.ProjectData.DataRoot
{
    /// <summary>
    /// The resolved storage area for user data such as projects, avatars, and logs.
    /// </summary>
    public interface IDataRoot
    {
        string RootPath { get; }

        /// <summary>
        /// Returns the full path of a standard subdirectory. The directory is not guaranteed to exist
        /// unless the module that owns it has created it.
        /// </summary>
        string GetDirectory(DataRootDirectory directory);
    }
}
