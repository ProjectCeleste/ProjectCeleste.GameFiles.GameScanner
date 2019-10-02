#region Using directives

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using ProjectCeleste.GameFiles.GameScanner.FileDownloader;
using ProjectCeleste.GameFiles.GameScanner.Models;
using ProjectCeleste.GameFiles.Tools.L33TZip;
using ProjectCeleste.GameFiles.Tools.Misc;
using ProjectCeleste.GameFiles.Tools.Xml;

#endregion

namespace ProjectCeleste.GameFiles.GameScanner
{
    public class GameScannnerManager : IDisposable
    {
        private static readonly string GameScannerTempPath =
            Path.Combine(Path.GetTempPath(), "ProjectCeleste.GameFiles.GameScannner", "Temp");

        private readonly string _filesRootPath;

        private readonly bool _isSteam;

        private readonly object _scanLock = new object();

        private readonly bool _useChunkDownloader;

        private CancellationTokenSource _cts;

        private IEnumerable<GameFileInfo> _filesInfo;

        private long _globalProgress;

        public GameScannnerManager(bool useChunkDownloader = false, bool isSteam = false) : this(GetGameFilesRootPath(),
            useChunkDownloader, isSteam)
        {
        }

        public GameScannnerManager(string filesRootPath, bool useChunkDownloader, bool isSteam = false)
        {
            if (string.IsNullOrEmpty(filesRootPath))
                throw new ArgumentException("Game files path is null or empty!", nameof(filesRootPath));

            _filesRootPath = filesRootPath;
            _useChunkDownloader = useChunkDownloader;
            _isSteam = isSteam;
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

        public async Task InitializeFromCelesteManifest(bool isSteam = false)
        {
            if (_filesInfo?.Any() == true)
                throw new Exception("Already Initialized");

            await Task.Factory.StartNew(CleanUpTmpFolder);

            //
            var gameFileInfos = await GameFilesInfoFromCelesteManifest(_isSteam);
            var fileInfos = gameFileInfos as GameFileInfo[] ?? gameFileInfos.ToArray();
            if (!fileInfos.Any())
                throw new ArgumentException("Game files info is null or empty!", nameof(gameFileInfos));

            _filesInfo = fileInfos;
        }

        public async Task InitializeFromGameManifest(string type, int build, bool isSteam = false)
        {
            if (_filesInfo?.Any() == true)
                throw new Exception("Already Initialized");

            await Task.Factory.StartNew(CleanUpTmpFolder);

            //
            var gameFileInfos = await GameFilesInfoFromCelesteManifest(_isSteam);
            var fileInfos = gameFileInfos as GameFileInfo[] ?? gameFileInfos.ToArray();
            if (!fileInfos.Any())
                throw new ArgumentException("Game files info is null or empty!", nameof(gameFileInfos));

            _filesInfo = fileInfos;
        }

        public async Task<bool> Scan(bool quick = true, IProgress<ScanProgress> progress = null)
        {
            if (_filesInfo == null || !_filesInfo.Any())
                throw new Exception("Not Initialized");

            if (!Monitor.TryEnter(_scanLock))
                throw new Exception("Scan already running!");

            var retVal = true;
            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = new CancellationTokenSource();
                var token = _cts.Token;

                //
                await Task.Run(() =>
                {
                    var totalSize = _filesInfo.Select(key => key.Size).Sum();
                    var currentSize = 0L;
                    var index = 0;
                    var totalIndex = _filesInfo.Count();

                    progress?.Report(new ScanProgress(string.Empty, 0, 0, totalIndex));

                    Parallel.ForEach(_filesInfo, async (fileInfo, state) =>
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

                        if (quick && !RunFileQuickCheck(Path.Combine(_filesRootPath, fileInfo.FileName), fileInfo.Size))
                        {
                            retVal = false;
                            state.Break();
                        }
                        else if (!await RunFileCheck(Path.Combine(_filesRootPath, fileInfo.FileName), fileInfo.Size,
                            fileInfo.Crc32, token))
                        {
                            retVal = false;
                            state.Break();
                        }

                        var currentIndex = Interlocked.Increment(ref index);
                        double newSize = Interlocked.Add(ref currentSize, fileInfo.Size);

                        progress?.Report(new ScanProgress(fileInfo.FileName, newSize / totalSize * 100,
                            currentIndex, totalIndex));
                    });
                }, token);
            }
            finally
            {
                Monitor.Exit(_scanLock);
            }

