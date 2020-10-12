using Celeste.GameFiles.GameScanner.PathMapping;
using FluentAssertions;
using NUnit.Framework;

namespace Celeste.GameFiles.GameScanner.Tests
{
    class PathTransformerBuilderTests
    {
        [Test]
        public void RegistersFileToRootDirectory()
        {
            var dirs = new string[]
            {
                "Spartan.exe"
            };
            
            var builder = new PathTransformerBuilder();
            var tree = builder.Build(dirs);

            tree.Files.Should().ContainSingle(t => t == "Spartan.exe");
            tree.Subdirectories.Should().BeEmpty();
        }

        [Test]
        public void RegistersFileUnderRightRootDirectory()
        {
            var dirs = new string[]
            {
                "art\\Art.bar"
            };
            
            var builder = new PathTransformerBuilder();
            var tree = builder.Build(dirs);

            tree.Files.Should().BeEmpty();
            tree.Subdirectories.Should().ContainKey("art");
            tree.Subdirectories["art"].Files.Should().Contain("Art.bar");
        }

        [Test]
        public void RegistersMultipleFilesUnderRightRootDirectory()
        {
            var dirs = new string[]
            {
                "art\\Art.bar",
                "art\\ArtUI.bar"
            };
            
            var builder = new PathTransformerBuilder();
            var tree = builder.Build(dirs);

            tree.Files.Should().BeEmpty();
            tree.Subdirectories.Should().ContainKey("art");
            tree.Subdirectories["art"].Files.Should().HaveCount(2);
        }

        [Test]
        public void RegistersSubdirectories()
        {
            var dirs = new string[]
            {
                "sound\\amb\\Amb_Map_Ce_Day_1.mp3",
                "sound\\amb\\Amb_Map_Ce_Day_2.mp3"
            };

            var builder = new PathTransformerBuilder();
            var tree = builder.Build(dirs);

            tree.Files.Should().BeEmpty();
            tree.Subdirectories.Should().ContainKey("sound");
            tree.Subdirectories["sound"].Subdirectories.Should().ContainKey("amb");
            tree.Subdirectories["sound"].Subdirectories["amb"].Files.Should().HaveCount(2);
        }
    }
}
