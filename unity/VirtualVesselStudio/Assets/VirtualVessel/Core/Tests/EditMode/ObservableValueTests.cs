using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using VirtualVessel.Core.State;
using Is = NUnit.Framework.Is;
using UnityIs = UnityEngine.TestTools.Constraints.Is;

namespace VirtualVessel.Core.Tests
{
    public sealed class ObservableValueTests
    {
        [Test]
        public void Set_NotifiesSubscribersWithNewValue()
        {
            var value = new ObservableValue<int>(1);
            var received = new List<int>();
            value.Subscribe(received.Add);

            bool changed = value.Set(2);

            Assert.That(changed, Is.True);
            Assert.That(value.Value, Is.EqualTo(2));
            Assert.That(received, Is.EqualTo(new[] { 2 }));
        }

        [Test]
        public void Set_EqualValue_DoesNotNotify()
        {
            var value = new ObservableValue<string>("live");
            int calls = 0;
            value.Subscribe(_ => calls++);

            bool changed = value.Set("live");

            Assert.That(changed, Is.False);
            Assert.That(calls, Is.Zero);
        }

        [Test]
        public void Subscribe_DoesNotDeliverCurrentValue()
        {
            var value = new ObservableValue<int>(5);
            int calls = 0;

            value.Subscribe(_ => calls++);

            Assert.That(calls, Is.Zero);
        }

        [Test]
        public void Dispose_StopsNotifications_AndIsIdempotent()
        {
            var value = new ObservableValue<int>();
            int calls = 0;
            Action<int> handler = _ => calls++;
            IDisposable first = value.Subscribe(handler);
            value.Subscribe(handler);

            first.Dispose();
            first.Dispose();
            value.Set(1);

            Assert.That(calls, Is.EqualTo(1), "A second Dispose must not remove the other subscription of the same delegate.");
        }

        [Test]
        public void Subscriber_CanUnsubscribeAndSetDuringNotification()
        {
            var value = new ObservableValue<int>();
            var received = new List<int>();
            IDisposable subscription = null;
            subscription = value.Subscribe(v =>
            {
                received.Add(v);
                subscription.Dispose();
                value.Set(v + 10);
            });

            value.Set(1);

            Assert.That(received, Is.EqualTo(new[] { 1 }));
            Assert.That(value.Value, Is.EqualTo(11));
        }

        [Test]
        public void ThrowingSubscriber_DoesNotStopOthers()
        {
            var value = new ObservableValue<int>();
            int laterCalls = 0;
            value.Subscribe(_ => throw new InvalidOperationException("broken"));
            value.Subscribe(_ => laterCalls++);

            var exception = Assert.Throws<AggregateException>(() => value.Set(1));

            Assert.That(exception.InnerExceptions, Has.Count.EqualTo(1));
            Assert.That(laterCalls, Is.EqualTo(1));
            Assert.That(value.Value, Is.EqualTo(1));
        }

        [Test]
        public void ConcurrentSets_LeaveOneOfTheWrittenValues()
        {
            var value = new ObservableValue<int>();
            int notifications = 0;
            value.Subscribe(_ => System.Threading.Interlocked.Increment(ref notifications));

            Parallel.For(1, 1001, i => value.Set(i));

            Assert.That(value.Value, Is.InRange(1, 1000));
            Assert.That(notifications, Is.GreaterThan(0));
        }

        [Test]
        public void Set_DoesNotAllocate()
        {
            var value = new ObservableValue<int>();
            int sum = 0;
            value.Subscribe(v => sum += v);
            int next = 0;
            TestDelegate set = () => value.Set(++next);

            // Run the exact delegate once first so that JIT and first-call costs are not measured.
            set();

            Assert.That(set, UnityIs.Not.AllocatingGCMemory());
        }
    }
}
