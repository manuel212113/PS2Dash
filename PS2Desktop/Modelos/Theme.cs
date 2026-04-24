using System.Collections.Generic;

namespace PS2Desktop.Modelos
{
    public class Theme
    {
        public System.Guid id { get; set; }
        public string nombre { get; set; }
        public string autor { get; set; }
        public string descripcion { get; set; }
        public List<string> caracteristicas { get; set; }
        public string video_demo { get; set; }
        public string link_descarga { get; set; }
        public string image_url { get; set; }
    }
}
