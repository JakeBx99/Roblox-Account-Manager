using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using System;
using System.IO;

namespace RBX_Alt_Manager.Classes
{
    public static class ClientSettingsPatcher
    {
        public static void PatchSettings()
        {
            DirectoryInfo VersionFolder = null;
            bool FoundViaRegistry = false;

            // Try multiple registry keys to find Roblox path
            string[] RegistryKeys = { @"roblox\DefaultIcon", @"roblox-player\DefaultIcon" };
            foreach (string Key in RegistryKeys)
            {
                object RegistryValue = Registry.ClassesRoot.OpenSubKey(Key)?.GetValue("");
                if (RegistryValue != null && RegistryValue is string RobloxPath)
                {
                    if (RobloxPath.Contains(",")) RobloxPath = RobloxPath.Split(',')[0];
                    RobloxPath = RobloxPath.Trim('"');

                    if (File.Exists(RobloxPath))
                        VersionFolder = Directory.GetParent(RobloxPath);
                    else if (Directory.Exists(RobloxPath))
                        VersionFolder = new DirectoryInfo(RobloxPath);

                    if (VersionFolder != null && VersionFolder.Exists)
                    {
                        FoundViaRegistry = true;
                        break;
                    }
                }
            }

            // Fallback to LocalAppData
            if (VersionFolder == null || !VersionFolder.Exists)
            {
                string LocalAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "Versions");
                if (Directory.Exists(LocalAppData)) VersionFolder = new DirectoryInfo(LocalAppData);
            }

            if (VersionFolder == null || !VersionFolder.Exists)
            {
                Program.Logger.Error("Can't patch ClientAppSettings, Roblox folder not found");
                return;
            }

            // If we are in the Versions folder, find the newest subfolder
            if (VersionFolder.Name.Equals("Versions", StringComparison.OrdinalIgnoreCase))
            {
                DirectoryInfo NewestVersion = null;
                foreach (DirectoryInfo Dir in VersionFolder.GetDirectories())
                {
                    // Check if it's a version folder or contains roblox executables
                    if (Dir.Name.StartsWith("version-", StringComparison.OrdinalIgnoreCase) || File.Exists(Path.Combine(Dir.FullName, "RobloxPlayerBeta.exe")) || File.Exists(Path.Combine(Dir.FullName, "RobloxPlayerLauncher.exe")))
                    {
                        if (NewestVersion == null || Dir.LastWriteTime > NewestVersion.LastWriteTime)
                            NewestVersion = Dir;
                    }
                }

                if (NewestVersion != null)
                    VersionFolder = NewestVersion;
            }

            // Final check: does it contain a way to run roblox, is it a known version folder, or a known bootstrapper?
            bool HasBeta = File.Exists(Path.Combine(VersionFolder.FullName, "RobloxPlayerBeta.exe"));
            bool HasLauncher = File.Exists(Path.Combine(VersionFolder.FullName, "RobloxPlayerLauncher.exe"));
            bool IsVersionFolder = VersionFolder.Name.StartsWith("version-", StringComparison.OrdinalIgnoreCase);
            bool IsCustomBootstrapper = VersionFolder.Name.IndexOf("Fishstrap", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         VersionFolder.Name.IndexOf("Bloxstrap", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!HasBeta && !HasLauncher && !IsVersionFolder && !IsCustomBootstrapper && !FoundViaRegistry)
            {
                Program.Logger.Error($"Can't patch ClientAppSettings, folder ({VersionFolder.Name}) does not appear to be a valid Roblox version folder");
                return;
            }

            DirectoryInfo SettingsFolder = new DirectoryInfo(Path.Combine(VersionFolder.FullName, "ClientSettings"));

            if (!SettingsFolder.Exists) SettingsFolder.Create();

            string CustomFN = AccountManager.General.Exists("CustomClientSettings") ? AccountManager.General.Get<string>("CustomClientSettings") : string.Empty;
            string SettingsFN = Path.Combine(SettingsFolder.FullName, "ClientAppSettings.json");

            if (!string.IsNullOrEmpty(CustomFN) && File.Exists(CustomFN))
                File.Copy(CustomFN, SettingsFN);
            else if (AccountManager.General.Get<bool>("UnlockFPS"))
            {
                if (File.Exists(SettingsFN) && File.ReadAllText(SettingsFN).TryParseJson(out JObject Settings))
                {
                    Settings["DFIntTaskSchedulerTargetFps"] = AccountManager.General.Exists("MaxFPSValue") ? AccountManager.General.Get<int>("MaxFPSValue") : 240;
                    File.WriteAllText(SettingsFN, Settings.ToString(Newtonsoft.Json.Formatting.None));
                }
                else
                    File.WriteAllText(SettingsFN, "{\"DFIntTaskSchedulerTargetFps\":240}");
            }
        }
    }
}