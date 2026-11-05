using System;
using System.Collections.Generic;
using NUnit.Framework;
using VirtualVessel.ExternalServices.Process;

namespace VirtualVessel.ExternalServices.Tests
{
    public sealed class ProcessRulesTests
    {
        [TestCase("plain", "plain")]
        [TestCase("", "\"\"")]
        [TestCase("with space", "\"with space\"")]
        [TestCase("say \"hi\"", "\"say \\\"hi\\\"\"")]
        [TestCase(@"C:\Program Files\", "\"C:\\Program Files\\\\\"")]
        [TestCase(@"C:\folder\file.json", @"C:\folder\file.json")]
        [TestCase(@"a\\""b", "\"a\\\\\\\\\\\"b\"")]
        public void Quote_FollowsTheWindowsRules(string argument, string expected)
        {
            Assert.That(ProcessRules.Quote(argument), Is.EqualTo(expected));
        }

        [Test]
        public void CommandLine_JoinsQuotedArguments()
        {
            Assert.That(ProcessRules.CommandLine(new[] { "-I", "train.py", "run", @"C:\My Runs\request.json" }),
                Is.EqualTo("-I train.py run \"C:\\My Runs\\request.json\""));
        }

        [TestCase(@"C:\Tools\python\python.exe", true)]
        [TestCase(@"c:\tools\PYTHON\python.exe", true)]
        [TestCase(@"C:\Tools2\python.exe", false)]
        [TestCase(@"C:\Tools\..\Windows\cmd.exe", false)]
        [TestCase(@"python.exe", false)]
        [TestCase(@"D:\Data\run.exe", true)]
        public void IsInside_ResolvesTheFullPath(string path, bool inside)
        {
            Assert.That(ProcessRules.IsInside(path, new[] { @"C:\Tools", @"D:\Data\" }), Is.EqualTo(inside));
        }

        [Test]
        public void IsInside_IgnoresRelativeFolders()
        {
            Assert.That(ProcessRules.IsInside(@"C:\Tools\a.exe", new[] { "Tools", "" }), Is.False);
        }

        [TestCase(0, false, false, ExternalProcessState.Completed)]
        [TestCase(3, false, false, ExternalProcessState.Failed)]
        [TestCase(null, true, false, ExternalProcessState.Cancelled)]
        [TestCase(0, true, false, ExternalProcessState.Cancelled)]
        [TestCase(null, false, true, ExternalProcessState.Failed)]
        public void Classify_TellsHowARunEnded(int? exitCode, bool cancelled, bool timedOut, ExternalProcessState state)
        {
            Assert.That(ProcessRules.Classify(exitCode, cancelled, timedOut), Is.EqualTo(state));
        }

        [Test]
        public void DescribeEnvironment_HidesSecretsAndSorts()
        {
            var environment = new Dictionary<string, string> { ["TOKEN"] = "abc123", ["PYTHONUTF8"] = "1" };

            string text = ProcessRules.DescribeEnvironment(environment, new[] { "TOKEN" });

            Assert.That(text, Is.EqualTo("PYTHONUTF8=1, TOKEN=(hidden)"));
            Assert.That(text, Does.Not.Contain("abc123"));
        }

        [Test]
        public void Tail_KeepsTheLastLines()
        {
            Assert.That(ProcessRules.Tail(new[] { "a", "b", "c" }, 2), Is.EqualTo(new[] { "b", "c" }));
            Assert.That(ProcessRules.Tail(Array.Empty<string>(), 2), Is.Empty);
        }
    }
}
