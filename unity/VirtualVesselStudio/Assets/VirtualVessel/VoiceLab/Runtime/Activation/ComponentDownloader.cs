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

        public HttpComponentDownloader()
        {
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

            try
            {
                using HttpResponseMessage response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
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
                while ((read = await body.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await file.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                    have += read;
                    received?.Report(have);
                }
            }
            catch (HttpRequestException exception)
            {
                throw new ComponentDownloadException($"{url} could not be downloaded: {exception.Message}", exception);
            }
            catch (IOException exception) when (!cancellationToken.IsCancellationRequested)
            {
                throw new ComponentDownloadException($"{url} stopped while downloading: {exception.Message}", exception);
            }
        }

        public void Dispose()
        {
            _client.Dispose();
        }
    }
}