            return retVal;
        }

        public async Task<bool> ScanAndRepair(IProgress<ScanProgress> progress = null,
            IProgress<ScanSubProgress> subProgress = null)
        {
            if (_filesInfo == null || !_filesInfo.Any())
                throw new Exception("Not Initialized");

            if (!Monitor.TryEnter(_scanLock))
                throw new Exception("Scan already running!");

            try
            {
                //
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = new CancellationTokenSource();
                var token = _cts.Token;

                return await Task.Run(async () =>
                {
                    //
                    CleanUpTmpFolder();

                    //
                    var retVal = false;

                    //
                    var totalSize = _filesInfo.Select(key => key.BinSize).Sum();
                    const long currentSize = 0L;
                    _globalProgress = currentSize;
                    var totalIndex = _filesInfo.Count();
                    var filesArray = _filesInfo.OrderByDescending(key => key.FileName.Contains("\\"))
                        .ThenBy(key => key.FileName).ToArray();
                    for (var i = 0; i < filesArray.Length; i++)
                    {
                        token.ThrowIfCancellationRequested();

                        var fileInfo = filesArray[i];

                        retVal = await ScanAndRepairFile(fileInfo, _filesRootPath, _useChunkDownloader, subProgress,
                            _cts.Token);

                        if (!retVal)
                            break;

                        double currentProgress = Interlocked.Add(ref _globalProgress, fileInfo.BinSize);

                        progress?.Report(new ScanProgress(fileInfo.FileName,
                            currentProgress / totalSize * 100, i, totalIndex));
                    }

                    return retVal;
                }, token);
            }
            finally
            {
                await Task.Factory.StartNew(CleanUpTmpFolder);

                Monitor.Exit(_scanLock);
            }
        }

        public void Abort()
        {
            if (!Monitor.IsEntered(_scanLock))
                return;

            if (_cts != null && !_cts.IsCancellationRequested)
                _cts.Cancel();
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

        public static async Task<bool> RunFileCheck(string filePath, long fileSize, uint fileCrc32,
#pragma warning disable IDE0034 // Simplifier l'expression 'default'
            CancellationToken ct = default(CancellationToken),
#pragma warning restore IDE0034 // Simplifier l'expression 'default'
            IProgress<double> progress = null)
        {
            return RunFileQuickCheck(filePath, fileSize) &&
                   fileCrc32 == await Crc32Utils.DoGetCrc32FromFile(filePath, ct, progress);
        }

        public static bool RunFileQuickCheck(string filePath, long fileSize)
        {
            var fi = new FileInfo(filePath);
            return fi.Exists && fi.Length == fileSize;
        }

        public static async Task<bool> ScanAndRepairFile(GameFileInfo fileInfo, string gameFilePath,
            bool useChunkDownloader = false, IProgress<ScanSubProgress> progress = null,
#pragma warning disable IDE0034 // Simplifier l'expression 'default'
            CancellationToken ct = default(CancellationToken))
#pragma warning restore IDE0034 // Simplifier l'expression 'default'
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
                return true;
            }

            //#2 Download File
            ct.ThrowIfCancellationRequested();
            progress?.Report(new ScanSubProgress(ScanSubProgressStep.Download, 0));

            var tempFileName = Path.Combine(GameScannerTempPath, $"{fileInfo.FileName.GetHashCode():X4}.tmp");
            if (File.Exists(tempFileName))
                File.Delete(tempFileName);

            var fileDownloader = useChunkDownloader && fileInfo.BinSize > ChunkFileDownloader.ChunkSizeLimit
                ? new ChunkFileDownloader(fileInfo.HttpLink, tempFileName, GameScannerTempPath)
                : (IFileDownloader) new SimpleFileDownloader(fileInfo.HttpLink, tempFileName);

