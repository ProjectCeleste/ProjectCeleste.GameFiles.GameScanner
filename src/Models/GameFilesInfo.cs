using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Xml.Serialization;
using Newtonsoft.Json;

namespace ProjectCeleste.GameFiles.GameScanner.Models
{
    [XmlRoot(ElementName = "FileInfo")]
    [JsonObject(Title = "GameFileInfo", Description = "")]
    public class GameFileInfo
    {
        public GameFileInfo()
        {
        }

        [JsonConstructor]
        public GameFileInfo([JsonProperty(PropertyName = "FileName", Required = Required.Always)]
            string fileName,
            [JsonProperty(PropertyName = "CRC32", Required = Required.Always)]
            uint crc32,
            [JsonProperty(PropertyName = "Size", Required = Required.Always)]
            long size,
            [JsonProperty(PropertyName = "HttpLink", Required = Required.Always)]
            string httpLink,
            [JsonProperty(PropertyName = "BinCRC32", Required = Required.Always)]
            uint binCrc32,
            [JsonProperty(PropertyName = "BinSize", Required = Required.Always)]
            long binSize)
        {
            FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
            Crc32 = crc32;
            Size = size;
            HttpLink = httpLink ?? throw new ArgumentNullException(nameof(httpLink));
            BinCrc32 = binCrc32;
            BinSize = binSize;
        }

        [Key]
        [Required(AllowEmptyStrings = false)]
        [JsonProperty(PropertyName = "FileName", Required = Required.Always)]
        [XmlAttribute(AttributeName = "FileName")]
        public string FileName { get; set; }

        [Required]
        [Range(0, uint.MaxValue)]
        [JsonProperty(PropertyName = "CRC32", Required = Required.Always)]
        [XmlAttribute(AttributeName = "CRC32")]
        public uint Crc32 { get; set; }

        [Required]
        [Range(0, long.MaxValue)]
        [JsonProperty(PropertyName = "Size", Required = Required.Always)]
        [XmlAttribute(AttributeName = "Size")]
        public long Size { get; set; }

        [Required(AllowEmptyStrings = false)]
        [JsonProperty(PropertyName = "HttpLink", Required = Required.Always)]
        [XmlAttribute(AttributeName = "HttpLink")]
        public string HttpLink { get; set; }

        [Required]
        [Range(0, uint.MaxValue)]
        [JsonProperty(PropertyName = "BinCRC32", Required = Required.Always)]
        [XmlAttribute(AttributeName = "BinCRC32")]
        public uint BinCrc32 { get; set; }

        [Required]
        [Range(0, long.MaxValue)]
        [JsonProperty(PropertyName = "BinSize", Required = Required.Always)]
        [XmlAttribute(AttributeName = "BinSize")]
        public long BinSize { get; set; }

        public string GetPlatformIndependentFilePath()
        {
            return FileName?.Replace("\\", "/");
        }
    }

    [JsonObject(Title = "GameFilesInfo", Description = "")]
    [XmlRoot(ElementName = "FilesInfo")]
    public class GameFilesInfo
    {
        public GameFilesInfo()
        {
            Version = new GameVersion(4, 0, 0, 6148);
            GameFileInfo = new Dictionary<string, GameFileInfo>(StringComparer.OrdinalIgnoreCase);
        }

        [JsonConstructor]
        public GameFilesInfo([JsonProperty(PropertyName = "Version", Required = Required.Always)]
            GameVersion version,
            [JsonProperty(PropertyName = "GameFileInfo", Required = Required.Always)]
            IEnumerable<GameFileInfo> gameFileInfo)
        {
            Version = version;
            GameFileInfo = (gameFileInfo as GameFileInfo[] ?? gameFileInfo.ToArray()).ToDictionary(key => key.FileName,
                StringComparer.OrdinalIgnoreCase);
        }

        [Required]
        [JsonProperty(PropertyName = "Version", Required = Required.Always)]
        [XmlIgnore]
        public GameVersion Version { get; set; }

        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        [JsonIgnore]
        [XmlAttribute(AttributeName = "Version")]
        public string VersionString
        {
            get => Version.ToString();
            set => Version = new GameVersion(value);
        }

        [JsonIgnore] [XmlIgnore] public IDictionary<string, GameFileInfo> GameFileInfo { get; }

        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        [Required]
        [JsonProperty(PropertyName = "GameFileInfo", Required = Required.Always)]
        [XmlElement(ElementName = "FilesInfo")]
        public GameFileInfo[] GameFileInfoArray
        {
            get => GameFileInfo.Values.ToArray();
            set
            {
                GameFileInfo.Clear();
                if (value == null)
                    return;
                foreach (var item in value)
                    GameFileInfo.Add(item.FileName, item);
            }
        }
    }

    public class GameVersion
    {
        public int Major { get; set; }
        public int Minor { get; set; }
        public int Build { get; set; }
        public int Revision { get; set; }
        public int MajorRevision { get; set; }
        public int MinorRevision { get; set; }

        public GameVersion() { }

        public GameVersion(string val) : this(new Version(val))
        {
        }

        public GameVersion(Version version)
        {
            Major = version.Major;
            Minor = version.Minor;
            Build = version.Build;
            Revision = version.Revision;
        }

        public GameVersion(int major, int minor, int build, int revision)
        {
            Major = major;
            Minor = minor;
            Build = build;
            Revision = revision;
        }

        public Version ToVersion()
        {
            return new Version(Major, Minor, Build, Revision);
        }
    }
}