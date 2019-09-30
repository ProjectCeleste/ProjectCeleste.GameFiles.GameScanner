#region Using directives

using System;
using System.IO;
using System.Threading;
using System.Windows.Threading;

#endregion

namespace ProjectCeleste.GameFiles.GameScanner.ChunkDownloader
{
    /// <summary>
    ///     download engine executes a download job
    /// </summary>
    public class DownloadEngine
    {
        //engine state properties
        public enum DwnlState
        {
            Invalid,
            Create,
            Idle,
            Start,
            Download,
            Append,
            Complete,
            Error,
            Abort
        }

        //the download the engine operates on
        private readonly Download _download;

        //engine trackers
        private readonly DispatcherTimer _downloadTracker;

        //engine worker
        private Thread _workerThread;

        /// <summary>
        ///     create a new engine for the download
        /// </summary>
        /// <param name="download">download to operate on</param>
        /// <param name="downloadTracker">tracker to monitor the download optional</param>
        public DownloadEngine(Download download, DispatcherTimer downloadTracker = null)
        {
            try
            {
                IsStateCompleted = false;
                State = DwnlState.Create;

                //save the download job
                _download = download;

                //if there is a tracker then add a tick to it
                _downloadTracker = downloadTracker;
                if (downloadTracker != null)
                {
                    _downloadTracker = downloadTracker;
                    _downloadTracker.Tick += DownloadTracker_Tick;
                }
                else
                {
                    _downloadTracker = null;
                }

                IsStateCompleted = false;
                State = DwnlState.Idle;
            }
            catch (Exception e)
            {
                IsStateCompleted = false;
                State = DwnlState.Error;

                Error = e;

                IsStateCompleted = true;
            }
        }

        public DwnlState State { private set; get; }
        public bool IsStateCompleted { private set; get; }
        public Exception Error { private set; get; }

        /// <summary>
        ///     starts the worker thread for the download job
        /// </summary>
        internal void Start()
        {
            if (State != DwnlState.Idle)
                return;

            IsStateCompleted = false;
            State = DwnlState.Start;

            //things to reset before starting
            Error = null;

            //the flow of various tasks within the worker thread
            _workerThread = new Thread(() =>
            {
                try
                {
                    var dir = Path.GetDirectoryName(_download.DwnlTarget);
                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    IsStateCompleted = false;
                    State = DwnlState.Download;

                    _download.DwnlScheduler.Start();

                    IsStateCompleted = false;
                    State = DwnlState.Append;

                    Append();

                    IsStateCompleted = false;
                    State = DwnlState.Complete;

                    Complete();

                    IsStateCompleted = true;
                }
                catch (ThreadAbortException)
                {
                    /* ignore this exception */
                }
                catch (Exception e)
                {
                    IsStateCompleted = false;
                    State = DwnlState.Abort;

                    Abort().Join();

                    IsStateCompleted = false;
                    State = DwnlState.Error;

                    Error = e;

                    IsStateCompleted = true;
                }
            });

            //start the worker thread
            _workerThread.Start();
        }

        /// <summary>
        ///     aborts the download engine async
        /// </summary>
        /// <returns>abort thread upon which we can wait</returns>
        internal Thread Abort()
        {
            IsStateCompleted = false;
            State = DwnlState.Abort;

            //create a new thread to abort the engine
            var abortThread = new Thread(() =>
            {
                _downloadTracker?.Stop();
                if (_workerThread.IsAlive)
                    _download.DwnlScheduler.Abort();

                IsStateCompleted = true;
                State = DwnlState.Idle;
            });

            abortThread.Start();
            return abortThread;
        }

        /// <summary>
        ///     tracks the progress of the download
        /// </summary>
        /// <param name="sender">which requests tracking on the download</param>
        /// <param name="e"></param>
        private void DownloadTracker_Tick(object sender, EventArgs e)
        {
            switch (State)
            {
                case DwnlState.Complete:
                    var timeSpanC = ((DispatcherTimer) sender).Interval;
                    _download.UpdateDownloadProgress(timeSpanC.Seconds + (double) timeSpanC.Milliseconds / 1000);
                    ((DispatcherTimer) sender)?.Stop();
                    break;
                case DwnlState.Download:
                    if (_download.DwnlProgress < 100)
                    {
                        var timeSpan = ((DispatcherTimer) sender).Interval;
                        _download.UpdateDownloadProgress(timeSpan.Seconds + (double) timeSpan.Milliseconds / 1000);
                    }
                    break;
                case DwnlState.Append:
                    if (_download.AppendProgress < 100)
                        _download.UpdateAppendProgress();
                    break;
                case DwnlState.Invalid:
                case DwnlState.Idle:
                case DwnlState.Create:
                case DwnlState.Start:
                    break;
                case DwnlState.Error:
                case DwnlState.Abort:
                    var timeSpanE = ((DispatcherTimer) sender).Interval;
                    _download.UpdateDownloadProgress(timeSpanE.Seconds + (double) timeSpanE.Milliseconds / 1000);
                    ((DispatcherTimer) sender)?.Stop();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(State), State, null);
            }
        }

        /// <summary>
        ///     stitches the chunks together
        /// </summary>
        private void Append()
        {
            //synchronus copy of chunks to target file
            using (var targetFile =
                new BufferedStream(new FileStream(_download.DwnlTarget, FileMode.Create, FileAccess.Write)))
            {
                for (var i = 0; i < _download.DwnlChunks.ChunkCount; i++)
                    using (var sourceChunks = new BufferedStream(File.OpenRead(_download.DwnlChunks.ChunkTarget(i))))
                    {
                        sourceChunks.CopyTo(targetFile);
                    }
            }
            var timeSpanE = _downloadTracker.Interval;
            _download.UpdateDownloadProgress(timeSpanE.Seconds + (double) timeSpanE.Milliseconds / 1000);
            _downloadTracker?.Stop();
        }

        /// <summary>
        ///     performs cleanup jobs
        /// </summary>
        private void Complete()
        {
            //cleanup job 1: delete the chunks
            for (var i = 0; i < _download.DwnlChunks.ChunkCount; i++)
                File.Delete(_download.DwnlChunks.ChunkTarget(i));

            //cleanup job 2: delete the chunk directory
            // ReSharper disable once AssignNullToNotNullAttribute
            Directory.Delete(Path.GetDirectoryName(_download.DwnlChunks.ChunkTarget(0)), true);
        }
    }
}