            if (progress != null)
                fileDownloader.ProgressChanged += (sender, eventArg) =>
                {
                    switch (fileDownloader.State)
                    {
                        case FileDownloaderState.Invalid:
                        case FileDownloaderState.Download:
                            progress.Report(new ScanSubProgress(
                                ScanSubProgressStep.Download, fileDownloader.DwnlProgress * 0.99,
                                new ScanDownloadProgress(fileDownloader.DwnlSize, fileDownloader.DwnlSizeCompleted,
                                    fileDownloader.DwnlSpeed)));
                            break;
                        case FileDownloaderState.Finalize:
                            progress.Report(new ScanSubProgress(
                                ScanSubProgressStep.Download, 99,
                                new ScanDownloadProgress(fileDownloader.DwnlSize, fileDownloader.DwnlSizeCompleted,
                                    0)));
                            break;
                        case FileDownloaderState.Complete:
                            progress.Report(new ScanSubProgress(
                                ScanSubProgressStep.Download, 100,
                                new ScanDownloadProgress(fileDownloader.DwnlSize, fileDownloader.DwnlSizeCompleted,
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

            try
            {
                await fileDownloader.Download(ct);
            }
            catch (Exception e)
            {
                throw new Exception($"Downloaded file '{fileInfo.FileName}' failed!\r\n" +
                                    $"{e.Message}");
            }

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

            if (!await RunFileCheck(tempFileName, fileInfo.BinSize, fileInfo.BinCrc32, ct, subProgressCheckDown))
            {
                if (File.Exists(tempFileName))
                    File.Delete(tempFileName);

                throw new Exception($"Downloaded file '{fileInfo.FileName}' is invalid!");
            }

            //#4 Extract downloaded file
            ct.ThrowIfCancellationRequested();
            if (L33TZipUtils.IsL33TZipFile(tempFileName))
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
                await L33TZipUtils.DoExtractL33TZipFile(tempFileName, tempFileName2, ct, extractProgress);

                //#4.1 Check Extracted File
                ct.ThrowIfCancellationRequested();
                Progress<double> subProgressCheckExt = null;
                if (progress != null)
                {
                    //
                    progress.Report(new ScanSubProgress(
                        ScanSubProgressStep.CheckExtractDownload, 0));

                    subProgressCheckExt = new Progress<double>();
                    subProgressCheckExt.ProgressChanged += (o, d) =>
                    {
                        progress.Report(new ScanSubProgress(
                            ScanSubProgressStep.CheckExtractDownload, d));
                    };
                }

                if (!await RunFileCheck(tempFileName2, fileInfo.Size, fileInfo.Crc32, ct, subProgressCheckExt))
                    throw new Exception($"Extracted file '{fileInfo.FileName}' is invalid!");

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

            return true;
        }

        #endregion

        #region Get GameFilesInfo

        public static string GetGameFilesRootPath()
        {
            {
                //Custom Path 1
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Spartan.exe");
                if (File.Exists(path))
                    return Path.GetDirectoryName(path);

                //Custom Path 2
                path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AOEO", "Spartan.exe");
                if (File.Exists(path))
                    return Path.GetDirectoryName(path);

                //Custom Path 3
                if (Environment.Is64BitOperatingSystem)
                {
                    path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                        "Age Of Empires Online", "Spartan.exe");
                    if (File.Exists(path))
                        return Path.GetDirectoryName(path);
                }

                //Custom Path 4
                path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Age Of Empires Online", "Spartan.exe");
                if (File.Exists(path))
                    return Path.GetDirectoryName(path);

                //Steam 1
                if (Environment.Is64BitOperatingSystem)
                {
                    path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam",
                        "steamapps", "common", "Age Of Empires Online", "Spartan.exe");
                    if (File.Exists(path))
                        return Path.GetDirectoryName(path);
                }

                //Steam 2
                path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam",
                    "steamapps", "common", "Age Of Empires Online", "Spartan.exe");
                if (File.Exists(path))
                    return Path.GetDirectoryName(path);

                //Original Game Path
                path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Local",
                    "Microsoft", "Age Of Empires Online", "Spartan.exe");
                return File.Exists(path)
                    ? Path.GetDirectoryName(path)
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AOEO");
            }
        }

