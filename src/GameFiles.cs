using Celeste.GameFiles.GameScanner.Configuration;
using Newtonsoft.Json;
using ProjectCeleste.GameFiles.GameScanner.Models;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace ProjectCeleste.GameFiles.GameScanner
{
    public static class GameFiles
    {
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

        public static async Task<GameFilesInfo> GameFilesInfoFromGameManifest(string type = "production",
            int build = 6148, bool isSteam = false)
        {
            string txt;
            using (var client = new WebClient())
            {
                txt = await client.DownloadStringTaskAsync(
                    $"http://spartan.msgamestudios.com/content/spartan/{type}/{build}/manifest.txt");
            }

            var retVal = from line in txt.Split(new[] { Environment.NewLine, "\r\n" },
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

            return new GameFilesInfo(new Version(4, 0, 0, 6148), retVal);
        }

        public static async Task<GameFilesInfo> GameFilesInfoFromCelesteManifest(bool isSteam = false, ManifestConfiguration manifestConfiguration)
        {
            //Load default manifest
            var gameFilesInfo = await GameFilesInfoFromGameManifest(isSteam: isSteam);

            //Load Celeste override
            string manifestJsonContents;
            using (var client = new WebClient())
            {
                manifestJsonContents = await client.DownloadStringTaskAsync(manifestConfiguration.GameFilesManifestLocation);
            }

            var gameFilesInfoOverride = JsonConvert.DeserializeObject<GameFilesInfo>(manifestJsonContents);
            gameFilesInfo.Version = gameFilesInfoOverride.Version;
            foreach (var fileInfo in gameFilesInfoOverride.GameFileInfo.Select(key => key.Value))
                gameFilesInfo.GameFileInfo[fileInfo.FileName] = fileInfo;

            //Load xLive override
            string manifestXLiveJsonContents;
            using (var client = new WebClient())
            {
                manifestXLiveJsonContents = await client.DownloadStringTaskAsync(manifestConfiguration.XLiveManifestLocation);
            }

            gameFilesInfo.GameFileInfo["xlive.dll"] =
                JsonConvert.DeserializeObject<GameFileInfo>(manifestXLiveJsonContents);

            //
            return gameFilesInfo;
        }
    }
}
