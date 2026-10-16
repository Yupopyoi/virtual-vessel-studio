using System.Linq;
using NUnit.Framework;
using UnityEditor;

namespace VirtualVessel.Application.Tests
{
    public sealed class PersistentSceneSetupTests
    {
        private const string PersistentScenePath = "Assets/Scenes/Persistent.unity";

        [Test]
        public void PersistentScene_IsFirstEnabledSceneInBuildSettings()
        {
            EditorBuildSettingsScene first = EditorBuildSettings.scenes.FirstOrDefault(scene => scene.enabled);

            Assert.That(first, Is.Not.Null, "No scene is enabled in the build settings.");
            Assert.That(first.path, Is.EqualTo(PersistentScenePath));
        }

        [Test]
        public void PersistentScene_ContainsExactlyOneBootstrap()
        {
            string[] dependencies = AssetDatabase.GetDependencies(PersistentScenePath, recursive: false);
            string bootstrapScript = dependencies.SingleOrDefault(path => path.EndsWith("/ApplicationBootstrap.cs", System.StringComparison.Ordinal));

            Assert.That(bootstrapScript, Is.Not.Null, "The persistent scene does not reference ApplicationBootstrap.");

            string sceneText = System.IO.File.ReadAllText(PersistentScenePath);
            string scriptGuid = AssetDatabase.AssetPathToGUID(bootstrapScript);
            int occurrences = (sceneText.Length - sceneText.Replace(scriptGuid, string.Empty).Length) / scriptGuid.Length;

            Assert.That(occurrences, Is.EqualTo(1), "The persistent scene must contain exactly one ApplicationBootstrap.");
        }

        [Test]
        public void PersistentScene_HasOneUiRoot_WithAssets_ThatTheBootstrapReferences()
        {
            string sceneText = System.IO.File.ReadAllText(PersistentScenePath);
            string rootGuid = AssetDatabase.FindAssets("UiRoot t:MonoScript")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.EndsWith("/UiRoot.cs", System.StringComparison.Ordinal))
                .Select(AssetDatabase.AssetPathToGUID)
                .Single();
            string[] documents = sceneText.Split(new[] { "--- !u!" }, System.StringSplitOptions.None);
            string[] roots = documents.Where(document => document.Contains("guid: " + rootGuid)).ToArray();

            Assert.That(roots, Has.Length.EqualTo(1), "The persistent scene must contain exactly one UiRoot.");
            string rootId = roots[0].Substring(roots[0].IndexOf('&') + 1).Split('\n')[0].Trim();
            string assetsGuid = AssetDatabase.AssetPathToGUID("Assets/VirtualVessel/UI/UiAssets.asset");
            Assert.That(roots[0], Does.Contain($"_assets: {{fileID: 11400000, guid: {assetsGuid}, type: 2}}"), "The UiRoot must reference UiAssets.asset.");
            Assert.That(sceneText, Does.Contain($"_uiRoot: {{fileID: {rootId}}}"), "ApplicationBootstrap must reference the UiRoot.");
        }
    }
}
