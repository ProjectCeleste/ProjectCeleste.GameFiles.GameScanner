#region Using directives

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace ProjectCeleste.GameFiles.GameScanner.FileDownloader
{
    public class ChunkFileDownloader : IFileDownloader
    {
        internal const int MaxChunkSize = 10 * 1024 * 1024; //10Mb
        private const int MaxConcurrentDownloads = 12;

        private readonly Stopwatch _downloadSpeedStopwatch;
        private readonly string _downloadTempFolder;

        private ConcurrentDictionary<long, string> _completedChunks = new ConcurrentDictionary<long, string>();
        private ConcurrentQueue<FileRange> _chunkDownloadQueue;

        private long _downloadSizeCompleted;
        private int _activeDownloads = 1;
        private bool _downloadFailed = false;

        public ChunkFileDownloader(string httpLink, string outputFileName, string tmpFolder)
        {
            DownloadUrl = httpLink;
            FilePath = outputFileName;
            _downloadTempFolder = tmpFolder;
            _downloadSpeedStopwatch = new Stopwatch();

            ServicePointManager.Expect100Continue = false;
            ServicePointManager.DefaultConnectionLimit = 100;
            ServicePointManager.MaxServicePointIdleTime = 1000;
        }

        public FileDownloaderState State { get; private set; } = FileDownloaderState.Invalid;

        public double DownloadProgress => DownloadSize > 0 ? (double) BytesDownloaded / DownloadSize * 100 : 0;

        public long DownloadSize { get; private set; }

        public long BytesDownloaded => Interlocked.Read(ref _downloadSizeCompleted);

        public string DownloadUrl { get; }

        public double DownloadSpeed
        {
            get
            {
                double receivedBytes = BytesDownloaded;
                return receivedBytes > 0 ? receivedBytes / ((double) _downloadSpeedStopwatch.ElapsedMilliseconds / 1000) : 0;
            }
        }

        public string FilePath { get; }

        public Exception Error { get; private set; }

        public async Task DownloadAsync(CancellationToken ct = default)
        {
            if (State == FileDownloaderState.Download)
                return;

            State = FileDownloaderState.Download;
            Interlocked.Exchange(ref _downloadSizeCompleted, 0);
            _downloadSpeedStopwatch.Reset();

            var downloadDirectoryName = Path.GetDirectoryName(FilePath);
            if (downloadDirectoryName != null && !Directory.Exists(downloadDirectoryName))
                Directory.CreateDirectory(downloadDirectoryName);

            if (!Directory.Exists(_downloadTempFolder))
                Directory.CreateDirectory(_downloadTempFolder);
            try
            {
                _downloadSpeedStopwatch.Start();

                OnProgressChanged();

                DownloadSize = await GetDownloadSizeAsync();

                var readRanges = CalculateFileChunkRanges();

                //Parallel download
                using (new Timer(ReportProgress, null, 100, 100))
                {
                    await OrchestrateDownloadWorkersAsync(readRanges, ct);
                    _downloadSpeedStopwatch.Stop();

                    if (BytesDownloaded != DownloadSize)
                        throw new Exception($"Download was completed ({BytesDownloaded} bytes), but did not receive expected size of {DownloadSize} bytes");

                    State = FileDownloaderState.Finalize;
                    ReportProgress(null); //Forced

                    WriteChunksToFile(_completedChunks);

                    State = FileDownloaderState.Complete;
                    ReportProgress(null); //Forced
                }
            }
            catch (Exception e)
            {
                _downloadSpeedStopwatch.Stop();
                Error = e;
                State = e is OperationCanceledException ? FileDownloaderState.Abort : FileDownloaderState.Error;
                throw;
            }
            finally
            {
                OnProgressChanged();
            }
        }

        private async Task OrchestrateDownloadWorkersAsync(IEnumerable<FileRange> chunks, CancellationToken ct)
        {
            var downloaderWorkers = new ConcurrentQueue<Task>();
            _chunkDownloadQueue = new ConcurrentQueue<FileRange>(chunks);

            while (_activeDownloads < MaxConcurrentDownloads && _chunkDownloadQueue.Count > 0 && !_downloadFailed)
            {
                _activeDownloads++;

                downloaderWorkers.Enqueue(Task.Run(DequeueAndDownloadChunksAsync, ct));
                await Task.Delay(1000);
            }

            await Task.WhenAll(downloaderWorkers);
        }

        private async Task DequeueAndDownloadChunksAsync()
        {
            var workerFailedDownloading = false;
            var taskId = _activeDownloads;

            while (_chunkDownloadQueue.TryDequeue(out var fileChunk) && !workerFailedDownloading)
            {
                var chunkDownload = new ChunkDownload(DownloadUrl, fileChunk, _downloadTempFolder);
                var downloadSuccesfullyCompleted = await chunkDownload.TryDownloadAsync(IncrementTotalDownloadProgress);

                if (!downloadSuccesfullyCompleted)
                {
                    _chunkDownloadQueue.Enqueue(fileChunk);
                    workerFailedDownloading = true;
                    _downloadFailed = true;
                }
                else
                {
                    _completedChunks.TryAdd(fileChunk.Start, chunkDownload.DownloadTmpFileName);
                }
            }
        }

        private IEnumerable<FileRange> CalculateFileChunkRanges()
        {
            for (var chunkIndex = 0;
                chunkIndex < DownloadSize / MaxChunkSize + (DownloadSize % MaxChunkSize > 0 ? 1 : 0);
                chunkIndex++)
            {
                var chunkStart = MaxChunkSize * chunkIndex;
                var chunkEnd = Math.Min(chunkStart + MaxChunkSize - 1, DownloadSize);

                yield return new FileRange(chunkStart, chunkEnd);
            }
        }

        private async Task<long> GetDownloadSizeAsync()
        {
            var sizeDownloadRequest = WebRequest.Create(DownloadUrl);
            sizeDownloadRequest.Method = "HEAD";

            using (var response = await sizeDownloadRequest.GetResponseAsync())
            {
                return long.Parse(response.Headers.Get("Content-Length"));
            }
        }

        private void WriteChunksToFile(IDictionary<long, string> fileChunks)
        {
            using (var targetFile = new BufferedStream(new FileStream(FilePath, FileMode.Create, FileAccess.Write)))
            {
                foreach (var tempFile in fileChunks.ToArray().OrderBy(b => b.Key))
                {
                    using (var sourceChunks = new BufferedStream(File.OpenRead(tempFile.Value)))
                        sourceChunks.CopyTo(targetFile);

                    File.Delete(tempFile.Value);
                }
            }
        }

        private void IncrementTotalDownloadProgress(int bytesDownloaded)
        {
            Interlocked.Add(ref _downloadSizeCompleted, bytesDownloaded);
        }

        public event EventHandler ProgressChanged;

        protected virtual void OnProgressChanged()
        {
            ProgressChanged?.Invoke(this, null);
        }

        private void ReportProgress(object state)
        {
            OnProgressChanged();
        }
    }
}