        public static async Task<IEnumerable<GameFileInfo>> GameFilesInfoFromGameManifest(string type = "production",
            int build = 6148, bool isSteam = false)
        {
            string txt;
            using (var client = new WebClient())
            {
                txt = await client.DownloadStringTaskAsync(
                    $"http://spartan.msgamestudios.com/content/spartan/{type}/{build}/manifest.txt");
            }

            var retVal = from line in txt.Split(new[] {Environment.NewLine, "\r\n"},
                    StringSplitOptions.RemoveEmptyEntries)
                where line.StartsWith("+")
                where
                // Launcher
                !line.StartsWith("+AoeOnlineDlg.dll", StringComparison.OrdinalIgnoreCase) &&
                !line.StartsWith("+AoeOnlinePatch.dll", StringComparison.OrdinalIgnoreCase) &&
                !line.StartsWith("+expapply.dll", StringComparison.OrdinalIgnoreCase) &&
                !line.StartsWith("+LauncherLocList.txt", StringComparison.OrdinalIgnoreCase) &&
                !line.StartsWith("+LauncherStrings-de-DE.xml", StringComparison.OrdinalIgnoreCase) &&
                !line.StartsWith("+LauncherStrings-en-US.xml", StringComparison.OrdinalIgnoreCase) &&
                !line.StartsWith("+LauncherStrings-es-ES.xml", StringComparison.OrdinalIgnoreCase) &&
                !line.StartsWith("+LauncherStrings-fr-FR.xml", StringComparison.OrdinalIgnoreCase) &&
                !line.StartsWith("+LauncherStrings-it-IT.xml", StringComparison.OrdinalIgnoreCase) &&
                !line.StartsWith("+LauncherStrings-zh-CHT.xml", StringComparison.OrdinalIgnoreCase) &&
                !line.StartsWith("+AOEOnline.exe.cfg", StringComparison.OrdinalIgnoreCase) &&
                //Beta Launcher
                !line.StartsWith("+Launcher.exe", StringComparison.OrdinalIgnoreCase) &&
                !line.StartsWith("+LauncherReplace.exe", StringComparison.OrdinalIgnoreCase) &&
                !line.StartsWith("+LauncherLocList.txt", StringComparison.OrdinalIgnoreCase) &&
                !line.StartsWith("+AOEO_Privacy.rtf", StringComparison.OrdinalIgnoreCase) &&
                !line.StartsWith("+pw32b.dll", StringComparison.OrdinalIgnoreCase) &&
                //Steam                      
                (!line.StartsWith("+steam_api.dll", StringComparison.OrdinalIgnoreCase) || isSteam &&
                 line.StartsWith("+steam_api.dll", StringComparison.OrdinalIgnoreCase)) &&
                //Junk
                !line.StartsWith("+t3656t4234.tmp", StringComparison.OrdinalIgnoreCase)
                select line.Split('|')
                into lineSplit
                select new GameFileInfo(lineSplit[0].Substring(1, lineSplit[0].Length - 1),
                    Convert.ToUInt32(lineSplit[1]),
                    Convert.ToInt64(lineSplit[2]),
                    $"http://spartan.msgamestudios.com/content/spartan/{type}/{build}/{lineSplit[3]}",
                    Convert.ToUInt32(lineSplit[4]),
                    Convert.ToInt64(lineSplit[5]));

            return retVal;
        }

        public static async Task<IEnumerable<GameFileInfo>> GameFilesInfoFromCelesteManifest(bool isSteam = false)
        {
            //Load default manifest
            var filesInfo = (await GameFilesInfoFromGameManifest("production", 6148, isSteam))
                .ToDictionary(key => key.FileName, StringComparer.OrdinalIgnoreCase);

            //Load Celeste override
            string json;
            using (var client = new WebClient())
            {
                json = await client.DownloadStringTaskAsync(
                    "https://downloads.projectceleste.com/game_files/manifest_override.json");
            }

            var filesInfoOverride = JsonConvert.DeserializeObject<GameFilesInfo>(json).GameFileInfo.ToArray()
                .Select(key => key.Value);

            foreach (var fileInfo in filesInfoOverride)
                if (filesInfo.ContainsKey(fileInfo.FileName))
                    filesInfo[fileInfo.FileName] = fileInfo;
                else
                    filesInfo.Add(fileInfo.FileName, fileInfo);

            return filesInfo.ToArray().Select(key => key.Value);
        }

