using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace PS2Desktop.Vistas
{
    /// <summary>
    /// Lógica de interacción para TemaView.xaml
    /// </summary>
    public partial class TemaView : UserControl
    {
        public event EventHandler IrADetalle;

        public TemaView() => InitializeComponent();

        private void btnTemaDetalle_Click(object sender, RoutedEventArgs e)
        {
            // Disparamos el evento hacia afuera
            IrADetalle?.Invoke(this, EventArgs.Empty);
        }
    }
}
