#region Using directives

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ProjectCeleste.GameFiles.GameScanner.Utils;

#endregion

namespace ProjectCeleste.GameFiles.GameScanner.FileDownloader
{
    public class SimpleFileDownloader
    {
        public enum DwnlState
        {
            Invalid,
            Create,
            Download,
            Complete,
            Error,
            Abort
        }

        private readonly Stopwatch _stopwatch;

        public SimpleFileDownloader(string httpLink, string outputFileName)
        {
            DwnlSource = httpLink;
            DwnlTarget = outputFileName;
            _stopwatch = new Stopwatch();
        }

        public DwnlState State { get; private set; } = DwnlState.Invalid;

        public double DwnlProgress { get; private set; }

        public long DwnlSize { get; private set; }

        public double DwnlSizeCompleted { get; private set; }

        public string DwnlSource { get; }

        public double DwnlSpeed { get; private set; }

        public string DwnlTarget { get; }

        public Exception Error { get; private set; }

        public async Task StartAndWait(CancellationToken ct = default(CancellationToken))
        {
            using (var webClient = new WebClient())
            {
                State = DwnlState.Create;

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

                //
                State = DwnlState.Download;

                _stopwatch.Reset();

                _stopwatch.Start();

                await webClient.DownloadFileTaskAsync(DwnlSource, DwnlTarget);

                try
                {
                    //
                    while (State < DwnlState.Complete)
                    {
                        using (await new SemaphoreSlim(0, 1).UseWaitAsync(ct))
                        {
                        }
                        ct.ThrowIfCancellationRequested();
                    }
                }
                finally
                {
                    _stopwatch.Stop();
                    webClient.CancelAsync();
                    cancel.Dispose();
                }
                

                // ReSharper disable once SwitchStatementMissingSomeCases
                switch (State)
                {
                    case DwnlState.Error:
                        throw Error;
                    case DwnlState.Abort:
                        throw new OperationCanceledException(ct);
                    case DwnlState.Complete:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(State), State, null);
                }
            }
        }

        private void DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            DwnlSize = e.TotalBytesToReceive;
            DwnlSizeCompleted = e.BytesReceived;
            DwnlProgress = e.ProgressPercentage;
            DwnlSpeed = (double) e.BytesReceived / _stopwatch.Elapsed.Seconds;

            OnProgressChanged();
        }

        private void DownloadCompleted(object sender, AsyncCompletedEventArgs e)
        {
            //
            _stopwatch.Stop();

            //
            if (e.Error != null)
            {
                State = DwnlState.Error;
                Error = e.Error;
            }
            else if (e.Cancelled)
            {
                State = DwnlState.Abort;
                Error = new OperationCanceledException();
            }
            else
            {
                DwnlProgress = 100;
                State = DwnlState.Complete;
                Error = null;
            }

            //
            OnProgressChanged();
        }

        public event EventHandler ProgressChanged;

        protected virtual void OnProgressChanged()
        {
            ProgressChanged?.Invoke(this, null);
        }
    }
}