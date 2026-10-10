using System;
using System.IO;

namespace VirtualVessel.ProjectData.DataRoot
{
    internal sealed class DataRootPaths : IDataRoot
    {
        public DataRootPaths(string rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                throw new ArgumentException("Data Root path must not be empty.", nameof(rootPath));
            }

            RootPath = Path.GetFullPath(rootPath);
        }

        public string RootPath { get; }

        public string GetDirectory(DataRootDirectory directory)
        {
            return Path.Combine(RootPath, directory.ToString());
        }
    }
}
