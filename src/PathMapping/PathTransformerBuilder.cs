using Celeste.GameFiles.GameScanner.PathMapping.Model;
namespace Celeste.GameFiles.GameScanner.PathMapping
{
    public class PathTransformerBuilder
    {
        private Directory _root;

        public Directory Build(string[] paths)
        {
            _root = new Directory(null);

            foreach (string path in paths)
            {
                RegisterPath(path);
            }

            return _root;
        }

        private void RegisterPath(string path)
        {
            var pathSegments = path.Split('\\');
            var currentDirectory = _root;

            for (int i = 0; i < pathSegments.Length; i++)
            {
                var currentPathSegment = pathSegments[i];

                // Check if we are on the last segment, that means it is the file name
                if (i == pathSegments.Length - 1)
                {
                    currentDirectory.Files.Add(currentPathSegment);
                }
                else // If it isn't, this is the name of a directory
                {
                    // Check if the directory already exists
                    if (currentDirectory.Subdirectories.TryGetValue(currentPathSegment, out var dir))
                    {
                        currentDirectory = dir;
                    }
                    else // Directory not yet discovered/registered
                    {
                        var directory = new Directory(currentPathSegment);
                        currentDirectory.Subdirectories.Add(directory.Name, directory);

                        currentDirectory = directory;
                    }
                }
            }
        }
    }
}
