#region Using directives

using System;
using System.IO;
using System.Net;
using System.Threading;

#endregion

namespace ProjectCeleste.GameFiles.GameScanner.ChunkDownloader
{
    /// <summary>
    ///     defines all the chunk download jobs of a download job
    /// </summary>
    public class Chunks
    {
        //constants
        public const int ChunkBufferSize = 8 * Download.Kb;

        public const long ChunkSizeLimit = 10 * Download.Mb;

        /// <summary>
        ///     creates the chunk data repo
        /// </summary>
        /// <param name="chunkSource">url to download the chunk from</param>
        /// <param name="totalSize">total size of all the chunk</param>
        /// <param name="cacheFolder"></param>
        public Chunks(string chunkSource, long totalSize, string cacheFolder)
        {
            //set chunk meta-data
            TotalSize = totalSize;
            ChunkSource = chunkSource;
            ChunkCount = FindChunkCount();
            ChunkSize = ChunkCount != 1 ? ChunkSizeLimit : totalSize;

            //set chunks tracking data
            ChunkProgress = new long[ChunkCount];

            //chunks cache directory
            var chunkTargetDir = Path.Combine(cacheFolder,
                $"{(ChunkSource + ChunkSize + TotalSize).GetHashCode():X4}");
            ChunkTargetTemplate = Path.Combine(chunkTargetDir,
                "{0}.chunk.tmp");

            //create a temp directory for chunks
            if (!Directory.Exists(chunkTargetDir))
                Directory.CreateDirectory(chunkTargetDir);
        }

        //chunk meta-data
        public long ChunkSize { get; }

        public long ChunkCount { get; }
        public string ChunkSource { get; }
        public string ChunkTargetTemplate { get; }

        //chunk tracking data
        public long[] ChunkProgress { get; }

        public long TotalSize { get; }

        /// <summary>
        ///     getter for the chunk target based on chunk's id
        /// </summary>
        /// <param name="chunkId">chunk's id</param>
        /// <returns>chunk's target path</returns>
        internal string ChunkTarget(long chunkId)
        {
            return string.Format(ChunkTargetTemplate, chunkId);
        }

        /// <summary>
        ///     chunk download logic
        /// </summary>
        /// <param name="chunkId">chunk to download</param>
        internal void DownloadChunk(long chunkId)
        {
            //adjust the download range and progress for resume connections
            var chunkStart = ChunkSize * chunkId;
            var chunkEnd = Math.Min(chunkStart + ChunkSize - 1, TotalSize);
            var chunkDownloaded = File.Exists(ChunkTarget(chunkId)) ? new FileInfo(ChunkTarget(chunkId)).Length : 0;
            chunkStart += chunkDownloaded;
            ChunkProgress[chunkId] = chunkDownloaded;

            //check if there is a need to download
            if (chunkStart >= chunkEnd)
                return;

            //prepare the download request
            var dwnlReq = WebRequest.CreateHttp(ChunkSource);
            dwnlReq.AllowAutoRedirect = true;
            dwnlReq.AddRange(chunkStart, chunkEnd);
            dwnlReq.ServicePoint.ConnectionLimit = 100;
            dwnlReq.ServicePoint.Expect100Continue = false;

            try
            {
                //prepare the streams
                using (var dwnlRes = (HttpWebResponse) dwnlReq.GetResponse())
                using (var dwnlSource = dwnlRes.GetResponseStream())
                using (var dwnlTarget = new FileStream(ChunkTarget(chunkId), FileMode.Append, FileAccess.Write))
                {
                    //buffer and downloaded buffer size
                    int bufferedSize;
                    var buffer = new byte[ChunkBufferSize];

                    do
                    {
                        //read the download response async and wait for the results
                        var bufferReader = dwnlSource.ReadAsync(buffer, 0, ChunkBufferSize);
                        bufferReader.Wait();

                        //update buffered size
                        bufferedSize = bufferReader.Result;
                        Interlocked.Add(ref ChunkProgress[chunkId], bufferedSize);

                        //write the buffer to target
                        dwnlTarget.Write(buffer, 0, bufferedSize);
                    } while (bufferedSize > 0);
                }
            }
            finally
            {
                dwnlReq.Abort();
            }
        }

        /// <summary>
        ///     finds the allowed number of chunks
        /// </summary>
        /// <returns>the allowed chunk count</returns>
        private long FindChunkCount()
        {
            //request for finding the number of chunks
            var rangeReq = WebRequest.CreateHttp(ChunkSource);
            rangeReq.AddRange(0, ChunkSizeLimit);
            rangeReq.AllowAutoRedirect = true;

            //returns appropriate number of chunks based on accept-ranges
            using (var rangeRes = (HttpWebResponse) rangeReq.GetResponse())
            {
                if (rangeRes.StatusCode < HttpStatusCode.Redirect &&
                    rangeRes.Headers[HttpResponseHeader.AcceptRanges] == "bytes")
                    return TotalSize / ChunkSizeLimit + (TotalSize % ChunkSizeLimit > 0 ? 1 : 0);
                return 1;
            }
        }
    }
}