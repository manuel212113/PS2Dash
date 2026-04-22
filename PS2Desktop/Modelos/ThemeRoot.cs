using System.Collections.Generic;

namespace PS2Desktop.Modelos
{
    public class ThemeRoot
    {
        public string comunidad { get; set; }
        public string seccion { get; set; }
        public string url_fuente { get; set; }
        public List<Theme> temas { get; set; }
    }
}
