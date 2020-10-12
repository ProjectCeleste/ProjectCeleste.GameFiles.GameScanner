using Celeste.GameFiles.GameScanner.PathMapping;
using NUnit.Framework;

namespace Celeste.GameFiles.GameScanner.Tests
{
    public class PathTransformerTests
    {
        [TestCase("Spartan.exe", ExpectedResult = "Spartan.exe")]
        [TestCase("spartan.exe", ExpectedResult = "Spartan.exe")]
        [TestCase("art\\Art.bar", ExpectedResult = "art/Art.bar")]
        [TestCase("Art\\Art.bar", ExpectedResult = "art/Art.bar")]
        [TestCase("Art\\art.bar", ExpectedResult = "art/Art.bar")]
        [TestCase("art\\art.bar", ExpectedResult = "art/Art.bar")]
        [TestCase("sound\\SoundSets\\Amb.xml", ExpectedResult = "sound/SoundSets/Amb.xml")]
        [TestCase("sound\\SoundSets\\foo.xml", ExpectedResult = "sound/SoundSets/foo.xml")]
        [TestCase("Sound\\SoundSets\\foo.xml", ExpectedResult = "sound/SoundSets/foo.xml")]
        [TestCase("sound\\foo.xml", ExpectedResult = "sound/foo.xml")]
        [TestCase("Sound\\foo.xml", ExpectedResult = "sound/foo.xml")]
        [TestCase("startup\\game.cfg", ExpectedResult = "startup/game.cfg")]
        [TestCase("unknown_file.bin", ExpectedResult = "unknown_file.bin")]
        public string TransformsToExpectedResult(string pathToTransform)
        {
            var originalPathsFromManifest = new string[]
            {
                "Spartan.exe",
                "art\\Art.bar",
                "sound\\SoundSets\\Amb.xml"
            };

            var builder = new PathTransformerBuilder();
            var transformer = new PathTransformer(builder.Build(originalPathsFromManifest));

            return transformer.TransformPath(pathToTransform);
        }
    }
}