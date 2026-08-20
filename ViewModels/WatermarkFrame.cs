using Newtonsoft.Json;

namespace kuboPix.ViewModels
{
    public class WatermarkFrame
    {
        // Server returns Int64 Id
        [JsonProperty("Id")]
        public long Id { get; set; }

        // Server field is "Name" (LogoName or FrameName)
        [JsonProperty("Name")]
        public string FrameName { get; set; } = string.Empty;

        // Server field is "Image" (LogoImage or FrameImage)
        [JsonProperty("Image")]
        public string FrameImage { get; set; } = string.Empty;

        public int Status { get; set; }

        // Server Type is int: 1 = Frame, 2 = Watermark/Logo
        // [JsonProperty("Type")] maps JSON "Type" field to this int property
        [JsonProperty("Type")]
        public int TypeId { get; set; }

        // Friendly type string used internally ("frame" or "background") — not from JSON
        [JsonIgnore]
        public string Type { get; set; } = "frame";

        // Type int constants matching server values (1 = Frame, 2 = Watermark/Logo)
        public const int TypeFrame = 1;
        public const int TypeWatermark = 2;
    }
}
