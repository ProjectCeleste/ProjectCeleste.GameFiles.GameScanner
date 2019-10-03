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
        private const int ChunkBufferSize = 32 * 1024; //32Kb
        internal const int ChunkSizeLimit = 10 * 1024 * 1024; //10Mb

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

        public double DwnlSpeed
        {
            get
            {
                double recvBytes = DwnlSizeCompleted;
                return recvBytes > 0 ? recvBytes / ((double) _stopwatch.ElapsedMilliseconds / 1000) : 0;
            }
        }

        public string DwnlTarget { get; }

        public Exception Error { get; private set; }

        public async Task Download(CancellationToken ct = default(CancellationToken))
        {
            if (State == FileDownloaderState.Download)
                return;

            //
            State = FileDownloaderState.Download;
            Interlocked.Exchange(ref _dwnlSizeCompleted, 0);
            _stopwatch.Reset();

            //
            var path = Path.GetDirectoryName(DwnlTarget);
            if (path != null && !Directory.Exists(path))
                Directory.CreateDirectory(path);

            if (!Directory.Exists(_tmpFolder))
                Directory.CreateDirectory(_tmpFolder);
            //
            try
            {
                //
                _stopwatch.Start();

                //
                OnProgressChanged();

                //Get file size
                var webRequest = WebRequest.Create(DwnlSource);
                webRequest.Method = "HEAD";
                using (var webResponse = await webRequest.GetResponseAsync())
                {
                    DwnlSize = long.Parse(webResponse.Headers.Get("Content-Length"));
                }

                //Calculate ranges
                var readRanges = new List<Range>();
                for (var chunkIndex = 0;
                    chunkIndex < DwnlSize / ChunkSizeLimit + (DwnlSize % ChunkSizeLimit > 0 ? 1 : 0);
                    chunkIndex++)
                {
                    var chunkStart = ChunkSizeLimit * chunkIndex;
                    var chunkEnd = Math.Min(chunkStart + ChunkSizeLimit - 1, DwnlSize);

                    readRanges.Add(new Range(chunkStart, chunkEnd));
                }

                //Parallel download
                var timerSync = new object();
                using (new Timer(ReportProgress, timerSync, 500, 500))
                {
                    var tempFilesDictionary = new ConcurrentDictionary<long, string>();
                    var parallel = Parallel.ForEach(readRanges,
                        (readRange, state) =>
                        {
                            try
                            {
                                var dwnlReq = WebRequest.CreateHttp(DwnlSource);
                                dwnlReq.AllowAutoRedirect = true;
                                dwnlReq.AddRange(readRange.Start, readRange.End);
                                dwnlReq.ServicePoint.ConnectionLimit = 100;
                                dwnlReq.ServicePoint.Expect100Continue = false;
                                try
                                {
                                    var tempFilePath =
                                        Path.Combine(_tmpFolder,
                                            $"0x{DwnlSource.ToLower().GetHashCode():X4}.0x{readRange.Start:X8}.tmp");
                                    using (var dwnlRes = (HttpWebResponse) dwnlReq.GetResponse())
                                    using (var dwnlSource = dwnlRes.GetResponseStream())
                                    {
                                        if (dwnlSource == null)
                                            throw new Exception("dwnlSource == null");
                                        using (var dwnlTarget =
                                            new FileStream(tempFilePath, FileMode.Create, FileAccess.Write))
                                        {
                                            int bufferedSize;
                                            var buffer = new byte[ChunkBufferSize];
                                            do
                                            {
                                                //
                                                ct.ThrowIfCancellationRequested();

                                                //
                                                var bufferedRead =
                                                    dwnlSource.ReadAsync(buffer, 0, ChunkBufferSize, ct);
                                                bufferedRead.Wait(ct);
                                                bufferedSize = bufferedRead.Result;

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

                    if (!parallel.IsCompleted)
                        throw new Exception("!parallel.IsCompleted");
                    //
                    _stopwatch.Stop();

                    //
                    if (DwnlSizeCompleted != DwnlSize)
                        throw new Exception("Incomplete download");

                    //
                    State = FileDownloaderState.Finalize;
                    ReportProgress(timerSync); //Forced

                    //Merge to single file
                    using (var targetFile =
                        new BufferedStream(new FileStream(DwnlTarget, FileMode.Create, FileAccess.Write)))
                    {
                        foreach (var tempFile in tempFilesDictionary.ToArray().OrderBy(b => b.Key))
                            using (var sourceChunks = new BufferedStream(File.OpenRead(tempFile.Value)))
                            {
                                sourceChunks.CopyTo(targetFile);
                            }
                    }

                    //
                    State = FileDownloaderState.Complete;
                    ReportProgress(timerSync); //Forced
                }
            }
            catch (Exception e)
            {
                _stopwatch.Stop();
                Error = e;
                State = e is OperationCanceledException ? FileDownloaderState.Abort : FileDownloaderState.Error;
                throw;
            }
            finally
            {
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