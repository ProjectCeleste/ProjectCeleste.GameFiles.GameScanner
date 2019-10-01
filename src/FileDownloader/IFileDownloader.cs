#region Using directives

using System;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace ProjectCeleste.GameFiles.GameScanner.FileDownloader
{
    public interface IFileDownloader
    {
        double DwnlProgress { get; }
        long DwnlSize { get; }
        long DwnlSizeCompleted { get; }
        string DwnlSource { get; }
        double DwnlSpeed { get; }
        string DwnlTarget { get; }
        Exception Error { get; }
        FileDownloaderState State { get; }

        event EventHandler ProgressChanged;

        Task Download(CancellationToken ct = default(CancellationToken));
    }
}