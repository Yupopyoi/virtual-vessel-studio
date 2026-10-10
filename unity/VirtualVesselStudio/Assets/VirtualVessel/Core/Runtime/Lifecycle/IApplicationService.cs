using System;
using System.Threading;
using System.Threading.Tasks;

namespace VirtualVessel.Core.Lifecycle
{
    /// <summary>
    /// A service whose lifetime is owned by the application host.
    /// </summary>
    /// <remarks>
    /// <see cref="InitializeAsync"/> is called once at startup. <see cref="ShutdownAsync"/> is called only
    /// for services that initialized successfully, in reverse startup order. <see cref="IDisposable.Dispose"/>
    /// is always called last, even if initialization or shutdown failed.
    /// <para>
    /// Both methods are started on the Unity main thread. When the Editor exits Play Mode, the host
    /// waits for <see cref="ShutdownAsync"/> synchronously on the main thread, so an implementation must
    /// not await work that resumes on the main thread after it starts shutting down. Do main-thread
    /// cleanup synchronously first, then use <c>ConfigureAwait(false)</c> for any remaining waits.
    /// </para>
    /// </remarks>
    public interface IApplicationService : IDisposable
    {
        string Name { get; }

        Task InitializeAsync(CancellationToken cancellationToken);

        Task ShutdownAsync(CancellationToken cancellationToken);
    }
}
