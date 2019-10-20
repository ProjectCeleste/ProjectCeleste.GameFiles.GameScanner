using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ProjectCeleste.GameFiles.GameScanner.FileDownloader.Util
{
    internal static class Parallelism
    {
        internal static Task ForEachAsync<T>(this IEnumerable<T> source, int maxConcurrentTasks, Func<T, Task> body)
        {
            // See https://devblogs.microsoft.com/pfxteam/implementing-a-simple-foreachasync-part-2/
            return Task.WhenAll(
                from partition in Partitioner.Create(source).GetPartitions(maxConcurrentTasks)
                select Task.Run(async delegate {
                    using (partition)
                        while (partition.MoveNext())
                            await body(partition.Current);
                }));
        }
    }
}
