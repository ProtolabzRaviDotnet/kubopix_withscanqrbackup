using System.Collections.Generic;

namespace kuboPix.ViewModels
{
    public class BackgroundItem
    {
        public long Id { get; set; }
        public string BackgroundName { get; set; } = string.Empty;
        public string BackgroundImage { get; set; } = string.Empty;
        public int Status { get; set; }
        public int SequenceOrder { get; set; }
    }

    public class BackgroundApiResponse
    {
        public int Status { get; set; }
        public string Message { get; set; } = string.Empty;
        public bool Enabled { get; set; }
        public List<BackgroundItem> Data { get; set; } = new();
    }
}
