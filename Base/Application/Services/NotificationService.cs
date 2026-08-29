namespace Base.Services;

using Base.Core;
using Microsoft.Win32;
using System.IO;
using System.Security;
using System.Windows;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

public enum NotificationLevel { Info, Warning, Error }

public class NotificationService : WpfBehaviourSingleton<NotificationService>
{
    private const string AppId = "ModernTools.App";
    private string _iconPath = string.Empty;

    public override void Awake()
    {
        RegisterAumid();
        ExtractIcon();
        base.Awake();
    }

    private static void RegisterAumid()
    {
        using var key = Registry.CurrentUser.CreateSubKey($@"SOFTWARE\Classes\AppUserModelId\{AppId}");
        key.SetValue("DisplayName", "ModernTools");
    }

    private void ExtractIcon()
    {
        var iconDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ModernTools");
        Directory.CreateDirectory(iconDir);
        _iconPath = Path.Combine(iconDir, "toast-icon.png");

        if (File.Exists(_iconPath)) return;

        try
        {
            var uri = new Uri("pack://application:,,,/Base;component/Assets/Icons/AppIcon_128.png");
            var resource = Application.GetResourceStream(uri);
            if (resource != null)
            {
                using var fs = File.Create(_iconPath);
                resource.Stream.CopyTo(fs);
            }
        }
        catch { }
    }

    /// <summary>
    /// Shows a Windows 11 toast notification.
    /// </summary>
    /// <param name="title">Notification title.</param>
    /// <param name="body">Notification body text.</param>
    /// <param name="level">Severity level — affects title prefix and sound.</param>
    /// <param name="longDuration">True for ~25s display, false (default) for ~5s.</param>
    public void Show(string title, string body,
        NotificationLevel level = NotificationLevel.Info,
        bool longDuration = false)
    {
        var (prefix, sound) = level switch
        {
            NotificationLevel.Warning => ("⚠ ", "ms-winsoundevent:Notification.Mail"),
            NotificationLevel.Error   => ("✕ ", "ms-winsoundevent:Notification.IM"),
            _                         => ("",   "ms-winsoundevent:Notification.Default"),
        };

        var duration = longDuration ? "long" : "short";
        var logoXml = File.Exists(_iconPath)
            ? $@"<image placement=""appLogoOverride"" src=""file:///{_iconPath.Replace('\\', '/')}"" hint-crop=""circle""/>"
            : string.Empty;

        var xml = $"""
            <toast duration="{duration}">
              <visual>
                <binding template="ToastGeneric">
                  <text>{SecurityElement.Escape(prefix + title)}</text>
                  <text>{SecurityElement.Escape(body)}</text>
                  {logoXml}
                </binding>
              </visual>
              <audio src="{sound}" loop="false"/>
            </toast>
            """;

        var doc = new XmlDocument();
        doc.LoadXml(xml);
        ToastNotificationManager.CreateToastNotifier(AppId).Show(new ToastNotification(doc));
    }
}
