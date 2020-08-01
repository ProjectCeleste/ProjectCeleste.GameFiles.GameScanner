using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Celeste.GameFiles.GameScanner.Configuration;
using ProjectCeleste.GameFiles.GameScanner.FileDownloader;
using ProjectCeleste.GameFiles.GameScanner.Models;
using ProjectCeleste.GameFiles.GameScanner.Utils;
using ProjectCeleste.GameFiles.Tools.Utils;

namespace ProjectCeleste.GameFiles.GameScanner
{
    public sealed class GameScannerManager : IDisposable
    {
        private static readonly string GameScannerTempPath =
            Path.Combine(Path.GetTempPath(), "ProjectCeleste.GameFiles.GameScanner", "Temp");

        private readonly string _filesRootPath;

        private readonly bool _isSteam;

        private bool _scanIsRunning;

        private CancellationTokenSource _cts;

        private IEnumerable<GameFileInfo> _gameFiles;

        public GameScannerManager(bool isSteam = false) : this(GameFiles.GetGameFilesRootPath(), isSteam)
        {
        }

        public GameScannerManager(string filesRootPath, bool isSteam = false)
        {
            if (string.IsNullOrEmpty(filesRootPath))
                throw new ArgumentException("Game files path is null or empty", nameof(filesRootPath));

            _filesRootPath = filesRootPath;
            _isSteam = isSteam;
            _scanIsRunning = false;
        }

        public void Dispose()
        {
            Abort();

            if (_cts != null)
            {
                _cts.Dispose();
                _cts = null;
            }

            CleanUpTmpFolder();
        }

        public async Task InitializeFromCelesteManifest(ManifestConfiguration manifestConfiguration)
        {
            if (_gameFiles?.Any() == true)
                throw new Exception("Game files has already been loaded");

            CleanUpTmpFolder();

            var gameFileInfos =
                (await GameFiles.GameFilesInfoFromCelesteManifest(manifestConfiguration, _isSteam)).GameFileInfo.Select(key => key.Value);
            var fileInfos = gameFileInfos as GameFileInfo[] ?? gameFileInfos.ToArray();
            if (fileInfos.Length == 0)
                throw new ArgumentException("Game files info is null or empty", nameof(gameFileInfos));

            _gameFiles = fileInfos;
        }

        public async Task InitializeFromGameManifest(string type = "production",
            int build = 6148)
        {
            if (_gameFiles?.Any() == true)
                throw new Exception("Game files has already been loaded");

            CleanUpTmpFolder();

            var gameFileInfos =
                (await GameFiles.GameFilesInfoFromGameManifest(type, build, _isSteam)).GameFileInfo.Select(key => key.Value);
            var fileInfos = gameFileInfos as GameFileInfo[] ?? gameFileInfos.ToArray();
            if (fileInfos.Length == 0)
                throw new ArgumentException("Game files info is null or empty", nameof(gameFileInfos));

            _gameFiles = fileInfos;
        }

        public async Task<bool> Scan(bool quick = true, IProgress<ScanProgress> progress = null)
        {
            EnsureInitialized();
            EnsureGameScannerIsNotRunning();

            _scanIsRunning = true;

            var retVal = true;
            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = new CancellationTokenSource();
                var token = _cts.Token;

                var totalSize = _gameFiles.Select(key => key.Size).Sum();
                var currentSize = 0L;
                var index = 0;
                var totalIndex = _gameFiles.Count();
                progress?.Report(new ScanProgress(string.Empty, 0, 0, totalIndex));
                if (quick)
                {
                    Parallel.ForEach(_gameFiles, (fileInfo, state) =>
                    {
                        try
                        {
                            token.ThrowIfCancellationRequested();
                        }
                        catch (OperationCanceledException)
                        {
                            state.Break();
                            throw;
                        }

                        if (!RunFileQuickCheck(Path.Combine(_filesRootPath, fileInfo.FileName), fileInfo.Size))
                        {
                            retVal = false;
                            state.Break();
                        }

                        var currentIndex = Interlocked.Increment(ref index);
                        double newSize = Interlocked.Add(ref currentSize, fileInfo.Size);

                        progress?.Report(new ScanProgress(fileInfo.FileName, newSize / totalSize * 100,
                            currentIndex, totalIndex));
                    });
                }
                else
                {
                    var fileInfos = _gameFiles.ToArray();
                    for (var i = 0; i < fileInfos.Length; i++)
                    {
                        var fileInfo = fileInfos[i];
                        token.ThrowIfCancellationRequested();

                        if (!await RunFileCheck(Path.Combine(_filesRootPath, fileInfo.FileName), fileInfo.Size,
                            fileInfo.Crc32, token))
                            return false;

                        currentSize += fileInfo.Size;

                        progress?.Report(new ScanProgress(fileInfo.FileName, currentSize / totalSize * 100,
                            i + 1, totalIndex));
                    }
                }
            }
            finally
            {
                _scanIsRunning = false;
            }