        #endregion

        #region Create GameFilesInfo Package

        public static async Task CreateUpdatePackage(string inputFolder, string outputFolder,
#pragma warning disable IDE0034 // Simplifier l'expression 'default'
            string baseHttpLink, Version buildId, CancellationToken ct = default(CancellationToken))
#pragma warning restore IDE0034 // Simplifier l'expression 'default'
        {
            var finalOutputFolder = Path.Combine(outputFolder, "bin_override", buildId.ToString());

            if (!baseHttpLink.EndsWith("/"))
                baseHttpLink += "/";
            baseHttpLink = Path.Combine(baseHttpLink, "bin_override", buildId.ToString()).Replace("\\", "/");

            var data = await GenerateGameFilesInfo(inputFolder, finalOutputFolder, baseHttpLink, buildId, ct);

            if (data.GameFileInfo.Count < 1)
                throw new Exception("FileInfo.Count < 1");

            //Json
            var json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(Path.Combine(finalOutputFolder, $"manifest_override-{buildId}.json"), json,
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(outputFolder, "manifest_override.json"), json, Encoding.UTF8);

            //Xml
            var xml = data.SerializeToXml();
            File.WriteAllText(Path.Combine(finalOutputFolder, $"manifest_override-{buildId}.xml"), xml,
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(outputFolder, "manifest_override.xml"), xml, Encoding.UTF8);
        }

        private static async Task<GameFilesInfo> GenerateGameFilesInfo(string inputFolder, string outputFolder,
#pragma warning disable IDE0034 // Simplifier l'expression 'default'
            string baseHttpLink, Version buildId, CancellationToken ct = default(CancellationToken))
#pragma warning restore IDE0034 // Simplifier l'expression 'default'
        {
            if (Directory.Exists(outputFolder))
                Directory.Delete(outputFolder, true);

            Directory.CreateDirectory(outputFolder);

            var newFilesInfo = new List<GameFileInfo>();
            foreach (var file in Directory.GetFiles(inputFolder, "*", SearchOption.AllDirectories))
            {
                //
                ct.ThrowIfCancellationRequested();

                //
                var rootPath = inputFolder;
                if (!rootPath.EndsWith(Path.DirectorySeparatorChar.ToString()))
                    rootPath += Path.DirectorySeparatorChar;

                var fileName = file.Replace(rootPath, string.Empty);

                var newInfo = await GenerateGameFileInfo(file, fileName, outputFolder, baseHttpLink, ct);

                newFilesInfo.Add(newInfo);
            }

            return new GameFilesInfo(buildId, newFilesInfo);
        }

        private static async Task<GameFileInfo> GenerateGameFileInfo(string file, string fileName,
            string outputFolder,
#pragma warning disable IDE0034 // Simplifier l'expression 'default'
            string baseHttpLink, CancellationToken ct = default(CancellationToken))
#pragma warning restore IDE0034 // Simplifier l'expression 'default'
        {
            if (!baseHttpLink.EndsWith("/"))
                baseHttpLink += "/";

            var binFileName = $"{fileName.ToLower().GetHashCode():X4}.bin";
            var outFileName = Path.Combine(outputFolder, binFileName);

            await L33TZipUtils.DoCreateL33TZipFile(file, outFileName, ct);

            var fileInfo = new GameFileInfo(fileName, await Crc32Utils.DoGetCrc32FromFile(file, ct),
                new FileInfo(file).Length, Path.Combine(baseHttpLink, binFileName).Replace("\\", "/"),
                await Crc32Utils.DoGetCrc32FromFile(outFileName, ct), new FileInfo(outFileName).Length);

            return fileInfo;
        }

        #endregion
    }
}