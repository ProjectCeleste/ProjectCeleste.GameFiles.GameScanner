using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Force.Crc32;

namespace ProjectCeleste.GameFiles.GameScanner.Utils
{
    public static class Crc32Utils
    {
        public static async Task<uint> ComputeCrc32FromFileAsync(string fileName,
            CancellationToken ct = default,
            IProgress<double> progress = null)
        {
            return await Task.Run(() =>
            {
                if (!File.Exists(fileName))
                    throw new FileNotFoundException($"File '{fileName}' not found!", fileName);

                using (var fs = File.OpenRead(fileName))
                {
                    var result = 0u;
                    var buffer = new byte[4096];
                    int bytesRead;
                    var totalBytesRead = 0L;
                    var fileLength = fs.Length;
                    var percentageCompleted = 0d;

                    while ((bytesRead = fs.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();

                        totalBytesRead += bytesRead;

                        result = Crc32Algorithm.Append(result, buffer, 0, bytesRead);

                        var currentPercentageCompleted = (double)totalBytesRead / fileLength * 100;

                        if (currentPercentageCompleted - percentageCompleted > 1)
                        {
                            progress?.Report(currentPercentageCompleted);
                            percentageCompleted = currentPercentageCompleted;
                        }

                        if (totalBytesRead >= fileLength)
                            break;
                    }

                    return result;
                }
            }, ct);
        }
    }
}