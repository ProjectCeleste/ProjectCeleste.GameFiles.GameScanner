using Newtonsoft.Json;
using ProjectCeleste.GameFiles.GameScanner.Models;
using ProjectCeleste.GameFiles.GameScanner.Utils;
using ProjectCeleste.GameFiles.Tools.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Celeste.GameFiles.GameScanner
{
    public static class Packaging
    {
        public static async Task CreateGameUpdatePackage(string inputFolder, string outputFolder,
            string baseHttpLink, Version buildId, CancellationToken ct = default)
        {
            var finalOutputFolder = Path.Combine(outputFolder, "bin_override", buildId.ToString());

            if (!baseHttpLink.EndsWith("/"))
                baseHttpLink += "/";
            baseHttpLink = Path.Combine(baseHttpLink, "bin_override", buildId.ToString()).Replace("\\", "/");

            var gameFiles = await GenerateGameFilesInfo(inputFolder, finalOutputFolder, baseHttpLink, buildId, ct);

            if (gameFiles.GameFileInfo.Count < 1)
                throw new Exception($"No game files found in {inputFolder}");

            var manifestJsonContents = JsonConvert.SerializeObject(gameFiles, Formatting.Indented);
            File.WriteAllText(Path.Combine(finalOutputFolder, $"manifest_override-{buildId}.json"),
                manifestJsonContents,
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(outputFolder, "manifest_override.json"), manifestJsonContents,
                Encoding.UTF8);
        }

        public static async Task CreateXLiveUpdatePackage(string xLivePath, string outputFolder,
            string baseHttpLink, Version buildId, CancellationToken ct = default)
        {
            var finalOutputFolder = Path.Combine(outputFolder, "xlive", buildId.ToString());

            if (!baseHttpLink.EndsWith("/"))
                baseHttpLink += "/";
            baseHttpLink = Path.Combine(baseHttpLink, "xlive", buildId.ToString()).Replace("\\", "/");

            var xLiveInfo = await GenerateGameFileInfo(xLivePath, "xlive.dll", finalOutputFolder, baseHttpLink, ct);

            var manifestJsonContents = JsonConvert.SerializeObject(xLiveInfo, Formatting.Indented);
            File.WriteAllText(Path.Combine(finalOutputFolder, $"xlive-{buildId}.json"), manifestJsonContents,
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(outputFolder, "xlive.json"), manifestJsonContents, Encoding.UTF8);
        }

        public static async Task<GameFilesInfo> GenerateGameFilesInfo(string inputFolder, string outputFolder,
            string baseHttpLink, Version buildId, CancellationToken ct = default)
        {
            if (Directory.Exists(outputFolder))
                Directory.Delete(outputFolder, true);

            Directory.CreateDirectory(outputFolder);

            var newFilesInfo = new List<GameFileInfo>();
            foreach (var file in Directory.GetFiles(inputFolder, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();

                var rootPath = inputFolder;
                if (!rootPath.EndsWith(Path.DirectorySeparatorChar.ToString()))
                    rootPath += Path.DirectorySeparatorChar;

                var fileName = file.Replace(rootPath, string.Empty);

                var newInfo = await GenerateGameFileInfo(file, fileName, outputFolder, baseHttpLink, ct);

                newFilesInfo.Add(newInfo);
            }

            return new GameFilesInfo(buildId, newFilesInfo);
        }

        public static async Task<GameFileInfo> GenerateGameFileInfo(string file, string fileName,
            string outputFolder, string baseHttpLink, CancellationToken ct = default)
        {
            if (!Directory.Exists(outputFolder))
                Directory.CreateDirectory(outputFolder);

            if (!baseHttpLink.EndsWith("/"))
                baseHttpLink += "/";

            var binFileName = $"{fileName.ToLower().GetHashCode():X4}.bin";
            var outFileName = Path.Combine(outputFolder, binFileName);

            await L33TZipUtils.CompressFileAsL33TZipAsync(file, outFileName, ct, null);

            var fileCrc = await Crc32Utils.ComputeCrc32FromFileAsync(file, ct);
            var fileLength = new FileInfo(file).Length;

            var externalLocation = Path.Combine(baseHttpLink, binFileName).Replace("\\", "/");
            var outFileCrc = await Crc32Utils.ComputeCrc32FromFileAsync(outFileName, ct);
            var outFileLength = new FileInfo(outFileName).Length;

            return new GameFileInfo(fileName, fileCrc, fileLength, externalLocation,
                outFileCrc, outFileLength);
        }
    }
}
