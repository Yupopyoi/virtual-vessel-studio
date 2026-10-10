using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using VirtualVessel.Core.Threading;

namespace VirtualVessel.Core.Tests
{
    public sealed class MainThreadDispatcherTests
    {
        [Test]
        public void Drain_RunsActionsInPostingOrder()
        {
            var dispatcher = new MainThreadDispatcher();
            var order = new List<int>();

            dispatcher.Post(() => order.Add(1));
            dispatcher.Post(() => order.Add(2));
            dispatcher.Post(() => order.Add(3));
            dispatcher.Drain(10, null);

            Assert.That(order, Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public void Drain_RespectsLimit_AndLeavesRemainingForLaterFrames()
        {
            var dispatcher = new MainThreadDispatcher();
            int runCount = 0;
            for (int i = 0; i < 5; i++)
            {
                dispatcher.Post(() => runCount++);
            }

            int firstDrain = dispatcher.Drain(3, null);

            Assert.That(firstDrain, Is.EqualTo(3));
            Assert.That(runCount, Is.EqualTo(3));
            Assert.That(dispatcher.PendingCount, Is.EqualTo(2));

            int secondDrain = dispatcher.Drain(3, null);

            Assert.That(secondDrain, Is.EqualTo(2));
            Assert.That(runCount, Is.EqualTo(5));
        }

        [Test]
        public void Drain_ActionThrows_ReportsAndContinues()
        {
            var dispatcher = new MainThreadDispatcher();
            var reported = new List<Exception>();
            bool laterActionRan = false;

            dispatcher.Post(() => throw new InvalidOperationException("boom"));
            dispatcher.Post(() => laterActionRan = true);
            dispatcher.Drain(10, reported.Add);

            Assert.That(reported, Has.Count.EqualTo(1));
            Assert.That(reported[0], Is.TypeOf<InvalidOperationException>());
            Assert.That(laterActionRan, Is.True);
        }

        [Test]
        public void IsMainThread_IsFalseOnOtherThread()
        {
            var dispatcher = new MainThreadDispatcher();

            bool onWorker = Task.Run(() => dispatcher.IsMainThread).Result;

            Assert.That(dispatcher.IsMainThread, Is.True);
            Assert.That(onWorker, Is.False);
        }

        [Test]
        public void Post_FromManyThreads_RunsEveryAction()
        {
            var dispatcher = new MainThreadDispatcher();
            int runCount = 0;
            const int PostsPerThread = 100;
            const int ThreadCount = 8;

            var tasks = new Task[ThreadCount];
            for (int t = 0; t < ThreadCount; t++)
            {
                tasks[t] = Task.Run(() =>
                {
                    for (int i = 0; i < PostsPerThread; i++)
                    {
                        dispatcher.Post(() => Interlocked.Increment(ref runCount));
                    }
                });
            }

            Task.WaitAll(tasks);
            dispatcher.Drain(int.MaxValue, null);

            Assert.That(runCount, Is.EqualTo(PostsPerThread * ThreadCount));
        }

        [Test]
        public void Post_Null_Throws()
        {
            var dispatcher = new MainThreadDispatcher();

            Assert.Throws<ArgumentNullException>(() => dispatcher.Post(null));
        }
    }
}
