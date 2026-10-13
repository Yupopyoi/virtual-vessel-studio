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

            string text;
            try
            {
                text = File.ReadAllText(_path);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                text = null;
            }

            return Parse(text);
        }

        public void Write(SessionInfo session, string applicationVersion)
        {
            File.WriteAllText(_path, Format(session, applicationVersion));
        }

        /// <summary>The marker text for <paramref name="session"/>.</summary>
        internal static string Format(SessionInfo session, string applicationVersion)
        {
            var data = new MarkerFile
            {
                sessionId = session.SessionId,
                startedAtUtc = session.StartedAtUtc.ToString("o", CultureInfo.InvariantCulture),
                applicationVersion = applicationVersion,
            };

            return JsonUtility.ToJson(data, prettyPrint: true);
        }

        /// <summary>
        /// Reads a leftover marker. Unreadable or missing content still means an abnormal exit, so the
        /// result is never null; its fields are null when unknown.
        /// </summary>
        internal static PreviousSession Parse(string text)
        {
            if (!string.IsNullOrEmpty(text))
            {
                try
                {
                    MarkerFile data = JsonUtility.FromJson<MarkerFile>(text);
                    if (data != null)
                    {
                        return new PreviousSession(data.sessionId, data.startedAtUtc, data.applicationVersion);
                    }
                }
                catch (ArgumentException)
                {
                    // Not JSON: fall through to an unknown session.
                }
            }

            return new PreviousSession(null, null, null);
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
