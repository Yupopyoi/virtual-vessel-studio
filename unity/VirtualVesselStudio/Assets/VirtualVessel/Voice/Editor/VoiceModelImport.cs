using System;
using System.IO;
using Unity.InferenceEngine;
using UnityEditor;

namespace VirtualVessel.Voice.Editor
{
    /// <summary>
    /// Turns ONNX files placed in a project's Assets/VoiceImport into .sentis files elsewhere, because only
    /// the Editor can read ONNX. Run with -executeMethod; the models never enter the repository.
    /// </summary>
    public static class VoiceModelImport
    {
        private const string ImportFolder = "Assets/VoiceImport";

        public static void Run()
        {
            string target = Environment.GetEnvironmentVariable("VV_VOICE_SENTIS") ?? throw new InvalidOperationException("Set VV_VOICE_SENTIS.");
            Directory.CreateDirectory(target);
            AssetDatabase.Refresh();
            foreach (string guid in AssetDatabase.FindAssets("t:ModelAsset", new[] { ImportFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                ModelAsset asset = AssetDatabase.LoadAssetAtPath<ModelAsset>(path);
                ModelWriter.Save(Path.Combine(target, Path.GetFileNameWithoutExtension(path) + ".sentis"), asset);
            }
        }
    }
}
