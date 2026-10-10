using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VirtualVessel.Core.Lifecycle;

namespace VirtualVessel.Application.Tests
{
    internal enum FakeBehavior
    {
        Succeed,
        Throw,
        NeverComplete,
    }

    /// <summary>
    /// Records lifecycle calls into a shared journal so tests can assert ordering across services.
    /// </summary>
    internal sealed class FakeService : IApplicationService
    {
        private readonly List<string> _journal;

        public FakeService(string name, List<string> journal, FakeBehavior initializeBehavior = FakeBehavior.Succeed, FakeBehavior shutdownBehavior = FakeBehavior.Succeed)
        {
            Name = name;
            _journal = journal;
            InitializeBehavior = initializeBehavior;
            ShutdownBehavior = shutdownBehavior;
        }

        public string Name { get; }

        public FakeBehavior InitializeBehavior { get; }

        public FakeBehavior ShutdownBehavior { get; }

        public CancellationToken InitializeToken { get; private set; }

        public CancellationToken ShutdownToken { get; private set; }

        public int DisposeCount { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            InitializeToken = cancellationToken;
            _journal.Add($"init:{Name}");
            return Run(InitializeBehavior);
        }

        public Task ShutdownAsync(CancellationToken cancellationToken)
        {
            ShutdownToken = cancellationToken;
            _journal.Add($"shutdown:{Name}");
            return Run(ShutdownBehavior);
        }

        public void Dispose()
        {
            DisposeCount++;
            _journal.Add($"dispose:{Name}");
        }

        private static Task Run(FakeBehavior behavior)
        {
            switch (behavior)
            {
                case FakeBehavior.Throw:
                    return Task.FromException(new InvalidOperationException("Simulated failure."));
                case FakeBehavior.NeverComplete:
                    return new TaskCompletionSource<bool>().Task;
                default:
                    return Task.CompletedTask;
            }
        }
    }
}
