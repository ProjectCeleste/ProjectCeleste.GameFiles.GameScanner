namespace Celeste.GameFiles.GameScanner.Configuration
{
    public class ManifestConfiguration
    {
        public string GameFilesManifestLocation { get; set; } = "https://downloads.projectceleste.com/game_files/manifest_override.json";
        public string XLiveManifestLocation { get; set; } = "https://downloads.projectceleste.com/game_files/xlive.json";
    }
}
