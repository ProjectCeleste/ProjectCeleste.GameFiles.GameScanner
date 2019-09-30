#region Using directives

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using ProjectCeleste.GameFiles.GameScanner.Utils;

#endregion

namespace ProjectCeleste.GameFiles.GameScanner.ChunkDownloader
{
    public class ChunkFileDownloader
    {
        private readonly string _cacheFolder;
        private readonly string _dwnlSource;
        private readonly string _dwnlTarget;
        private Download _download;

        private DownloadEngine _downloadEngine;
        private DispatcherTimer _downloadTracker;

        public ChunkFileDownloader(string dwnlSource, string dwnlTarget, string cacheFolder)
        {
            _dwnlSource = dwnlSource;
            _dwnlTarget = dwnlTarget;
            _cacheFolder = cacheFolder;
        }

        public double AppendProgress => _download?.AppendProgress ?? 0;

        public double DwnlProgress => _download?.DwnlProgress ?? 0;

        public long DwnlSize => _download?.DwnlSize ?? 0;

        public double DwnlSizeCompleted => _download?.DwnlSizeCompleted ?? 0;

        public string DwnlSource => _download?.DwnlSource;

        public double DwnlSpeed => _download?.DwnlSpeed ?? 0;

        public string DwnlTarget => _download?.DwnlTarget;

        public DownloadEngine.DwnlState State => _downloadEngine?.State ?? DownloadEngine.DwnlState.Invalid;

        public Exception Error => _downloadEngine?.Error;

        public async Task StartAndWait(CancellationToken ct = default(CancellationToken))
        {
            if (State != DownloadEngine.DwnlState.Invalid)
                throw new Exception();

            //create a new download job
            _download = new Download(_dwnlSource, _dwnlTarget, _cacheFolder);

            //stop the download tracker and engine if they exist
            _downloadTracker?.Stop();
            _downloadEngine?.Abort().Join();

            //create the tracker, engine
            _downloadTracker = new DispatcherTimer {Interval = TimeSpan.FromSeconds(1)};

            _downloadTracker.Tick += (o, eventArgs) => OnProgressChanged();
            _downloadTracker.Start();

            //create the engine
            _downloadEngine = new DownloadEngine(_download, _downloadTracker);
            _downloadEngine.Start();

            //
            while (State < DownloadEngine.DwnlState.Complete &&
                   (_downloadEngine == null || !_downloadEngine.IsStateCompleted))
                try
                {
                    using (await new SemaphoreSlim(0, 1).UseWaitAsync(ct))
                    {
                    }
                    ct.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException)
                {
                    Abort();
                }

            if (State == DownloadEngine.DwnlState.Complete)
                return;

            // ReSharper disable once SwitchStatementMissingSomeCases
            switch (State)
            {
                case DownloadEngine.DwnlState.Idle:
                    if (Error != null)
                        throw Error;
                    else
                        throw new OperationCanceledException(ct);
                case DownloadEngine.DwnlState.Error:
                    throw Error;
                case DownloadEngine.DwnlState.Abort:
                    throw new OperationCanceledException(ct);
                default:
                    throw new ArgumentOutOfRangeException(nameof(State), State, null);
            }
        }

        public void Pause()
        {
            _downloadEngine.Abort();
        }

        private void Abort()
        {
            _downloadEngine.Abort().Join();
        }

        public async Task ResumeAndWait(CancellationToken ct = default(CancellationToken))
        {
            if (State == DownloadEngine.DwnlState.Invalid)
                throw new Exception();

            _downloadEngine.Start();

            //
            while (State < DownloadEngine.DwnlState.Complete &&
                   (_downloadEngine == null || !_downloadEngine.IsStateCompleted))
                try
                {
                    using (await new SemaphoreSlim(0, 1).UseWaitAsync(ct))
                    {
                    }
                    ct.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException)
                {
                    Abort();
                }

            if (State == DownloadEngine.DwnlState.Complete)
                return;

            // ReSharper disable once SwitchStatementMissingSomeCases
            switch (State)
            {
                case DownloadEngine.DwnlState.Idle:
                    if (Error != null)
                        throw Error;
                    else
                        throw new OperationCanceledException();
                case DownloadEngine.DwnlState.Error:
                    throw Error;
                case DownloadEngine.DwnlState.Abort:
                    throw new OperationCanceledException();
                default:
                    throw new ArgumentOutOfRangeException(nameof(State), State, null);
            }
        }

        public event EventHandler ProgressChanged;

        protected virtual void OnProgressChanged()
        {
            ProgressChanged?.Invoke(this, null);
        }
    }
}