            return retVal;
        }

        public async Task ScanAndRepair(IProgress<ScanProgress> progress = null, IProgress<ScanSubProgress> subProgress = null)
        {
            EnsureInitialized();
            EnsureGameScannerIsNotRunning();

            _scanIsRunning = true;

            try
            {
                if (_cts != null)
                {
                    _cts.Cancel();
                    _cts.Dispose();
                }

                _cts = new CancellationTokenSource();

                var token = _cts.Token;

                CleanUpTmpFolder();

                var totalSize = _gameFiles.Select(key => key.BinSize).Sum();
                var globalProgress = 0L;
                var totalIndex = _gameFiles.Count();
                var gameFiles = _gameFiles.OrderByDescending(key => key.FileName.Contains("\\"))
                    .ThenBy(key => key.FileName).ToArray();

                for (var i = 0; i < gameFiles.Length; i++)
                {
                    token.ThrowIfCancellationRequested();

                    var fileInfo = gameFiles[i];

                    progress?.Report(new ScanProgress(fileInfo.FileName,
                        (double) globalProgress / totalSize * 100, i, totalIndex));

                    await ScanAndRepairFile(fileInfo, _filesRootPath, subProgress, token);

                    globalProgress += fileInfo.BinSize;
                }
            }
            finally
            {
                await Task.Factory.StartNew(CleanUpTmpFolder);

                _scanIsRunning = false;
            }
        }

        public void Abort()
        {
            if (!_scanIsRunning)
                return;

            if (_cts?.IsCancellationRequested == false)
                _cts.Cancel();
        }

        private void EnsureInitialized()
        {
            if (_gameFiles?.Any() != true)
                throw new Exception("Game scanner has not been initialized or no game files was found");
        }

        private void EnsureGameScannerIsNotRunning()
        {
            if (_scanIsRunning)
                throw new Exception("Scan is already running");
        }

        private static void CleanUpTmpFolder()
        {
            if (!Directory.Exists(GameScannerTempPath))
                return;

            try
            {
                var files = new DirectoryInfo(GameScannerTempPath).GetFiles("*", SearchOption.AllDirectories);

                if (files.Length > 0)
                    Parallel.ForEach(files, file =>
                    {
                        try
                        {
                            File.Delete(file.FullName);
                        }
                        catch (Exception)
                        {
                            //
                        }
                    });

                Directory.Delete(GameScannerTempPath, true);
            }
            catch (Exception)
            {
                //
            }
        }

        #region GameFile

        public static async Task EnsureValidGameFile(string gameFilePath, long expectedFileSize, uint expectedCrc32,
            CancellationToken ct = default, IProgress<double> progress = null)
        {
            var gameFileInfo = new FileInfo(gameFilePath);

            if (!gameFileInfo.Exists)
                throw new Exception($"The game file {gameFilePath} does not exist");

            if (gameFileInfo.Length != expectedFileSize)
                throw new Exception(
                    $"The game file {gameFilePath} was expected to have a size of {expectedFileSize} but was {gameFileInfo.Length}");

            var gameFileCrc32 = await Crc32Utils.ComputeCrc32FromFileAsync(gameFilePath, ct, progress);

            if (gameFileCrc32 != expectedCrc32)
                throw new Exception(
                    $"The game file {gameFilePath} was expected to have a crc32 {expectedCrc32} but was {gameFileCrc32}");
        }

        public static async Task<bool> RunFileCheck(string gameFilePath, long expectedFileSize, uint expectedCrc32,
            CancellationToken ct = default, IProgress<double> progress = null)
        {
            return RunFileQuickCheck(gameFilePath, expectedFileSize) &&
                   expectedCrc32 == await Crc32Utils.ComputeCrc32FromFileAsync(gameFilePath, ct, progress);
        }

        public static bool RunFileQuickCheck(string gameFilePath, long expectedFileSize)
        {
            var fi = new FileInfo(gameFilePath);
            return fi.Exists && fi.Length == expectedFileSize;
        }

