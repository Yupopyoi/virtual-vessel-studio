using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VirtualVessel.Application.Hosting;
using VirtualVessel.Application.Session;
using VirtualVessel.Application.Startup;
using VirtualVessel.ProjectData.DataRoot;
using Object = UnityEngine.Object;

namespace VirtualVessel.Application.Tests
{
    public sealed class ApplicationBootstrapTests
    {
        private const float TimeoutSeconds = 10f;

        private string _tempDirectory;
        private string _previousEnvironmentValue;
        private GameObject _bootstrapObject;

        [SetUp]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "VirtualVesselTests", Guid.NewGuid().ToString("N"));
            _previousEnvironmentValue = Environment.GetEnvironmentVariable(ApplicationStartupOptions.DataRootEnvironmentVariable);

            // Redirect the Data Root so the test never touches the developer's real data.
            Environment.SetEnvironmentVariable(ApplicationStartupOptions.DataRootEnvironmentVariable, _tempDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (_bootstrapObject != null)
            {
                Object.DestroyImmediate(_bootstrapObject);
            }

            Environment.SetEnvironmentVariable(ApplicationStartupOptions.DataRootEnvironmentVariable, _previousEnvironmentValue);

            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }

        [UnityTest]
        public IEnumerator Bootstrap_StartsWithOverriddenDataRoot_AndShutsDownCleanly()
        {
            _bootstrapObject = new GameObject("TestApplicationBootstrap");
            var bootstrap = _bootstrapObject.AddComponent<ApplicationBootstrap>();

            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (IsStarting(bootstrap.State) && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(bootstrap.State, Is.EqualTo(ApplicationState.Running));
            Assert.That(bootstrap.Runtime.DataRoot.RootPath, Is.EqualTo(Path.GetFullPath(_tempDirectory)));

            string markerPath = Path.Combine(bootstrap.Runtime.DataRoot.GetDirectory(DataRootDirectory.Logs), SessionMarker.FileName);
            Assert.That(File.Exists(markerPath), Is.True);

            Task stop = bootstrap.ShutdownAsync();
            deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!stop.IsCompleted && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(stop.IsCompleted, Is.True, "Shutdown did not finish in time.");
            Assert.That(bootstrap.State, Is.EqualTo(ApplicationState.Stopped));
            Assert.That(File.Exists(markerPath), Is.False);

            // Play Mode in the Editor logs to Logs/Editor, separate from built-application logs.
            string editorLogs = Path.Combine(bootstrap.Runtime.DataRoot.GetDirectory(DataRootDirectory.Logs), ApplicationComposition.EditorLogFolder);
            string[] logFiles = Directory.GetFiles(editorLogs, "*.jsonl");
            Assert.That(logFiles, Has.Length.EqualTo(1));
            string logText = File.ReadAllText(logFiles[0]);
            Assert.That(logText, Does.Contain(bootstrap.Runtime.Session.SessionId));
            Assert.That(logText, Does.Contain("Application started in"));
            Assert.That(logText, Does.Contain("Logging stopped."));
        }

        [UnityTest]
        public IEnumerator SecondBootstrap_IsDestroyed()
        {
            _bootstrapObject = new GameObject("TestApplicationBootstrap");
            var first = _bootstrapObject.AddComponent<ApplicationBootstrap>();

            var duplicateObject = new GameObject("DuplicateBootstrap");
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("More than one ApplicationBootstrap"));
            duplicateObject.AddComponent<ApplicationBootstrap>();
            yield return null;

            Assert.That(duplicateObject == null, Is.True, "The duplicate bootstrap should be destroyed.");
            Assert.That(first != null, Is.True);

            Task stop = first.ShutdownAsync();
            while (!stop.IsCompleted)
            {
                yield return null;
            }
        }

        private static bool IsStarting(ApplicationState state)
        {
            return state == ApplicationState.NotStarted || state == ApplicationState.Starting;
        }
    }
}
