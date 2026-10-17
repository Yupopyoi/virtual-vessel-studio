using System;
using System.Collections.Generic;
using NUnit.Framework;
using VirtualVessel.Core.Lifecycle;

namespace VirtualVessel.Core.Tests
{
    public sealed class FrameLoopTests
    {
        [Test]
        public void Tick_CallsWorkInRegistrationOrder_EveryFrame()
        {
            var loop = new FrameLoop();
            var calls = new List<string>();
            loop.Register(() => calls.Add("a"));
            loop.Register(() => calls.Add("b"));

            loop.Tick(null);
            loop.Tick(null);

            Assert.That(calls, Is.EqualTo(new[] { "a", "b", "a", "b" }));
        }

        [Test]
        public void Dispose_RemovesTheWork_AndTwiceIsHarmless()
        {
            var loop = new FrameLoop();
            int calls = 0;
            IDisposable registration = loop.Register(() => calls++);

            loop.Tick(null);
            registration.Dispose();
            registration.Dispose();
            loop.Tick(null);

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(loop.Count, Is.EqualTo(0));
        }

        [Test]
        public void AnException_DoesNotStopTheOthers_AndIsReportedOnce()
        {
            var loop = new FrameLoop();
            var errors = new List<Exception>();
            int after = 0;
            loop.Register(() => throw new InvalidOperationException("broken"));
            loop.Register(() => after++);

            loop.Tick(errors.Add);
            loop.Tick(errors.Add);

            Assert.That(after, Is.EqualTo(2), "The next work still runs every frame.");
            Assert.That(errors, Has.Count.EqualTo(1), "A failing tick does not flood the log every frame.");
        }

        [Test]
        public void RegisteringDuringATick_RunsFromTheNextTick_AndRemovingSkipsAtOnce()
        {
            var loop = new FrameLoop();
            var calls = new List<string>();
            IDisposable second = null;
            loop.Register(() =>
            {
                calls.Add("first");
                if (calls.Count == 1)
                {
                    loop.Register(() => calls.Add("added"));
                    second.Dispose();
                }
            });
            second = loop.Register(() => calls.Add("second"));

            loop.Tick(null);
            loop.Tick(null);

            Assert.That(calls, Is.EqualTo(new[] { "first", "first", "added" }));
        }
    }
}
