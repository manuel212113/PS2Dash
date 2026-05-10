using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using PS2Desktop.Modelos;
using PS2Desktop.Services;

namespace PS2Desktop.Vistas
{
    public partial class CrearView : UserControl
    {
        private ObservableCollection<string> _caracteristicas = new ObservableCollection<string>();

        public CrearView()
        {
            InitializeComponent();
            CaracteristicasList.ItemsSource = _caracteristicas;
            RBTema.IsChecked = true;
        }

        private bool IsTemaMode => RBTema.IsChecked == true;

        private void OnModeChanged(object sender, RoutedEventArgs e)
        {
            if (PreviewBorder != null)
                PreviewBorder.Visibility = Visibility.Collapsed;
            if (LblStatus != null)
                LblStatus.Text = "";
        }

        private void AddCaracteristica_Click(object sender, RoutedEventArgs e)
        {
            var text = TxtCaracteristica.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(text) && text != "Escribe y presiona Agregar")
            {
                _caracteristicas.Add(text);
                TxtCaracteristica.Clear();
            }
        }

        private async void OnImageUrlChanged(object sender, TextChangedEventArgs e)
        {
            var url = TxtImageUrl.Text?.Trim();
            if (string.IsNullOrWhiteSpace(url))
            {
                PreviewBorder.Visibility = Visibility.Collapsed;
                return;
            }

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(url, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.EndInit();
                PreviewImage.Source = bitmap;
                PreviewBorder.Visibility = Visibility.Visible;
            }
            catch
            {
                PreviewBorder.Visibility = Visibility.Collapsed;
            }
        }

        private async void BtnCrear_Click(object sender, RoutedEventArgs e)
        {
            var nombre = TxtNombre.Text?.Trim();
            if (string.IsNullOrWhiteSpace(nombre))
            {
                LblStatus.Text = "El nombre es obligatorio";
                LblStatus.Foreground = System.Windows.Media.Brushes.Red;
                return;
            }

            BtnCrear.IsEnabled = false;
            LblStatus.Text = "Guardando...";
            LblStatus.Foreground = System.Windows.Media.Brushes.Gray;

            try
            {
                if (AppState.Db == null)
                {
                    AppState.Db = await PostgresService.FromAppSettingsAsync();
                    await AppState.Db.InitializeAsync();
                }

                if (IsTemaMode)
                {
                    var tema = new Theme
                    {
                        id = Guid.NewGuid(),
                        nombre = nombre,
                        autor = TxtAutor.Text?.Trim(),
                        descripcion = TxtDescripcion.Text?.Trim(),
                        caracteristicas = new List<string>(_caracteristicas),
                        video_demo = string.IsNullOrWhiteSpace(TxtVideoUrl.Text?.Trim()) ? null : TxtVideoUrl.Text.Trim(),
                        link_descarga = string.IsNullOrWhiteSpace(TxtLinkDescarga.Text?.Trim()) ? null : TxtLinkDescarga.Text.Trim(),
                        image_url = string.IsNullOrWhiteSpace(TxtImageUrl.Text?.Trim()) ? null : TxtImageUrl.Text.Trim()
                    };
                    await AppState.Db.CreateThemeAsync(tema);
                    LblStatus.Text = $"✓ Tema «{nombre}» creado correctamente";
                }
                else
                {
                    var juego = new Game
                    {
                        id = Guid.NewGuid(),
                        nombre = nombre,
                        autor = TxtAutor.Text?.Trim(),
                        descripcion = TxtDescripcion.Text?.Trim(),
                        caracteristicas = new List<string>(_caracteristicas),
                        video_demo = string.IsNullOrWhiteSpace(TxtVideoUrl.Text?.Trim()) ? null : TxtVideoUrl.Text.Trim(),
                        link_descarga = string.IsNullOrWhiteSpace(TxtLinkDescarga.Text?.Trim()) ? null : TxtLinkDescarga.Text.Trim(),
                        image_url = string.IsNullOrWhiteSpace(TxtImageUrl.Text?.Trim()) ? null : TxtImageUrl.Text.Trim()
                    };
                    await AppState.Db.CreateGameAsync(juego);
                    LblStatus.Text = $"✓ Juego «{nombre}» creado correctamente";
                }

                LblStatus.Foreground = System.Windows.Media.Brushes.YellowGreen;
                ClearForm();
            }
            catch (Exception ex)
            {
                LblStatus.Text = "Error: " + ex.Message;
                LblStatus.Foreground = System.Windows.Media.Brushes.Red;
            }
            finally
            {
                BtnCrear.IsEnabled = true;
            }
        }

