using System;
using Newtonsoft.Json.Linq;

namespace VirtualVessel.ProjectData.Config
{
    /// <summary>
    /// A settings file's contents as a tree of named values, given to migrations so that they can
    /// rename, move, or remove fields without knowing the JSON library.
    /// </summary>
    /// <remarks>
    /// Paths use dots for nesting, such as <c>"audio.inputDevice"</c>. Values are JSON primitives,
    /// strings, or objects that serialize to them.
    /// </remarks>
    public sealed class ConfigDocument
    {
        private readonly JObject _root;

        internal ConfigDocument(JObject root)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
        }

        public bool Contains(string path)
        {
            return Find(path) != null;
        }

        public T Get<T>(string path, T fallback = default)
        {
            JToken token = Find(path);
            if (token == null || token.Type == JTokenType.Null)
            {
                return fallback;
            }

            try
            {
                return token.ToObject<T>();
            }
            catch (Exception exception) when (exception is FormatException || exception is InvalidCastException || exception is ArgumentException || exception is Newtonsoft.Json.JsonException)
            {
                return fallback;
            }
        }

        /// <summary>Sets a value, creating intermediate objects as needed.</summary>
        public void Set<T>(string path, T value)
        {
            string[] parts = Split(path);
            JObject parent = _root;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (!(parent[parts[i]] is JObject child))
                {
                    child = new JObject();
                    parent[parts[i]] = child;
                }

                parent = child;
            }

            parent[parts[parts.Length - 1]] = value == null ? JValue.CreateNull() : JToken.FromObject(value);
        }

        public bool Remove(string path)
        {
            JToken token = Find(path);
            if (token?.Parent is JProperty property)
            {
                property.Remove();
                return true;
            }

            return false;
        }

        /// <summary>Moves a value to another path. Returns false if there was nothing to move.</summary>
        public bool Rename(string fromPath, string toPath)
        {
            JToken token = Find(fromPath);
            if (token == null)
            {
                return false;
            }

            JToken copy = token.DeepClone();
            Remove(fromPath);
            string[] parts = Split(toPath);
            JObject parent = _root;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (!(parent[parts[i]] is JObject child))
                {
                    child = new JObject();
                    parent[parts[i]] = child;
                }

                parent = child;
            }

            parent[parts[parts.Length - 1]] = copy;
            return true;
        }

        private JToken Find(string path)
        {
            JToken current = _root;
            foreach (string part in Split(path))
            {
                if (!(current is JObject obj) || !obj.TryGetValue(part, StringComparison.Ordinal, out JToken next))
                {
                    return null;
                }

                current = next;
            }

            return current;
        }

        private static string[] Split(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Path must not be empty.", nameof(path));
            }

            return path.Split('.');
        }
    }

    /// <summary>One step of a settings format change, from <see cref="FromVersion"/> to the next version.</summary>
    public sealed class ConfigMigration
    {
        public ConfigMigration(int fromVersion, Action<ConfigDocument> apply)
        {
            if (fromVersion < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(fromVersion), fromVersion, "Versions start at 1.");
            }

            FromVersion = fromVersion;
            Apply = apply ?? throw new ArgumentNullException(nameof(apply));
        }

        public int FromVersion { get; }

        public Action<ConfigDocument> Apply { get; }
    }
}
