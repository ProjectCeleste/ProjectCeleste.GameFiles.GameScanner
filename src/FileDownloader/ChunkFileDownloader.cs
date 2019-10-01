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
        private const int ChunkBufferSize = 8 * 1024; //8Kb
        private const int MinChunkSize = 5 * 1024 * 1024; //5Mb
        private const int MaxChunkCount = 10;

        private readonly Stopwatch _stopwatch;
        private readonly string _tmpFolder;
        private long _dwnlSizeCompleted;

        public ChunkFileDownloader(string httpLink, string outputFileName, string tmpFolder)
        {
            DwnlSource = httpLink;
            DwnlTarget = outputFileName;
            _tmpFolder = tmpFolder;
            _stopwatch = new Stopwatch();
            
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.DefaultConnectionLimit = 100;
            ServicePointManager.MaxServicePointIdleTime = 1000;
        }

        public FileDownloaderState State { get; private set; } = FileDownloaderState.Invalid;

        public double DwnlProgress => DwnlSize > 0 ? (double) DwnlSizeCompleted / DwnlSize * 100 : 0;

        public long DwnlSize { get; private set; }

        public long DwnlSizeCompleted => Interlocked.Read(ref _dwnlSizeCompleted);

        public string DwnlSource { get; }

        public double DwnlSpeed => DwnlSize > 0 ? (double) DwnlSizeCompleted / _stopwatch.Elapsed.Seconds : 0;

        public string DwnlTarget { get; }

        public Exception Error { get; private set; }

        public async Task Download(CancellationToken ct = default(CancellationToken))
        {
            if(State == FileDownloaderState.Download)
                return;

            //
            State = FileDownloaderState.Download;

            _dwnlSizeCompleted = 0;

            //
            var path = Path.GetDirectoryName(DwnlTarget);
            if (path != null && !Directory.Exists(path))
                Directory.CreateDirectory(path);

            if (!Directory.Exists(_tmpFolder))
                Directory.CreateDirectory(_tmpFolder);
            //
            try
            {
                _stopwatch.Reset();
                _stopwatch.Start();
                using (new Timer(ReportProgress, new object(), 1000, 1000))
                {
                    //Handle number of parallel downloads
                    var numberOfParallelDownloads = Environment.ProcessorCount > MaxChunkCount
                        ? MaxChunkCount
                        : Environment.ProcessorCount;
                    if (MinChunkSize > (double) DwnlSize / numberOfParallelDownloads)
                        if (DwnlSize > MinChunkSize)
                        {
                            var chunkCount = (double) DwnlSize / MinChunkSize + 1;
                            numberOfParallelDownloads = Environment.ProcessorCount < chunkCount
                                ? Environment.ProcessorCount
                                : (int) chunkCount;
                        }
                        else
                        {
                            numberOfParallelDownloads = 1;
                        }
                    if (numberOfParallelDownloads < 1)
                        numberOfParallelDownloads = 1;

                    //Get file size
                    var webRequest = WebRequest.Create(DwnlSource);
                    webRequest.Method = "HEAD";
                    using (var webResponse = await webRequest.GetResponseAsync())
                    {
                        DwnlSize = long.Parse(webResponse.Headers.Get("Content-Length"));
                    }

                    using (var destinationStream =
                        new FileStream(DwnlTarget, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        //Calculate ranges
                        var readRanges = new List<Range>();
                        for (var chunk = 0; chunk < numberOfParallelDownloads - 1; chunk++)
                        {
                            var range = new Range(chunk * (DwnlSize / numberOfParallelDownloads),
                                (chunk + 1) * (DwnlSize / numberOfParallelDownloads) - 1);
                            readRanges.Add(range);
                        }

                        readRanges.Add(new Range(readRanges.Any() ? readRanges.Last().End + 1 : 0, DwnlSize - 1));

                        //Parallel download
                        var tempFilesDictionary = new ConcurrentDictionary<long, string>();
                        Parallel.ForEach(readRanges,
                            new ParallelOptions {MaxDegreeOfParallelism = numberOfParallelDownloads},
                            async (readRange, state) =>
                            {
                                try
                                {
                                    var dwnlReq = WebRequest.CreateHttp(DwnlSource);
                                    try
                                    {
                                        dwnlReq.AllowAutoRedirect = true;
                                        dwnlReq.AddRange(readRange.Start, readRange.End);
                                        var tempFilePath =
                                            Path.Combine(_tmpFolder,
                                                $"0x{DwnlSource.ToLower().GetHashCode():X4}.0x{readRange.Start:X8}.tmp");
                                        using (var dwnlRes = (HttpWebResponse) await dwnlReq.GetResponseAsync())
                                        using (var dwnlSource = dwnlRes.GetResponseStream())
                                        {
                                            using (var dwnlTarget =
                                                new FileStream(tempFilePath, FileMode.Create, FileAccess.Write,
                                                    FileShare.None))
                                            {
                                                int bufferedSize;
                                                var buffer = new byte[ChunkBufferSize];
                                                do
                                                {
                                                    ct.ThrowIfCancellationRequested();

                                                    //
                                                    bufferedSize =
                                                        await dwnlSource.ReadAsync(buffer, 0, ChunkBufferSize, ct);

                                                    //
                                                    dwnlTarget.Write(buffer, 0, bufferedSize);

                                                    //
                                                    Interlocked.Add(ref _dwnlSizeCompleted, bufferedSize);
                                                } while (bufferedSize > 0);
                                            }
                                            if (!tempFilesDictionary.TryAdd(readRange.Start, tempFilePath))
                                                throw new Exception();
                                        }
                                    }
                                    finally
                                    {
                                        dwnlReq.Abort();
                                    }
                                }
                                catch (Exception)
                                {
                                    state.Break();
                                    throw;
                                }
                            });

                        //Merge to single file
                        foreach (var tempFile in tempFilesDictionary.ToArray().OrderBy(b => b.Key))
                        {
                            var tempFileBytes = File.ReadAllBytes(tempFile.Value);
                            destinationStream.Write(tempFileBytes, 0, tempFileBytes.Length);
                            File.Delete(tempFile.Value);
                        }

                        //
                        State = FileDownloaderState.Complete;
                    }
                }
            }
            catch (OperationCanceledException e)
            {
                //
                Error = e;
                State = FileDownloaderState.Abort;
                throw;
            }
            catch (Exception e)
            {
                //
                Error = e;
                State = FileDownloaderState.Error;
                throw;
            }
            finally
            {
                _stopwatch.Stop();
                OnProgressChanged();
            }
        }

        public event EventHandler ProgressChanged;

        protected virtual void OnProgressChanged()
        {
            ProgressChanged?.Invoke(this, null);
        }

        private void ReportProgress(object state)
        {
            if (!Monitor.TryEnter(state))
                return;

            try
            {
                OnProgressChanged();
            }
            finally
            {
                Monitor.Exit(state);
            }
        }

        internal class Range
        {
            public Range(long start, long end)
            {
                Start = start;
                End = end;
            }

            public long Start { get; }
            public long End { get; }
        }
    }
}