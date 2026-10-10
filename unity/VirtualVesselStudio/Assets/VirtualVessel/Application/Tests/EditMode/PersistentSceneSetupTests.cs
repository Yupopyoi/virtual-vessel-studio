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
    }
}
