using System.Windows;
using PS2Desktop.Vistas;

namespace PS2Desktop
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            var splash = new AppSplash();
            splash.Show();
        }
    }
}
