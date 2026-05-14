using System;

namespace PS2Desktop.Modelos
{
    public class DownloadItem
    {
        public Guid Id { get; set; }
        public Guid? GameId { get; set; }
        public string Url { get; set; }
        public string DirectUrl { get; set; }
        public string FileName { get; set; }
        public long? FileSize { get; set; }
        public string Status { get; set; } = "pending";
        public DateTime CreatedAt { get; set; }
        public string ImageUrl { get; set; }
        public string SavePath { get; set; }
    }
}
