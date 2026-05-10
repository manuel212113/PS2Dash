using System;

namespace PS2Desktop.Modelos
{
    public class User
    {
        public Guid id { get; set; }
        public string email { get; set; }
        public string password_hash { get; set; }
        public string? avatar_url { get; set; }
        public string? google_id { get; set; }
        public string? display_name { get; set; }
        public DateTime created_at { get; set; }
    }
}