        private async void BtnImportar_Click(object sender, RoutedEventArgs e)
        {
            var json = TxtJsonImport.Text?.Trim();
            if (string.IsNullOrWhiteSpace(json))
            {
                LblStatus.Text = "Pega el JSON primero";
                LblStatus.Foreground = System.Windows.Media.Brushes.Red;
                return;
            }

            BtnImportar.IsEnabled = false;
            LblStatus.Text = "Importando...";
            LblStatus.Foreground = System.Windows.Media.Brushes.Gray;

            try
            {
                List<JsonElement> items;
                json = json.Trim();
                if (json.StartsWith("["))
                    items = JsonSerializer.Deserialize<List<JsonElement>>(json);
                else
                    items = new List<JsonElement> { JsonSerializer.Deserialize<JsonElement>(json) };

                if (AppState.Db == null)
                {
                    AppState.Db = await PostgresService.FromAppSettingsAsync();
                    await AppState.Db.InitializeAsync();
                }

                int ok = 0, err = 0;
                foreach (var item in items)
                {
                    try
                    {
                        var tipo = item.TryGetProperty("tipo", out var t) ? t.GetString() : "tema";
                        var nombre = item.TryGetProperty("nombre", out var n) ? n.GetString() ?? "Sin nombre" : "Sin nombre";

                        if (tipo == "juego")
                        {
                            var juego = new Game
                            {
                                id = Guid.NewGuid(),
                                nombre = nombre,
                                game_id = item.TryGetProperty("game_id", out var gid) ? gid.GetString() : null,
                                autor = item.TryGetProperty("autor", out var a) ? a.GetString() : null,
                                publisher = item.TryGetProperty("publisher", out var pub) ? pub.GetString() : null,
                                descripcion = item.TryGetProperty("descripcion", out var d) ? d.GetString() : null,
                                genero = item.TryGetProperty("genero", out var gen) ? gen.GetString() : null,
                                fecha_lanzamiento = item.TryGetProperty("fecha_lanzamiento", out var fec) ? fec.GetString() : null,
                                region = item.TryGetProperty("region", out var reg) ? reg.GetString() : null,
                                media_type = item.TryGetProperty("media_type", out var med) ? med.GetString() : null,
                                jugadores = item.TryGetProperty("jugadores", out var jug) ? jug.GetString() : null,
                                resolucion = item.TryGetProperty("resolucion", out var res) ? res.GetString() : null,
                                widescreen = item.TryGetProperty("widescreen", out var wide) && wide.GetBoolean(),
                                caracteristicas = item.TryGetProperty("caracteristicas", out var c)
                                    ? JsonSerializer.Deserialize<List<string>>(c.GetRawText()) ?? new List<string>()
                                    : new List<string>(),
                                video_demo = item.TryGetProperty("video_demo", out var v) ? v.GetString() : null,
                                link_descarga = item.TryGetProperty("link_descarga", out var l) ? l.GetString() : null,
                                image_url = item.TryGetProperty("image_url", out var i) ? i.GetString() : null
                            };
                            await AppState.Db.CreateGameAsync(juego);
                        }
                        else
                        {
                            var tema = new Theme
                            {
                                id = Guid.NewGuid(),
                                nombre = nombre,
                                autor = item.TryGetProperty("autor", out var a) ? a.GetString() : null,
                                descripcion = item.TryGetProperty("descripcion", out var d) ? d.GetString() : null,
                                caracteristicas = item.TryGetProperty("caracteristicas", out var c)
                                    ? JsonSerializer.Deserialize<List<string>>(c.GetRawText()) ?? new List<string>()
                                    : new List<string>(),
                                video_demo = item.TryGetProperty("video_demo", out var v) ? v.GetString() : null,
                                link_descarga = item.TryGetProperty("link_descarga", out var l) ? l.GetString() : null,
                                image_url = item.TryGetProperty("image_url", out var i) ? i.GetString() : null
                            };
                            await AppState.Db.CreateThemeAsync(tema);
                        }
                        ok++;
                    }
                    catch
                    {
                        err++;
                    }
                }

                LblStatus.Text = $"✓ Importados {ok} elementos{(err > 0 ? $", {err} errores" : "")}";
                LblStatus.Foreground = System.Windows.Media.Brushes.YellowGreen;
            }
            catch (Exception ex)
            {
                LblStatus.Text = "Error al importar: " + ex.Message;
                LblStatus.Foreground = System.Windows.Media.Brushes.Red;
            }
            finally
            {
                BtnImportar.IsEnabled = true;
            }
        }

