using System.Diagnostics;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PCVolumeMqtt;

internal static class Installer
{
    private const string RunKey = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run";
    private const string UninstallKey = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\PCVolumeMqtt";

    internal static void RegisterApplication()
    {
        using (var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true))
        {
            run?.SetValue("PCVolumeMqtt", Application.ExecutablePath);
        }

        using (var uninstall = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            uninstall?.SetValue("DisplayName", "PC Volume MQTT");
            uninstall?.SetValue("DisplayIcon", Application.ExecutablePath);
            uninstall?.SetValue("Publisher", "pc-ha-control");
            uninstall?.SetValue("UninstallString", $"\"{Application.ExecutablePath}\" --uninstall");
        }
    }

    internal static void UninstallApplication()
    {
        using (var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true))
        {
            run?.DeleteValue("PCVolumeMqtt", false);
        }
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);

        var exe = Application.ExecutablePath;
        var cmd = $"/C timeout 2 && del \"{exe}\"";
        Process.Start(new ProcessStartInfo("cmd.exe", cmd) { CreateNoWindow = true });
    }
}