        public static async Task ScanAndRepairFile(GameFileInfo fileInfo, string gameFilePath,
            IProgress<ScanSubProgress> progress = null, CancellationToken ct = default)
        {
            var filePath = Path.Combine(gameFilePath, fileInfo.FileName);

            //#1 Check File
            ct.ThrowIfCancellationRequested();
            progress?.Report(new ScanSubProgress(ScanSubProgressStep.Check, 0));

            Progress<double> subProgressCheck = null;
            if (progress != null)
            {
                subProgressCheck = new Progress<double>();
                subProgressCheck.ProgressChanged += (o, d) =>
                {
                    progress.Report(new ScanSubProgress(
                        ScanSubProgressStep.Check, d));
                };
            }

            if (await RunFileCheck(filePath, fileInfo.Size, fileInfo.Crc32, ct, subProgressCheck))
            {
                progress?.Report(new ScanSubProgress(ScanSubProgressStep.End, 100));
            }

            //#2 Download File
            ct.ThrowIfCancellationRequested();
            progress?.Report(new ScanSubProgress(ScanSubProgressStep.Download, 0));

            var tempFileName = Path.Combine(GameScannerTempPath, $"{fileInfo.FileName.GetHashCode():X4}.tmp");
            if (File.Exists(tempFileName))
                File.Delete(tempFileName);

            var fileDownloader = new FileDownloader.FileDownloader(fileInfo.HttpLink, tempFileName);

            if (progress != null)
                fileDownloader.ProgressChanged += (sender, eventArg) =>
                {
                    switch (fileDownloader.State)
                    {
                        case FileDownloaderState.Invalid:
                        case FileDownloaderState.Download:
                            progress.Report(new ScanSubProgress(
                                ScanSubProgressStep.Download, fileDownloader.DownloadProgress * 0.99,
                                new ScanDownloadProgress(fileDownloader.DownloadSize, fileDownloader.BytesDownloaded,
                                    fileDownloader.DownloadSpeed)));
                            break;
                        case FileDownloaderState.Finalize:
                            progress.Report(new ScanSubProgress(
                                ScanSubProgressStep.Download, 99,
                                new ScanDownloadProgress(fileDownloader.DownloadSize, fileDownloader.BytesDownloaded,
                                    0)));
                            break;
                        case FileDownloaderState.Complete:
                            progress.Report(new ScanSubProgress(
                                ScanSubProgressStep.Download, 100,
                                new ScanDownloadProgress(fileDownloader.DownloadSize, fileDownloader.BytesDownloaded,
                                    0)));
                            break;
                        case FileDownloaderState.Error:
                        case FileDownloaderState.Abort:
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(fileDownloader.State),
                                fileDownloader.State, null);
                    }
                };

            await fileDownloader.DownloadAsync(ct);


            //#3 Check Downloaded File
            ct.ThrowIfCancellationRequested();

            Progress<double> subProgressCheckDown = null;
            if (progress != null)
            {
                progress.Report(new ScanSubProgress(ScanSubProgressStep.CheckDownload, 0));

                subProgressCheckDown = new Progress<double>();
                subProgressCheckDown.ProgressChanged += (o, d) =>
                {
                    progress.Report(new ScanSubProgress(
                        ScanSubProgressStep.CheckDownload, d));
                };
            }

            try
            {
                await EnsureValidGameFile(tempFileName, fileInfo.BinSize, fileInfo.BinCrc32, ct, subProgressCheckDown);
            }
            catch
            {
                if (File.Exists(tempFileName))
                    File.Delete(tempFileName);

                throw;
            }

            //#4 Extract downloaded file
            ct.ThrowIfCancellationRequested();
            if (L33TZipUtils.IsL33TZip(tempFileName))
            {
                var tempFileName2 = $"{tempFileName.Replace(".tmp", string.Empty)}.ext.tmp";
                //
                Progress<double> extractProgress = null;
                if (progress != null)
                {
                    progress.Report(new ScanSubProgress(
                        ScanSubProgressStep.ExtractDownload, 0));

                    extractProgress = new Progress<double>();
                    extractProgress.ProgressChanged += (o, d) =>
                    {
                        progress.Report(new ScanSubProgress(
                            ScanSubProgressStep.ExtractDownload, d));
                    };
                }

                await L33TZipUtils.DecompressL33TZipAsync(tempFileName, tempFileName2, extractProgress, ct);

                //#4.1 Check Extracted File
                ct.ThrowIfCancellationRequested();
                Progress<double> subProgressCheckExt = null;
                if (progress != null)
                {
                    progress.Report(new ScanSubProgress(
                        ScanSubProgressStep.CheckExtractDownload, 0));

                    subProgressCheckExt = new Progress<double>();
                    subProgressCheckExt.ProgressChanged += (o, d) =>
                    {
                        progress.Report(new ScanSubProgress(
                            ScanSubProgressStep.CheckExtractDownload, d));
                    };
                }

                await EnsureValidGameFile(tempFileName2, fileInfo.Size, fileInfo.Crc32, ct, subProgressCheckExt);

                File.Delete(tempFileName);

                tempFileName = tempFileName2;
            }

            //#5 Move new file to game folder
            ct.ThrowIfCancellationRequested();

            progress?.Report(new ScanSubProgress(
                ScanSubProgressStep.Finalize, 0));

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
            else
            {
                var pathName = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(pathName) && !Directory.Exists(pathName))
                    Directory.CreateDirectory(pathName);
            }

            File.Move(tempFileName, filePath);

            //#6 End
            progress?.Report(new ScanSubProgress(
                ScanSubProgressStep.End, 100));
        }

        #endregion
    }
}