        private async void BtnLoadItems_Click(object sender, RoutedEventArgs e)
        {
            BtnLoadItems.IsEnabled = false;
            LblItemCount.Text = "Cargando...";

            try
            {
                if (AppState.Db == null)
                {
                    AppState.Db = await PostgresService.FromAppSettingsAsync();
                    await AppState.Db.InitializeAsync();
                }

                var temas = await AppState.Db.GetThemesAsync();
                var juegos = await AppState.Db.GetGamesAsync();

                var all = new ObservableCollection<ItemEntry>();
                foreach (var t in temas)
                    all.Add(new ItemEntry { Id = t.id, Nombre = t.nombre ?? "Sin nombre", Tipo = "Tema", ImageUrl = t.image_url });
                foreach (var g in juegos)
                    all.Add(new ItemEntry { Id = g.id, Nombre = g.nombre ?? "Sin nombre", Tipo = "Juego", ImageUrl = g.image_url });

                ItemsList.ItemsSource = all;
                LblItemCount.Text = $"{all.Count} elemento{(all.Count != 1 ? "s" : "")}";
            }
            catch (Exception ex)
            {
                LblItemCount.Text = "Error: " + ex.Message;
            }
            finally
            {
                BtnLoadItems.IsEnabled = true;
            }
        }

        private async void BtnEliminarItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.CommandParameter is Guid id)
            {
                var result = MessageBox.Show("¿Eliminar este elemento?", "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes) return;

                try
                {
                    if (AppState.Db == null)
                    {
                        AppState.Db = await PostgresService.FromAppSettingsAsync();
                        await AppState.Db.InitializeAsync();
                    }

                    await AppState.Db.DeleteThemeAsync(id);
                    await AppState.Db.DeleteGameAsync(id);
                    BtnLoadItems_Click(null, null);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void BtnImportarAvatares_Click(object sender, RoutedEventArgs e)
        {
            var json = TxtAvatarJson.Text?.Trim();
            if (string.IsNullOrWhiteSpace(json))
            {
                LblAvatarStatus.Text = "Pega el JSON primero";
                LblAvatarStatus.Foreground = System.Windows.Media.Brushes.Red;
                return;
            }

            BtnImportarAvatares.IsEnabled = false;
            LblAvatarStatus.Text = "Importando avatares...";
            LblAvatarStatus.Foreground = System.Windows.Media.Brushes.Gray;

            try
            {
                List<JsonElement> items;
                json = json.Trim();
                if (json.StartsWith("["))
                    items = JsonSerializer.Deserialize<List<JsonElement>>(json);
                else
                    items = new List<JsonElement> { JsonSerializer.Deserialize<JsonElement>(json) };

                if (AppState.Db == null)
                {
                    AppState.Db = await PostgresService.FromAppSettingsAsync();
                    await AppState.Db.InitializeAsync();
                }

                int ok = 0, err = 0;
                foreach (var item in items)
                {
                    try
                    {
                        var nombre = item.TryGetProperty("nombre", out var n) ? n.GetString()
                                  : item.TryGetProperty("id", out var i) ? i.GetString()
                                  : "Sin nombre";
                        var imageUrl = item.TryGetProperty("image_url", out var u) ? u.GetString()
                                     : item.TryGetProperty("sony_url", out var s) ? s.GetString()
                                     : null;

                        if (!string.IsNullOrEmpty(imageUrl))
                        {
                            await AppState.Db.CreateAvatarAsync(nombre, imageUrl);
                            ok++;
                        }
                        else
                        {
                            err++;
                        }
                    }
                    catch
                    {
                        err++;
                    }
                }

                LblAvatarStatus.Text = $"✓ Importados {ok} avatar{(ok != 1 ? "es" : "")}{(err > 0 ? $", {err} errores" : "")}";
                LblAvatarStatus.Foreground = System.Windows.Media.Brushes.YellowGreen;
                TxtAvatarJson.Clear();
            }
            catch (Exception ex)
            {
                LblAvatarStatus.Text = "Error al importar: " + ex.Message;
                LblAvatarStatus.Foreground = System.Windows.Media.Brushes.Red;
            }
            finally
            {
                BtnImportarAvatares.IsEnabled = true;
            }
        }

        private void ClearForm()
        {
            TxtNombre.Clear();
            TxtAutor.Clear();
            TxtDescripcion.Clear();
            _caracteristicas.Clear();
            TxtImageUrl.Clear();
            TxtVideoUrl.Text = "https://www.youtube.com/watch?v=...";
            TxtLinkDescarga.Clear();
            PreviewBorder.Visibility = Visibility.Collapsed;
        }
    }

    public class ItemEntry
    {
        public Guid Id { get; set; }
        public string Nombre { get; set; }
        public string Tipo { get; set; }
        public string ImageUrl { get; set; }
    }
}
