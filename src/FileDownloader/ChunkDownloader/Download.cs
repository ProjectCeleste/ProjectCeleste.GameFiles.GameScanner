#region Using directives

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;

#endregion

namespace ProjectCeleste.GameFiles.GameScanner.ChunkDownloader
{
    /// <summary>
    ///     defines the download job
    /// </summary>
    public class Download
    {
        //constants
        public const int Kb = 1024;

        public const long Mb = Kb * Kb;

        //
        private long _windowStart;

        /// <summary>
        ///     creates a new download job
        /// </summary>
        /// <param name="dwnlSource">url to download from</param>
        /// <param name="dwnlTarget">url to download to</param>
        /// <param name="cacheFolder"></param>
        public Download(string dwnlSource, string dwnlTarget, string cacheFolder)
        {
            //set the download data
            DwnlSource = dwnlSource;
            DwnlTarget = dwnlTarget;
            DwnlSize = FindFileSize(DwnlSource);

            //create the virtual chunk download jobs and its scheduler
            DwnlChunks = new Chunks(DwnlSource, DwnlSize, cacheFolder);
            DwnlScheduler = new DownloadScheduler(DwnlChunks);
        }

        //download chunk jobs and scheduler
        public Chunks DwnlChunks { get; }

        public DownloadScheduler DwnlScheduler { get; }

        //download file properties
        public string DwnlSource { get; }

        public string DwnlTarget { set; get; }
        public long DwnlSize { get; }

        //download tracking properties
        public double DwnlSizeCompleted { private set; get; }

        public double DwnlSpeed { private set; get; }
        public double DwnlProgress { private set; get; }

        //download append tracking properties
        public double AppendProgress { private set; get; }

        /// <summary>
        ///     updates the download progress
        /// </summary>
        /// <param name="timeSpan">time span of update</param>
        internal void UpdateDownloadProgress(double timeSpan)
        {
            //initial download progress parameters
            double bufferedSize;
            double downloadedSize = DwnlChunks.ChunkSize * _windowStart;
            var chunks = DwnlChunks;

            //adjust the start of the window if completed
            while (_windowStart < chunks.ChunkCount)
            {
                bufferedSize = Interlocked.Read(ref chunks.ChunkProgress[_windowStart]);
                if (bufferedSize == chunks.ChunkSize)
                {
                    _windowStart++;
                    downloadedSize += chunks.ChunkSize;
                }
                else
                {
                    break;
                }
            }

            //update the size of the active chunks
            for (var i = _windowStart; i < chunks.ChunkCount; i++)
            {
                bufferedSize = Interlocked.Read(ref DwnlChunks.ChunkProgress[i]);
                if (bufferedSize != 0)
                    downloadedSize += bufferedSize;
                else
                    break;
            }

            //compute the speed and progress
            DwnlSpeed = Math.Max(0, downloadedSize - DwnlSizeCompleted) / timeSpan;
            DwnlSizeCompleted = downloadedSize;
            DwnlProgress = DwnlSizeCompleted / DwnlSize * 100;
        }

        /// <summary>
        ///     updating the append progress
        /// </summary>
        internal void UpdateAppendProgress()
        {
            AppendProgress = File.Exists(DwnlTarget) ? (double) new FileInfo(DwnlTarget).Length / DwnlSize * 100 : 0;
        }

        /// <summary>
        ///     formats the byte size in KB or MB string
        /// </summary>
        /// <param name="bytes">no of bytes</param>
        /// <returns>bytes in KB or MB</returns>
        public static string FormatBytes(double bytes)
        {
            return bytes < Mb ? $"{bytes / Kb:f2} KB" : $"{bytes / Mb:f2} MB";
        }

        /// <summary>
        ///     finds the download file name from the url
        /// </summary>
        /// <param name="dwnlSource">the file whose name we want to find</param>
        /// <returns>the string with the file name</returns>
        public static string FindFileName(string dwnlSource)
        {
            //prepare the request headers
            var fileNameReq = WebRequest.CreateHttp(dwnlSource);
            fileNameReq.AllowAutoRedirect = true;
            fileNameReq.AddRange(0, Chunks.ChunkSizeLimit);

            using (var fileNameRes = (HttpWebResponse) fileNameReq.GetResponse())
            {
                //get file name markers in the data headers
                var contentType = fileNameRes.ContentType;
                var physicalPath = fileNameRes.ResponseUri.AbsolutePath.Split('/').Last();

                //use contenttype if extension not found
                if (physicalPath.Contains('.'))
                    return physicalPath;
                return physicalPath + "." + contentType.Split('/').Last();
            }
        }

        /// <summary>
        ///     finds the download file size from the url
        /// </summary>
        /// <param name="dwnlSource">url of the file whose file size we want to find</param>
        /// <returns>the download file size in bytes</returns>
        private static long FindFileSize(string dwnlSource)
        {
            //first create a native header request
            var fileSizeReq = WebRequest.CreateHttp(dwnlSource);
            fileSizeReq.AllowAutoRedirect = true;
            using (var fileSizeRes = (HttpWebResponse) fileSizeReq.GetResponse())
            {
                return fileSizeRes.ContentLength;
            }
        }
    }
}