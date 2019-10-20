using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectCeleste.GameFiles.GameScanner.FileDownloader
{
    internal class ChunkDownload
    {
        private const int ChunkBufferSize = 32 * 1024; // 32Kb

        private readonly HttpWebRequest _downloadRequest;
        private readonly string _downloadTmpFileName;

        internal ChunkDownload(string fileToDownload, FileRange fileRange, string tmpFolder)
        {

            _downloadRequest = WebRequest.CreateHttp(fileToDownload);
            _downloadRequest.AllowAutoRedirect = true;
            _downloadRequest.AddRange(fileRange.Start, fileRange.End);
            _downloadRequest.ServicePoint.ConnectionLimit = 100;
            _downloadRequest.ServicePoint.Expect100Continue = false;

            _downloadTmpFileName =
                    Path.Combine(tmpFolder,
                        $"0x{fileToDownload.ToLower().GetHashCode():X4}.0x{fileRange.Start:X8}.tmp");
        }

        internal async Task<string> DownloadChunkAsync(CancellationToken ct, Action<int> progressCallback)
        {
            try
            {
                using (var downloadResponse = (HttpWebResponse)_downloadRequest.GetResponse())
                using (var downloadSource = downloadResponse.GetResponseStream())
                using (var downloadTarget = new FileStream(_downloadTmpFileName, FileMode.Create, FileAccess.Write))
                {
                    int bytesRead;
                    var buffer = new byte[ChunkBufferSize];

                    do
                    {
                        ct.ThrowIfCancellationRequested();

                        bytesRead = await downloadSource.ReadAsync(buffer, 0, ChunkBufferSize, ct);
                        downloadTarget.Write(buffer, 0, bytesRead);

                        progressCallback(bytesRead);
                    }
                    while (bytesRead > 0);
                }
            }
            finally
            {
                _downloadRequest.Abort();
            }

            return _downloadTmpFileName;
        }
    }
}
