using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace VirtualVessel.UI.Editor
{
    /// <summary>
    /// The menu that writes the UI's screens to PNG (UI foundation detailed design 7.10). It runs the
    /// explicit PlayMode test that does the work and opens the output folder when it is done.
    /// </summary>
    /// <remarks>
    /// Batch mode: <c>tools/ui/capture-ui.ps1</c>, which runs the same test with <c>-runTests</c>.
    /// Entering Play Mode reloads scripts, so the callbacks are registered again on every load and the
    /// pending output folder is kept in <see cref="SessionState"/>.
    /// </remarks>
    [InitializeOnLoad]
    public static class UiScreenshotCapture
    {
        public const string TestName = "VirtualVessel.UI.Tests.UiScreenshotCaptureRun.Capture";

        // Same name as UiShotOptions.OutputVariable in the PlayMode test assembly, which this one cannot reference.
        public const string OutputVariable = "VV_UI_CAPTURE";

        private const string PendingKey = "VirtualVessel.UiScreenshotCapture.Output";

        static UiScreenshotCapture()
        {
            ScriptableObject.CreateInstance<TestRunnerApi>().RegisterCallbacks(new RunCallbacks());
        }

        [MenuItem("Virtual Vessel/UI/Capture Screenshots")]
        public static void Capture()
        {
            string projectPath = Path.GetDirectoryName(Application.dataPath);
            string output = OutputFolder(projectPath, DateTime.Now);
            Environment.SetEnvironmentVariable(OutputVariable, output);
            SessionState.SetString(PendingKey, output);
            ScriptableObject.CreateInstance<TestRunnerApi>().Execute(new ExecutionSettings(new Filter
            {
                testMode = TestMode.PlayMode,
                testNames = new[] { TestName },
            }));
        }

        /// <summary>screenshots/yyyyMMdd-HHmmss at the repository root, which Git ignores. Pure.</summary>
        public static string OutputFolder(string projectPath, DateTime now)
        {
            string repository = Path.GetFullPath(Path.Combine(projectPath, "..", ".."));
            return Path.Combine(repository, "screenshots", now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        }

        private sealed class RunCallbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                string output = SessionState.GetString(PendingKey, null);
                if (string.IsNullOrEmpty(output))
                {
                    // A run this menu did not start.
                    return;
                }

                SessionState.EraseString(PendingKey);
                Environment.SetEnvironmentVariable(OutputVariable, null);
                if (result.TestStatus == TestStatus.Passed && Directory.Exists(output))
                {
                    EditorUtility.RevealInFinder(Path.Combine(output, "index.html"));
                }
                else
                {
                    Debug.LogError($"[UiScreenshotCapture] The capture did not finish ({result.TestStatus}). See the Test Runner and the Console.");
                }
            }

            public void TestStarted(ITestAdaptor test)
            {
            }

            public void TestFinished(ITestResultAdaptor result)
            {
            }
        }
    }
}
