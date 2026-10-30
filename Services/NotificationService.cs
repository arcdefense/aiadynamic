using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace DynamicIsland.Services
{
    public class NotificationItem
    {
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string AppName { get; set; } = "NOTIFICATION";
        public string IconSymbol { get; set; } = "🔔";
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public class NotificationService
    {
        private UserNotificationListener? _listener;
        public event Action<NotificationItem>? NotificationReceived;

        public bool IsListening { get; private set; }

        public async Task InitializeAsync()
        {
            try
            {
                if (UserNotificationListener.Current != null)
                {
                    _listener = UserNotificationListener.Current;
                    var access = await _listener.RequestAccessAsync();
                    if (access == UserNotificationListenerAccessStatus.Allowed)
                    {
                        _listener.NotificationChanged += Listener_NotificationChanged;
                        IsListening = true;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"NotificationService init error: {ex.Message}");
            }
        }

        private void Listener_NotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args)
        {
            if (args.ChangeKind == UserNotificationChangedKind.Added)
            {
                try
                {
                    var notif = sender.GetNotification(args.UserNotificationId);
                    if (notif != null && notif.Notification?.Visual != null)
                    {
                        string appName = notif.AppInfo?.DisplayInfo?.DisplayName ?? "Windows";
                        string title = appName;
                        string body = string.Empty;

                        var binding = notif.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
                        if (binding != null)
                        {
                            var texts = binding.GetTextElements().Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
                            if (texts.Count > 0)
                            {
                                title = texts[0];
                                if (texts.Count > 1)
                                {
                                    body = string.Join(" • ", texts.Skip(1));
                                }
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(body))
                        {
                            var item = new NotificationItem
                            {
                                Title = title,
                                Message = body,
                                AppName = appName.ToUpperInvariant(),
                                IconSymbol = GetAppIconSymbol(appName),
                                Timestamp = DateTime.Now
                            };

                            NotificationReceived?.Invoke(item);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error processing toast: {ex.Message}");
                }
            }
        }

        public void PostManualNotification(string title, string message, string appName = "DYNAMIC ISLAND", string icon = "🔔")
        {
            NotificationReceived?.Invoke(new NotificationItem
            {
                Title = title,
                Message = message,
                AppName = appName.ToUpperInvariant(),
                IconSymbol = icon,
                Timestamp = DateTime.Now
            });
        }

        private static string GetAppIconSymbol(string appName)
        {
            string lower = appName.ToLowerInvariant();
            if (lower.Contains("whatsapp")) return "💬";
            if (lower.Contains("telegram")) return "✈️";
            if (lower.Contains("discord")) return "🎮";
            if (lower.Contains("spotify")) return "🎵";
            if (lower.Contains("chrome") || lower.Contains("edge") || lower.Contains("firefox")) return "🌐";
            if (lower.Contains("mail") || lower.Contains("outlook")) return "✉️";
            if (lower.Contains("download")) return "📥";
            if (lower.Contains("calendar")) return "📅";
            if (lower.Contains("security") || lower.Contains("defender")) return "🛡️";
            return "🔔";
        }
    }
}
