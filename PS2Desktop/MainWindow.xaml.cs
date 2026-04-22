using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PS2Desktop.Vistas; // Importante para que reconozca los archivos en esa carpeta

namespace PS2Desktop
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // 1. CARGA INICIAL: Cargamos la vista de inicio al abrir la app
        }

        // --- MÉTODOS DE NAVEGACIÓN ---

        private void BtnHome_Click(object sender, RoutedEventArgs e)
        {
            // Cambiamos el contenido al UserControl "Hogar"
            MainContentFrame.Content = new MainWindow();
            ActualizarEstiloBotones(BtnHome, BtnTemas);
        }

        private void BtnTemas_Click(object sender, RoutedEventArgs e)
        {
            // Cambiamos el contenido al UserControl "TemaView"
            // Al cargar el control
            TemaView vistaTemas = new TemaView();
            vistaTemas.IrADetalle += (s, e) => {
                MainContentFrame.Content = new DetalleTemaView();
            };
            MainContentFrame.Content = vistaTemas;
            ActualizarEstiloBotones(BtnTemas, BtnHome);
        }

        // Método para resaltar el botón activo (Estilo Epic Games)
        private void ActualizarEstiloBotones(Button activo, Button inactivo)
        {
            // Color de fondo para el botón seleccionado (#252A3D)
            activo.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#252A3D"));
            activo.Foreground = Brushes.White;

            // Transparente para el que no está seleccionado
            inactivo.Background = Brushes.Transparent;
            inactivo.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888E9E"));
        }

        // --- CONTROLES DE LA VENTANA ---

        private void Border_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                this.DragMove();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => this.WindowState = WindowState.Minimized;

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
                this.WindowState = WindowState.Normal;
            else
            {
                this.MaxHeight = SystemParameters.MaximizedPrimaryScreenHeight;
                this.WindowState = WindowState.Maximized;
            }
        }

        private void BtnJuegos_Click(object sender, RoutedEventArgs e)
        {
            MainContentFrame.Content = new JuegosView();
            ActualizarEstiloBotones(BtnJuegos, BtnHome);
        }
    }
}