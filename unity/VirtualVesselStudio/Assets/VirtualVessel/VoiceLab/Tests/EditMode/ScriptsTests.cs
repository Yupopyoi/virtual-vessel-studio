using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using VirtualVessel.ExternalServices.Process;
using VirtualVessel.ProjectData.Config;
using VirtualVessel.VoiceLab.Scripts;

namespace VirtualVessel.VoiceLab.Tests
{
    public sealed class ScriptsTests
    {
        private static readonly VoiceLabEnvironment s_environment = new VoiceLabEnvironment(
            @"C:\env\Scripts\python.exe", @"C:\repo\services\voice-lab", @"C:\rvc", @"C:\models\VoxCPM2", @"C:\t\fused_template_w100.sentis", @"C:\t\fused_template_w100.json");

        [Test]
        public void ScriptLine_ReadsEachKind()
        {
            ScriptLine progress = ScriptLine.Parse("{\"type\":\"progress\",\"stage\":\"train\",\"done\":3,\"total\":150}");
            ScriptLine result = ScriptLine.Parse("{\"type\":\"result\",\"output\":\"x\"}");
            ScriptLine error = ScriptLine.Parse("{\"type\":\"error\",\"code\":\"out_of_memory\",\"message\":\"m\"}");

            Assert.That((progress.Kind, progress.Stage, progress.Done, progress.Total), Is.EqualTo((ScriptLineKind.Progress, "train", 3, 150)));
            Assert.That(result.Fields.Get<string>("output"), Is.EqualTo("x"));
            Assert.That((error.Kind, error.ErrorCode, error.ErrorMessage), Is.EqualTo((ScriptLineKind.Error, "out_of_memory", "m")));
        }

        [TestCase("Loading model...")]
        [TestCase("{not json")]
        [TestCase("{\"type\":\"other\"}")]
        [TestCase("")]
        public void ScriptLine_OtherTextIsOther(string text)
        {
            Assert.That(ScriptLine.Parse(text).Kind, Is.EqualTo(ScriptLineKind.Other));
        }

        [Test]
        public void Request_RunsTheScriptIsolatedWithItsRequestFile()
        {
            ExternalProcessRequest request = PythonScripts.Request(s_environment, "train.py", "run", @"C:\runs\1\requests\train-run.json", @"C:\runs\1\logs\train-run.log");

            Assert.That(request.FileName, Is.EqualTo(s_environment.Python));
            Assert.That(request.Arguments, Is.EqualTo(new[] { "-I", @"C:\repo\services\voice-lab\train.py", "run", @"C:\runs\1\requests\train-run.json" }));
            Assert.That(request.WorkingDirectory, Is.EqualTo(s_environment.Scripts));
            Assert.That(request.Environment["PYTHONUTF8"], Is.EqualTo("1"));
        }

        [Test]
        public void Outcome_ReturnsTheResultOrSaysWhyNot()
        {
            ConfigDocument fields = ConfigDocument.Parse("{\"ok\":true}");
            ExternalProcessResult completed = Result(ExternalProcessState.Completed, 0);

            Assert.That(PythonScripts.Outcome(completed, fields, null, "x"), Is.SameAs(fields));
            Assert.That(Code(Result(ExternalProcessState.Cancelled, null), null, null), Is.EqualTo("cancelled"));
            Assert.That(Code(Result(ExternalProcessState.Failed, 1), null, ScriptLine.Parse("{\"type\":\"error\",\"code\":\"missing_file\",\"message\":\"m\"}")), Is.EqualTo("missing_file"));
            Assert.That(Code(Result(ExternalProcessState.Failed, 1), null, null), Is.EqualTo("failed"));
            // A clean exit with no result line is still a failure: the script broke its protocol.
            Assert.That(Code(completed, null, null), Is.EqualTo("failed"));
        }

        [Test]
        public void Environment_ParsesAndReportsWhatIsMissing()
        {
            string json = "{\"python\":\"C:\\\\env\\\\Scripts\\\\python.exe\",\"scripts\":\"C:\\\\s\",\"rvc\":\"\",\"voxCpmModel\":\"C:\\\\m\",\"template\":\"C:\\\\t.sentis\",\"manifest\":\"C:\\\\t.json\"}";

            VoiceLabEnvironment environment = VoiceLabEnvironment.Parse(json, out string problem);

            Assert.That(problem, Is.Null);
            Assert.That(environment.Python, Is.EqualTo(@"C:\env\Scripts\python.exe"));
            var problems = environment.Problems(path => path != @"C:\t.json", _ => true);
            Assert.That(problems, Has.Count.EqualTo(2));
            Assert.That(problems.Any(p => p.StartsWith("Rvc")) && problems.Any(p => p.StartsWith("Manifest")), Is.True);
            Assert.That(VoiceLabEnvironment.Parse("{", out problem), Is.Null);
            Assert.That(problem, Does.Contain("not valid JSON"));
        }

        [Test]
        public void NumberedName_TakesTheFirstFreeNumber()
        {
            Assert.That(PythonScripts.NumberedName("train-run", _ => false), Is.EqualTo("train-run-1"));
            Assert.That(PythonScripts.NumberedName("train-run", n => n != "train-run-3"), Is.EqualTo("train-run-3"));
        }

        [TestCase(@"C:\env\Scripts\python.exe", @"C:\env")]
        [TestCase(@"C:\Python312\python.exe", @"C:\Python312")]
        public void EnvironmentFolder_IsTheVenvRoot(string python, string folder)
        {
            Assert.That(VoiceLabEnvironment.EnvironmentFolder(python), Is.EqualTo(folder));
        }

        private static string Code(ExternalProcessResult ended, ConfigDocument result, ScriptLine error)
        {
            return Assert.Throws<ScriptFailedException>(() => PythonScripts.Outcome(ended, result, error, "x")).Code;
        }

        private static ExternalProcessResult Result(ExternalProcessState state, int? exitCode)
        {
            return new ExternalProcessResult(state, exitCode, false, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Array.Empty<string>());
        }
    }
}
