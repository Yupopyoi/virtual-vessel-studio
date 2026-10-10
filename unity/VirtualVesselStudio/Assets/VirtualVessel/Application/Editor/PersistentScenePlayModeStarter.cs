using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace VirtualVessel.Application.Editor
{
    /// <summary>
    /// Makes Play Mode always start from the persistent scene, whichever scene is open in the Editor.
    /// </summary>
    /// <remarks>
    /// The application foundation lives only in the persistent scene. Without this, pressing Play while
    /// editing a stage scene would run the stage without the application, which never happens in a build.
    /// The start scene is cleared while tests run, because PlayMode tests also enter Play Mode and must
    /// not start the real application against the user's Data Root.
    /// </remarks>
    [InitializeOnLoad]
    internal static class PersistentScenePlayModeStarter
    {
        public const string PersistentScenePath = "Assets/Scenes/Persistent.unity";

        static PersistentScenePlayModeStarter()
        {
            if (UnityEngine.Application.isBatchMode)
            {
                // Batch mode is used for automated test runs, never for interactive play.
                EditorSceneManager.playModeStartScene = null;
                return;
            }

            ScriptableObject.CreateInstance<TestRunnerApi>().RegisterCallbacks(new TestRunGuard());

            // Defer until the asset database is ready; loading assets from a static constructor during
            // domain reload can return null.
            EditorApplication.delayCall += Apply;
        }

        private static void Apply()
        {
            var persistentScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(PersistentScenePath);
            if (persistentScene == null)
            {
                Debug.LogWarning($"[Application] Persistent scene not found at {PersistentScenePath}. Play Mode starts from the open scene.");
                return;
            }

            EditorSceneManager.playModeStartScene = persistentScene;
        }

        private sealed class TestRunGuard : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
                EditorSceneManager.playModeStartScene = null;
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                Apply();
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
