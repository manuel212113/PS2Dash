using System;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Text.Json;
using System.Threading.Tasks;

namespace PS2Desktop.Services
{
    public class EmailService
    {
        private readonly string _smtpHost = "";
        private readonly int _smtpPort = 587;
        private readonly string _smtpUser = "";
        private readonly string _smtpPass = "";
        private readonly string _fromAddress = "noreply@ps2desktop.app";
        private readonly bool _enabled;

        public EmailService()
        {
            try
            {
                var path = AppSettings.AppSettingsPath;
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("Email", out var email))
                    {
                        if (email.TryGetProperty("SmtpHost", out var h)) _smtpHost = h.GetString() ?? "";
                        if (email.TryGetProperty("SmtpPort", out var p)) _smtpPort = p.GetInt32();
                        if (email.TryGetProperty("SmtpUser", out var u)) _smtpUser = u.GetString() ?? "";
                        if (email.TryGetProperty("SmtpPass", out var pw)) _smtpPass = pw.GetString() ?? "";
                        if (email.TryGetProperty("FromAddress", out var fa)) _fromAddress = fa.GetString() ?? _fromAddress;
                    }
                }
                _enabled = !string.IsNullOrEmpty(_smtpHost) && !string.IsNullOrEmpty(_smtpUser);
            }
            catch
            {
                _enabled = false;
            }
        }

        public bool IsEnabled => _enabled;

        public async Task<bool> SendPasswordResetEmailAsync(string toEmail, string resetToken)
        {
            if (!_enabled) return false;

            try
            {
                using var client = new SmtpClient(_smtpHost, _smtpPort)
                {
                    Credentials = new NetworkCredential(_smtpUser, _smtpPass),
                    EnableSsl = true
                };

                var subject = "Recuperación de contraseña - PS2 Desktop";
                var body = $@"
                    <h2>Recuperación de contraseña</h2>
                    <p>Has solicitado restablecer tu contraseña.</p>
                    <p>Tu código de verificación es: <strong>{resetToken}</strong></p>
                    <p>Este código expira en 15 minutos.</p>
                    <p>Si no solicitaste esto, ignora este mensaje.</p>";

                var mail = new MailMessage(_fromAddress, toEmail, subject, body)
                {
                    IsBodyHtml = true
                };

                await client.SendMailAsync(mail);

                LogEmail($"Password reset email sent to {toEmail}");
                return true;
            }
            catch (System.Net.Mail.SmtpException ex)
            {
                LogEmail($"SMTP error sending to {toEmail}: {ex.Message}");
                return false;
            }
            catch (InvalidOperationException ex)
            {
                LogEmail($"Invalid operation sending to {toEmail}: {ex.Message}");
                return false;
            }
        }

        private static void LogEmail(string message)
        {
            try
            {
                var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "emaillog.txt");
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[EmailService] {ex.Message}"); }
        }
    }
}
