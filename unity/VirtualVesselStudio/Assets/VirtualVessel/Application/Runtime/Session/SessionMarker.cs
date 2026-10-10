using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace VirtualVessel.Application.Session
{
    /// <summary>
    /// A file that exists only while the application is running.
    /// </summary>
    /// <remarks>
    /// The marker is written at startup and deleted after a normal shutdown. Finding a marker at the
    /// next startup means the previous run crashed or was killed (system design 5.28).
    /// </remarks>
    internal sealed class SessionMarker
    {
        public const string FileName = "session.lock";

        private readonly string _path;

        public SessionMarker(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException("Marker directory must not be empty.", nameof(directory));
            }

            _path = Path.Combine(directory, FileName);
        }

        public string FilePath => _path;

        /// <summary>
        /// Returns the session left behind by an abnormal exit, or null if the previous run exited normally.
        /// </summary>
        public PreviousSession ReadLeftover()
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            try
            {
                MarkerFile data = JsonUtility.FromJson<MarkerFile>(File.ReadAllText(_path));
                if (data != null)
                {
                    return new PreviousSession(data.sessionId, data.startedAtUtc, data.applicationVersion);
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                // The marker still proves the previous run did not exit normally, even if its content is unreadable.
            }

            return new PreviousSession(null, null, null);
        }

        public void Write(SessionInfo session, string applicationVersion)
        {
            var data = new MarkerFile
            {
                sessionId = session.SessionId,
                startedAtUtc = session.StartedAtUtc.ToString("o", CultureInfo.InvariantCulture),
                applicationVersion = applicationVersion,
            };

            File.WriteAllText(_path, JsonUtility.ToJson(data, prettyPrint: true));
        }

        public void Delete()
        {
            File.Delete(_path);
        }

        [Serializable]
        private sealed class MarkerFile
        {
            // Field names match the JSON property names because JsonUtility maps by field name.
            public string sessionId;
            public string startedAtUtc;
            public string applicationVersion;
        }
    }
}
