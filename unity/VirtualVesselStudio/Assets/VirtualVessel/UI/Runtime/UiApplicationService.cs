using System;
using System.Threading;
using System.Threading.Tasks;
using VirtualVessel.Core.Lifecycle;

namespace VirtualVessel.UI
{
    /// <summary>
    /// Runs the UI as one application service (UI foundation detailed design 7.9, 10).
    /// </summary>
    /// <remarks>
    /// Composed last, so the UI is built after every other service has started and stops before them.
    /// It is Optional: a UI that fails to build leaves the other services running, and the bootstrap
    /// then shows the startup problem screen.
    /// </remarks>
    public sealed class UiApplicationService : IApplicationService
    {
        public const string ServiceName = "UI";

        private readonly UiRoot _root;
        private readonly UiRootContext _context;

        public UiApplicationService(UiRoot root, UiRootContext context)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            _root = root;
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public string Name => ServiceName;

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _root.Build(_context);
            return Task.CompletedTask;
        }

        public Task ShutdownAsync(CancellationToken cancellationToken)
        {
            // The root can already be destroyed when Unity unloads the scene first.
            return _root != null ? _root.FlushSettingsAsync() : Task.CompletedTask;
        }

        public void Dispose()
        {
            if (_root != null)
            {
                _root.Teardown();
            }
        }
    }
}
