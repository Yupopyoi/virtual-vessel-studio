using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace VirtualVessel.VoiceLab.Activation
{
    /// <summary>Downloads one file into a .part file, continuing where an earlier attempt stopped.</summary>
    public interface IComponentDownloader
    {
        /// <param name="received">Bytes in the part file so far, including what an earlier attempt left.</param>
        /// <exception cref="ComponentDownloadException">The server could not be reached or refused.</exception>
        Task DownloadAsync(string url, string partPath, IProgress<long> received, CancellationToken cancellationToken);
    }

    /// <summary>A download that did not complete; what was received stays in the part file.</summary>
    public sealed class ComponentDownloadException : Exception
    {
        public ComponentDownloadException(string message, Exception innerException = null)
            : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Over HTTPS with HttpClient. A part file that exists is continued with a Range request; a server that ignores it
    /// and sends the whole file again (200) is written from the start.
    /// </summary>
    public sealed class HttpComponentDownloader : IComponentDownloader, IDisposable
    {
        private const int BufferSize = 1 << 20;

        private readonly HttpClient _client;
        private readonly TimeSpan _stall;

        /// <param name="stall">How long no data may come before the download counts as stopped; 60 seconds when null.</param>
        public HttpComponentDownloader(TimeSpan? stall = null)
        {
            _stall = stall ?? TimeSpan.FromSeconds(60);

            // No overall timeout: a 4 GB file takes long. A connection that stops sending is caught by the stall instead.
            _client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            _client.DefaultRequestHeaders.UserAgent.ParseAdd("VirtualVesselStudio-VoiceLab");
        }

        /// <summary>Whether to continue from <paramref name="have"/> bytes given the reply's status. Pure.</summary>
        public static bool Continues(HttpStatusCode status, long have)
        {
            return have > 0 && status == HttpStatusCode.PartialContent;
        }

        public async Task DownloadAsync(string url, string partPath, IProgress<long> received, CancellationToken cancellationToken)
        {
            long have = File.Exists(partPath) ? new FileInfo(partPath).Length : 0L;
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (have > 0)
            {
                request.Headers.Range = new RangeHeaderValue(have, null);
            }

            using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            stall.CancelAfter(_stall);
            try
            {
                using HttpResponseMessage response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stall.Token).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    // The part file is already whole, or longer than the file: the hash check decides.
                    return;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new ComponentDownloadException($"{url} answered {(int)response.StatusCode} {response.ReasonPhrase}.");
                }

                bool append = Continues(response.StatusCode, have);
                have = append ? have : 0L;
                using Stream body = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var file = new FileStream(partPath, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
                var buffer = new byte[BufferSize];
                int read;
                stall.CancelAfter(Timeout.InfiniteTimeSpan);
                while ((read = await ReadOrStallAsync(body, buffer, response, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await file.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                    have += read;
                    received?.Report(have);
                }
            }
            catch (ComponentDownloadException)
            {
                throw;
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                // Stopped by the user. Unity's runtime reports an aborted read as a WebException, not a cancellation.
                cancellationToken.ThrowIfCancellationRequested();
                throw;
            }
            catch (Exception exception) when (IsConnectionFailure(exception))
            {
                // A dropped or stalled connection, whatever type the runtime gives it (HttpRequestException, IOException,
                // and WebException in Unity's runtime); what was received stays for the next try (2026-11-11).
                string why = stall.IsCancellationRequested ? $"no data came for {_stall.TotalSeconds:0} s" : exception.Message;
                throw new ComponentDownloadException($"{url} stopped while downloading: {why}", exception);
            }
        }

        // Unity's runtime does not stop a read for a cancelled token, so a connection that goes quiet would wait forever:
        // the read races a timer, and losing it closes the response, which ends the read (measured 2026-11-11).
        private async Task<int> ReadOrStallAsync(Stream body, byte[] buffer, HttpResponseMessage response, CancellationToken cancellationToken)
        {
            Task<int> read = body.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
            using var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task finished = await Task.WhenAny(read, Task.Delay(_stall, timer.Token)).ConfigureAwait(false);
            if (finished == read)
            {
                timer.Cancel();
                return await read.ConfigureAwait(false);
            }

            response.Dispose();
            _ = read.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);
            cancellationToken.ThrowIfCancellationRequested();
            throw new ComponentDownloadException($"no data came for {_stall.TotalSeconds:0} s");
        }

        /// <summary>Whether an exception from a download is the connection failing, rather than a bug. Pure.</summary>
        public static bool IsConnectionFailure(Exception exception)
        {
            return exception is HttpRequestException || exception is IOException || exception is WebException
                || exception is OperationCanceledException || exception is ObjectDisposedException;
        }

        public void Dispose()
        {
            _client.Dispose();
        }
    }
}
