using System;
using System.Threading;
using System.Threading.Tasks;

namespace VirtualVessel.ExternalServices.Process
{
    /// <summary>
    /// Runs a process once and owns it until it ends (external services detailed design 5). Only processes it
    /// started are ever stopped (system design 4.13).
    /// </summary>
    public interface IExternalProcessRunner
    {
        /// <summary>
        /// Runs the request. Each line of standard output reaches <paramref name="onOutputLine"/>, from one
        /// background thread, in order; passing it on to the main thread is the caller's part. Cancelling closes
        /// standard input, waits <see cref="ExternalProcessRequest.CancelGrace"/>, then stops the whole tree.
        /// Failures, including a program that cannot start, come back as a result rather than an exception.
        /// </summary>
        Task<ExternalProcessResult> RunAsync(ExternalProcessRequest request, Action<string> onOutputLine, CancellationToken cancellationToken);
    }
}
