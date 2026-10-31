using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Performance;
using VirtualVessel.UI.Credits;
using VirtualVessel.UI.Screens;
using VirtualVessel.UI.Theme;
using Object = UnityEngine.Object;

namespace VirtualVessel.UI.Tests
{
    /// <summary>
    /// Writes the real UI's screens to PNG (UiScreenshotCapture, UI foundation detailed design 7.10).
    /// </summary>
    /// <remarks>
    /// A PlayMode test only so that the Unity Test Framework can run it in Play Mode, where runtime
    /// panels are drawn, from the Editor menu or a batch-mode script. It is explicit, so normal test
    /// runs skip it.
    /// </remarks>
    public sealed class UiScreenshotCaptureRun
    {
        // Frames for a change of size, theme, or language to reach the render target.
        private const int SettleFrames = 4;

        // Longer than the UI's transitions (0.12 s for buttons): batch mode runs frames so fast that a
        // few frames alone would catch colors halfway between the old and the new theme.
        private const float SettleSeconds = 0.3f;

        [UnityTest]
        [Explicit("Writes screenshots; run it by name (UI foundation detailed design 7.10).")]
        [Category("Explicit")]
        public IEnumerator Capture()
        {
            string projectPath = Path.GetDirectoryName(UnityEngine.Application.dataPath);
            UiShotOptions options = UiShotOptions.FromEnvironment(Environment.GetEnvironmentVariable, projectPath, DateTime.Now);
            IReadOnlyList<UiShot> shots = UiShotPlan.Create(options);
            Assert.That(shots, Is.Not.Empty, "No screen matches the requested ones.");
            Directory.CreateDirectory(options.Output);

            string config = Path.Combine(Path.GetTempPath(), "VirtualVesselCapture", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(config);
            File.WriteAllText(Path.Combine(config, "ui-settings.json"), "{ \"schemaVersion\": 1, \"developerMode\": true }");
            var host = new GameObject(nameof(UiScreenshotCaptureRun));
            host.AddComponent<UIDocument>();
            UiRoot root = host.AddComponent<UiRoot>();
            root.SetAssets(UnityEditor.AssetDatabase.LoadAssetAtPath<UiAssets>("Assets/VirtualVessel/UI/UiAssets.asset"));
            PerformanceMetricsService metrics = SampleMetrics();
            RenderTexture target = null;
            try
            {
                root.Build(new UiRootContext(config, new StopwatchMonotonicClock(), new UtcSystemClock(), null, null, false, "0.1.0", metrics, screens: new ScreenServices { Captures = new ScreenshotCaptureSources(), Streaming = new StreamPanelTests.FakeStreaming(), Credits = CreditFiles.ForRepository(Path.GetFullPath(Path.Combine(projectPath, "..", "..")), null) }));

                // 96 DPI whatever the machine, so images have the same size everywhere; the UI size still applies.
                root.Panel.scaleMode = PanelScaleMode.ConstantPixelSize;
                root.Panel.clearColor = true;
                root.Panel.colorClearValue = Color.black;

                foreach (UiShot shot in shots)
                {
                    root.Settings.SetLanguage(shot.Language);
                    root.Settings.SetBackground(shot.Background == "light" ? ThemeBackground.Light : ThemeBackground.Dark);
                    root.Settings.SetPalette(shot.Palette);
                    root.Settings.SetUiScale(shot.Scale);
                    root.Shell.Open(shot.Screen);
                    target = Resize(root, target, shot.Width, shot.Height);
                    yield return Settle();

                    // A screen taller than the window is written whole: grow the image to its content.
                    int height = FullHeight(root, shot);
                    if (height > shot.Height)
                    {
                        target = Resize(root, target, shot.Width, height);
                        yield return Settle();
                    }

                    Save(target, Path.Combine(options.Output, shot.FileName));
                }

                File.WriteAllText(Path.Combine(options.Output, "index.html"), UiShotPlan.IndexHtml(shots));
                Debug.Log($"[UiScreenshotCapture] {shots.Count} screenshots in {options.Output}");
            }
            finally
            {
                if (root.Panel != null)
                {
                    root.Panel.targetTexture = null;
                }

                Object.Destroy(host);
                metrics.Dispose();
                if (target != null)
                {
                    target.Release();
                    Object.Destroy(target);
                }

                Directory.Delete(config, recursive: true);
            }
        }

        private static IEnumerator Settle()
        {
            for (int i = 0; i < SettleFrames; i++)
            {
                yield return null;
            }

            yield return new WaitForSecondsRealtime(SettleSeconds);
            yield return null;
        }

        private static RenderTexture Resize(UiRoot root, RenderTexture current, int width, int height)
        {
            if (current != null && current.width == width && current.height == height)
            {
                return current;
            }

            root.Panel.targetTexture = null;
            if (current != null)
            {
                current.Release();
                Object.Destroy(current);
            }

            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            root.Panel.targetTexture = target;
            return target;
        }

        /// <summary>The image height that shows the whole page: the window plus what scrolls below it.</summary>
        private static int FullHeight(UiRoot root, UiShot shot)
        {
            var page = (ScrollView)root.Shell.Host.GetPage(shot.Screen);
            float hidden = page.contentContainer.layout.height - page.contentViewport.layout.height;
            return hidden > 0f ? shot.Height + Mathf.CeilToInt(hidden * shot.Scale) : shot.Height;
        }

        private static void Save(RenderTexture target, string path)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.Destroy(texture);
        }

        /// <summary>A few measurements so the Diagnostics table has rows of every kind.</summary>
        private static PerformanceMetricsService SampleMetrics()
        {
            var metrics = new PerformanceMetricsService(new PerformanceSettings { DetailedEnabled = true, BuiltinMetricsEnabled = false }, new StopwatchMonotonicClock(), new UtcSystemClock(), null);
            metrics.Timer("Application", "MainThreadFrameTime").Record(TimeSpan.FromTicks(57_900));
            metrics.Gauge("Application", "SystemUsedMemory", "MB").Set(1553.86);
            metrics.Gauge("Application", "GcReservedMemory", "MB").Set(138);
            metrics.Counter("Audio", "Underruns").Add(3);
            PerfTimer convert = metrics.Timer("Voice", "RealtimeConversion", TimeSpan.FromTicks(166_700));
            convert.Record(TimeSpan.FromTicks(123_400));
            convert.Record(TimeSpan.FromTicks(212_300));
            return metrics;
        }
    }
}
