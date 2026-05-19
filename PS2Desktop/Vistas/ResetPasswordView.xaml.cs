using Microsoft.Extensions.DependencyInjection;
using PS2Desktop.Services;
using PS2Desktop.Services.Interfaces;
using System;
using System.Windows;
using System.Windows.Media;

namespace PS2Desktop.Vistas
{
    public partial class ResetPasswordView : Window
    {
        private readonly IUserRepository _userRepo;
        private string _email;

        public ResetPasswordView()
        {
            InitializeComponent();
            _userRepo = App.ServiceProvider.GetRequiredService<IUserRepository>();
            Owner = App.Current.MainWindow;
        }

        private async void BtnEnviarCodigo_Click(object sender, RoutedEventArgs e)
        {
            _email = TxtEmail.Text?.Trim();
            if (string.IsNullOrEmpty(_email))
            {
                LblStatus.Text = "Ingresa un correo electrónico.";
                LblStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x35));
                return;
            }

            try
            {
                var user = await _userRepo.GetUserByEmailAsync(_email);
                if (user == null)
                {
                    LblStatus.Text = "No existe una cuenta con ese correo.";
                    LblStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x35));
                    return;
                }

                var token = await _userRepo.GenerateResetTokenAsync(_email);
                if (token == null)
                {
                    LblStatus.Text = "Error al generar el código. Intenta de nuevo.";
                    LblStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x35));
                    return;
                }

                Step1Panel.Visibility = Visibility.Collapsed;
                Step2Panel.Visibility = Visibility.Visible;
                TxtStepDesc.Text = $"Se envió un código a {_email}";
                LblStep2Status.Text = $"Tu código: {token} (válido 15 min)";
                LblStep2Status.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xF2));
            }
            catch (Exception ex)
            {
                LblStatus.Text = "Error: " + ex.Message;
                LblStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x35));
            }
        }

        private async void BtnResetPassword_Click(object sender, RoutedEventArgs e)
        {
            var code = TxtCode.Text?.Trim();
            var newPass = TxtNewPassword.Password;

            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(newPass))
            {
                LblStep2Status.Text = "Completa todos los campos.";
                LblStep2Status.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x35));
                return;
            }

            if (newPass.Length < 6)
            {
                LblStep2Status.Text = "La contraseña debe tener al menos 6 caracteres.";
                LblStep2Status.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x35));
                return;
            }

            try
            {
                var ok = await _userRepo.ResetPasswordAsync(_email, code, newPass);
                if (ok)
                {
                    ToastService.Instance.ShowSuccess("Contraseña restablecida correctamente.");
                    DialogResult = true;
                    Close();
                }
                else
                {
                    LblStep2Status.Text = "Código inválido o expirado.";
                    LblStep2Status.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x35));
                }
            }
            catch (Exception ex)
            {
                LblStep2Status.Text = "Error: " + ex.Message;
                LblStep2Status.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x35));
            }
        }

        private void BtnCerrar_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}