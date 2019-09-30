#region Using directives

using System;
using System.Threading;

#endregion

namespace ProjectCeleste.GameFiles.GameScanner.ChunkDownloader
{
    public class DownloadScheduler
    {
        //constants
        private const long SchedulerLimit = 10;

        //scheduler data
        private long _nextChunk;

        private Thread[] _schedulerThreads;

        /// <summary>
        ///     initialize the download scheduler
        /// </summary>
        /// <param name="chunks">the chunk download jobs</param>
        public DownloadScheduler(Chunks chunks)
        {
            Chunks = chunks;
        }

        //chunk download jobs and exception
        public Exception Error { private set; get; }

        public Chunks Chunks { get; }

        /// <summary>
        ///     start the scheduler
        /// </summary>
        internal void Start()
        {
            //little clean-up
            Error = null;
            _nextChunk = 0;

            //create new scheduler threads
            _schedulerThreads = new Thread[Math.Min(SchedulerLimit, Chunks.ChunkCount)];
            for (var i = 0; i < _schedulerThreads.Length; i++)
                _schedulerThreads[i] = new Thread(Schedule);

            //start all the scheduler threads
            foreach (var t in _schedulerThreads)
                t.Start();

            //wait for the scheduler threads to finish
            foreach (var t in _schedulerThreads)
            {
                if (t.IsAlive)
                    t.Join();

                if (Error != null)
                    throw Error;
            }
        }

        /// <summary>
        ///     aborts the scheduler threads and waits for them
        /// </summary>
        internal void Abort()
        {
            //abort all the threads
            foreach (var t in _schedulerThreads)
                if (t.IsAlive)
                    t.Abort();

            //wait for all the threads to abort
            foreach (var t in _schedulerThreads)
                if (t.IsAlive)
                    t.Join();
        }

        /// <summary>
        ///     scheduler thread logic
        /// </summary>
        private void Schedule()
        {
            try
            {
                while (true)
                {
                    long currentChunk = -1;
                    lock (Chunks)
                    {
                        //if next chunk available go to it
                        if (_nextChunk < Chunks.ChunkCount)
                            currentChunk = _nextChunk++;
                    }

                    //if ok download the next chunk
                    if (currentChunk != -1 && Error == null)
                        Chunks.DownloadChunk(currentChunk);
                    else
                        break;
                }
            }
            catch (Exception e)
            {
                Error = e;
            }
        }
    }
}