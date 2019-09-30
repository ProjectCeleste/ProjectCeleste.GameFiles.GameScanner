namespace ProjectCeleste.GameFiles.GameScanner.Models
{
    public class ScanAndRepairProgress
    {
        public ScanAndRepairProgress(string title, string description, double progressPercentage,
            ScanAndRepairSubProgress subProgress = null)
        {
            Title = title;
            Description = description;
            ProgressPercentage = progressPercentage;
            SubProgress = subProgress;
        }

        public string Title { get; }

        public string Description { get; }

        public double ProgressPercentage { get; }

        public ScanAndRepairSubProgress SubProgress { get; }
    }

    public enum ScanAndRepairSubProgressStep : byte
    {
        Checking = 0,
        Downloading = 10,
        CheckingDownload = 55,
        ExtractingDownload = 65,
        CheckingExtractedDownload = 85,
        Finalizing = 95,
        End = 100
    }

    public class ScanAndRepairSubProgress
    {
        public ScanAndRepairSubProgress(ScanAndRepairSubProgressStep step, string description,
            double progressPercentage)
        {
            Step = step;
            Description = description;
            ProgressPercentage = progressPercentage;
        }

        public ScanAndRepairSubProgressStep Step { get; }

        public string Description { get; }

        public double ProgressPercentage { get; }
    }
}