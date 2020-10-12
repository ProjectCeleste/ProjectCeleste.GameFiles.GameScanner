using System;
using System.Collections.Generic;

namespace Celeste.GameFiles.GameScanner.PathMapping.Model
{

    public class Directory
    {
        public string Name { get; }

        public Dictionary<string, Directory> Subdirectories { get; }
        public List<string> Files { get; }

        public Directory(string name)
        {
            Name = name;
            Subdirectories = new Dictionary<string, Directory>(StringComparer.OrdinalIgnoreCase);
            Files = new List<string>();
        }
    }
}
