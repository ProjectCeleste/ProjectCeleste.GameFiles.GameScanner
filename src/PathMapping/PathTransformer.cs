using Celeste.GameFiles.GameScanner.PathMapping.Model;
using System;
using System.Linq;
using System.Text;

namespace Celeste.GameFiles.GameScanner.PathMapping
{
    public class PathTransformer
    {
        private Directory _rootDirectory;

        public PathTransformer(Directory rootDirectory)
        {
            _rootDirectory = rootDirectory;
        }

        public string TransformPath(string path)
        {
            var pathSegments = path.Split('\\');
            var pathBuilder = new StringBuilder();

            var currentDirectoryLevel = _rootDirectory;

            for (int i = 0; i < pathSegments.Length; i++)
            {
                var pathSegment = pathSegments[i];

                if (i == pathSegments.Length - 1) // File level
                {
                    var registeredFileName = currentDirectoryLevel?.Files
                            .SingleOrDefault(t => t.Equals(pathSegment, StringComparison.OrdinalIgnoreCase));

                    if (!string.IsNullOrEmpty(registeredFileName))
                        pathBuilder.Append(registeredFileName);
                    else
                        pathBuilder.Append(pathSegment);
                }
                else // A directory
                {
                    Directory dir = null;
                    if (currentDirectoryLevel?.Subdirectories.TryGetValue(pathSegment, out dir) == true)
                        pathBuilder.Append(dir.Name);
                    else
                        pathBuilder.Append(pathSegment);

                    currentDirectoryLevel = dir;

                    pathBuilder.Append('/');
                }
            }

            return pathBuilder.ToString();
        }
    }
}
