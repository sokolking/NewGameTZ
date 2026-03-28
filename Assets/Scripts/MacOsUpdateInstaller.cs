using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// После скачивания dmg: выход из игры и bash-скрипт, который удаляет старый .app и старые {prefix}*.dmg, затем открывает новый dmg.
/// Только macOS standalone (не Editor).
/// </summary>
public static class MacOsUpdateInstaller
{
    public static bool CanRun => Application.platform == RuntimePlatform.OSXPlayer;

    /// <param name="newDmgPath">Полный путь к только что скачанному dmg.</param>
    /// <param name="dmgFileNamePrefix">Префикс имён dmg для удаления старых (из сервера dmgFileNamePrefix).</param>
    public static void ScheduleInstallAndQuit(string newDmgPath, string dmgFileNamePrefix)
    {
#if UNITY_EDITOR
        Debug.LogWarning($"[MacOsUpdateInstaller] Editor: would install from {newDmgPath}");
        return;
#else
        if (!CanRun)
        {
            Debug.LogWarning("[MacOsUpdateInstaller] Только macOS standalone.");
            return;
        }

        if (string.IsNullOrEmpty(newDmgPath) || !File.Exists(newDmgPath))
        {
            Debug.LogError("[MacOsUpdateInstaller] Файл dmg не найден.");
            return;
        }

        string appBundle = ResolveMacAppBundlePath();
        if (string.IsNullOrEmpty(appBundle))
        {
            Debug.LogError("[MacOsUpdateInstaller] Не удалось определить путь к .app.");
            return;
        }

        string prefix = string.IsNullOrEmpty(dmgFileNamePrefix) ? "Game" : dmgFileNamePrefix.Trim();

        string script = BuildShellScript();
        string scriptPath = Path.Combine(Application.temporaryCachePath, "tz_client_update_install.sh");
        File.WriteAllText(scriptPath, script, Encoding.UTF8);

        try
        {
            var chmod = new ProcessStartInfo
            {
                FileName = "/bin/chmod",
                Arguments = "+x \"" + scriptPath + "\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(chmod)?.WaitForExit();

            var psi = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = "\"" + scriptPath + "\" \"" + appBundle + "\" \"" + newDmgPath + "\" \"" + prefix + "\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(psi);
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[MacOsUpdateInstaller] " + ex.Message);
            return;
        }

        Application.Quit();
#endif
    }

    private static string BuildShellScript()
    {
        return @"#!/bin/bash
set -euo pipefail
sleep 2
APP_BUNDLE=""$1""
NEW_DMG=""$2""
PREFIX=""$3""
if [[ -d ""$APP_BUNDLE"" ]]; then
  rm -rf ""$APP_BUNDLE""
fi
DMG_DIR=""$(dirname ""$NEW_DMG"")""
NEW_NAME=""$(basename ""$NEW_DMG"")""
shopt -s nullglob
for f in ""$DMG_DIR""/""$PREFIX""*.dmg; do
  [[ -f ""$f"" ]] || continue
  base=""$(basename ""$f"")""
  if [[ ""$base"" != ""$NEW_NAME"" ]]; then
    rm -f ""$f""
  fi
done
open ""$NEW_DMG""
";
    }

    /// <summary>Путь к корню .app текущего процесса (macOS).</summary>
    public static string ResolveMacAppBundlePath()
    {
        try
        {
            using var p = Process.GetCurrentProcess();
            string exePath = p.MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath))
                return null;
            // .../Game.app/Contents/MacOS/Game
            var macosDir = new DirectoryInfo(Path.GetDirectoryName(exePath) ?? "");
            var contents = macosDir.Parent;
            var appRoot = contents?.Parent;
            return appRoot?.FullName;
        }
        catch
        {
            return null;
        }
    }
}
