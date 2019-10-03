#region Using directives

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace ProjectCeleste.GameFiles.GameScanner.FileDownloader
{
    public class SimpleFileDownloader : IFileDownloader
    {
        private readonly Stopwatch _stopwatch;

        public SimpleFileDownloader(string httpLink, string outputFileName)
        {
            DwnlSource = httpLink;
            DwnlTarget = outputFileName;
            _stopwatch = new Stopwatch();
        }

        public FileDownloaderState State { get; private set; } = FileDownloaderState.Invalid;

        public double DwnlProgress { get; private set; }

        public long DwnlSize { get; private set; }

        public long DwnlSizeCompleted { get; private set; }

        public string DwnlSource { get; }

        public double DwnlSpeed { get; private set; }

        public string DwnlTarget { get; }

        public Exception Error { get; private set; }

        public async Task Download(CancellationToken ct = default(CancellationToken))
        {
            if (State == FileDownloaderState.Download)
                return;

            //
            State = FileDownloaderState.Download;

            var path = Path.GetDirectoryName(DwnlTarget);
            if (path != null && !Directory.Exists(path))
                Directory.CreateDirectory(path);

            using (var webClient = new WebClient())
            {
                //
                webClient.DownloadFileCompleted += DownloadCompleted;
                webClient.DownloadProgressChanged += DownloadProgressChanged;

                //
                var cancel = ct.Register(() =>
                {
                    _stopwatch.Stop();
                    // ReSharper disable once AccessToDisposedClosure
                    webClient.CancelAsync();
                }, true);

                _stopwatch.Reset();
                _stopwatch.Start();
                var timerSync = new object();
                using (new Timer(ReportProgress, timerSync, 500, 500))
                {
                    try
                    {
                        await webClient.DownloadFileTaskAsync(DwnlSource, DwnlTarget);
                        if (DwnlSizeCompleted == DwnlSize)
                            State = FileDownloaderState.Complete;
                    }
                    finally
                    {
                        _stopwatch.Stop();
                        webClient.CancelAsync();
                        cancel.Dispose();
                    }
                }
                // ReSharper disable once SwitchStatementMissingSomeCases
                switch (State)
                {
                    case FileDownloaderState.Error:
                        throw Error;
                    case FileDownloaderState.Abort:
                        throw new OperationCanceledException(ct);
                    case FileDownloaderState.Complete:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(State), State, null);
                }
            }
        }

        public event EventHandler ProgressChanged;

        private void DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            DwnlSize = e.TotalBytesToReceive;
            DwnlSizeCompleted = e.BytesReceived;
            DwnlProgress = e.ProgressPercentage;
            DwnlSpeed = (double) e.BytesReceived / _stopwatch.Elapsed.Seconds;
        }

        private void DownloadCompleted(object sender, AsyncCompletedEventArgs e)
        {
            //
            if (e.Error != null)
            {
                Error = e.Error;
                State = FileDownloaderState.Error;
            }
            else if (e.Cancelled)
            {
                Error = new OperationCanceledException();
                State = FileDownloaderState.Abort;
            }
            else
            {
                Error = null;
                DwnlProgress = 100;
                DwnlSizeCompleted = DwnlSize;
                State = FileDownloaderState.Complete;
            }
        }

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
    }
}