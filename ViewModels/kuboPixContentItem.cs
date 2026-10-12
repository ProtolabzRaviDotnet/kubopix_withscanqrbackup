using System;
using System.Collections.Generic;

namespace kuboPix.ViewModels
{
    public class kuboPixContentResponse
    {
        public int status { get; set; }
        public string message { get; set; } = string.Empty;
        public List<kuboPixContentItem>? data { get; set; }
    }

    public class kuboPixContentItem
    {
        public int ContentTypeId { get; set; }
        public string ContentTypeName { get; set; } = string.Empty;
        public int PlatformTypeId { get; set; }
        public string? FileName { get; set; }
        public bool HasCustom { get; set; }
        public bool UseCustom { get; set; }
        public bool IsEnabled { get; set; }
        public string? DefaultUrl { get; set; }
        public string? CustomUrl { get; set; }
        public string? CurrentUrl { get; set; }
    }
}
