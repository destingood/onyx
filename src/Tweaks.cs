using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace BTOptimizer
{
    /// <summary>Catalogue des optimisations. Chaque entrée sait s'appliquer, se rétablir et se détecter.</summary>
    internal static class Catalog
    {
        private const string MMKey    = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
        private const string GamesKey = MMKey + @"\Tasks\Games";
        private const string AudioKey = MMKey + @"\Tasks\Audio";
        private const string ProAudioKey = MMKey + @"\Tasks\Pro Audio";

        public static List<Tweak> All()
        {
            var list = new List<Tweak>();

            // ================= SOURIS & CLAVIER =================
            list.Add(new Tweak
            {
                Id = "mouse_accel", Category = Cat.Souris, Recommended = true, Esport = true,
                Name = "Désactiver l'accélération de la souris (précision du pointeur)",
                Desc = "Déplacement 1:1 de la souris, indispensable pour la visée. Ne touche pas au curseur de vitesse. Effet immédiat.",
                BackupKeys = new[] { @"HKCU\Control Panel\Mouse" },
                Apply = () =>
                {
                    Sys.SetUser(@"Control Panel\Mouse", "MouseSpeed", "0", RegistryValueKind.String);
                    Sys.SetUser(@"Control Panel\Mouse", "MouseThreshold1", "0", RegistryValueKind.String);
                    Sys.SetUser(@"Control Panel\Mouse", "MouseThreshold2", "0", RegistryValueKind.String);
                    if (Sys.SameUser) Native.SetMouseParams(0, 0, 0);
                },
                Revert = () =>
                {
                    Sys.SetUser(@"Control Panel\Mouse", "MouseSpeed", "1", RegistryValueKind.String);
                    Sys.SetUser(@"Control Panel\Mouse", "MouseThreshold1", "6", RegistryValueKind.String);
                    Sys.SetUser(@"Control Panel\Mouse", "MouseThreshold2", "10", RegistryValueKind.String);
                    if (Sys.SameUser) Native.SetMouseParams(6, 10, 1);
                },
                Check = () => Sys.StrEquals(Sys.GetUser(@"Control Panel\Mouse", "MouseSpeed"), "0")
            });

            list.Add(new Tweak
            {
                Id = "input_queues", Category = Cat.Souris, Esport = true, Reboot = true,
                Name = "Réduire les files d'attente souris/clavier (32 au lieu de 100)",
                Desc = "Buffers pilote plus petits = traitement plus direct des entrées. Expérimental : sans effet mesurable sur certaines machines.",
                BackupKeys = new[] { @"HKLM\SYSTEM\CurrentControlSet\Services\mouclass\Parameters",
                                     @"HKLM\SYSTEM\CurrentControlSet\Services\kbdclass\Parameters" },
                Apply = () =>
                {
                    Sys.SetMachine(@"SYSTEM\CurrentControlSet\Services\mouclass\Parameters", "MouseDataQueueSize", 32, RegistryValueKind.DWord);
                    Sys.SetMachine(@"SYSTEM\CurrentControlSet\Services\kbdclass\Parameters", "KeyboardDataQueueSize", 32, RegistryValueKind.DWord);
                },
                Revert = () =>
                {
                    Sys.SetMachine(@"SYSTEM\CurrentControlSet\Services\mouclass\Parameters", "MouseDataQueueSize", 100, RegistryValueKind.DWord);
                    Sys.SetMachine(@"SYSTEM\CurrentControlSet\Services\kbdclass\Parameters", "KeyboardDataQueueSize", 100, RegistryValueKind.DWord);
                },
                Check = () => Sys.IntEquals(Sys.GetMachine(@"SYSTEM\CurrentControlSet\Services\mouclass\Parameters", "MouseDataQueueSize"), 32)
            });

            list.Add(new Tweak
            {
                Id = "sticky_keys_off", Category = Cat.Souris, Esport = true,
                Name = "Désactiver les popups touches rémanentes / filtres (Shift x5)",
                Desc = "Plus de fenêtre « touches rémanentes » qui vole le focus en pleine partie quand on spamme Shift ou qu'on maintient une touche.",
                BackupKeys = new[] { @"HKCU\Control Panel\Accessibility" },
                Apply = () =>
                {
                    Sys.SetUser(@"Control Panel\Accessibility\StickyKeys", "Flags", "506", RegistryValueKind.String);
                    Sys.SetUser(@"Control Panel\Accessibility\Keyboard Response", "Flags", "122", RegistryValueKind.String);
                    Sys.SetUser(@"Control Panel\Accessibility\ToggleKeys", "Flags", "58", RegistryValueKind.String);
                },
                Revert = () =>
                {
                    Sys.SetUser(@"Control Panel\Accessibility\StickyKeys", "Flags", "510", RegistryValueKind.String);
                    Sys.SetUser(@"Control Panel\Accessibility\Keyboard Response", "Flags", "126", RegistryValueKind.String);
                    Sys.SetUser(@"Control Panel\Accessibility\ToggleKeys", "Flags", "62", RegistryValueKind.String);
                },
                Check = () => Sys.StrEquals(Sys.GetUser(@"Control Panel\Accessibility\StickyKeys", "Flags"), "506")
            });

            list.Add(new Tweak
            {
                Id = "keyboard_delay", Category = Cat.Souris,
                Name = "Répétition clavier immédiate (KeyboardDelay = 0)",
                Desc = "Réduit le délai avant répétition d'une touche maintenue. Confort en édition/menus ; sans effet sur la latence d'une frappe simple.",
                BackupKeys = new[] { @"HKCU\Control Panel\Keyboard" },
                Apply = () => Sys.SetUser(@"Control Panel\Keyboard", "KeyboardDelay", "0", RegistryValueKind.String),
                Revert = () => Sys.SetUser(@"Control Panel\Keyboard", "KeyboardDelay", "1", RegistryValueKind.String),
                Check = () => Sys.StrEquals(Sys.GetUser(@"Control Panel\Keyboard", "KeyboardDelay"), "0")
            });

            // ================= ALIMENTATION & CPU =================
            list.Add(new Tweak
            {
                Id = "power_ultimate", Category = Cat.Alim, Recommended = true, Esport = true,
                Name = "Activer le plan d'alimentation Performances ultimes",
                Desc = "Supprime les économies d'énergie qui endorment le CPU (latence de réveil des coeurs). Le plan est créé s'il n'existe pas.",
                Apply = () => Sys.EnableUltimatePlan(),
                Revert = () => Sys.RestoreBalancedPlan(),
                Check = () => Sys.UltimateActive()
            });

            list.Add(new Tweak
            {
                Id = "power_throttling", Category = Cat.Alim, Recommended = true, Esport = true,
                Name = "Désactiver le Power Throttling (bridage énergétique des processus)",
                Desc = "Empêche Windows de brider les applications pour économiser l'énergie.",
                BackupKeys = new[] { @"HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling" },
                Apply = () => Sys.SetMachine(@"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", 1, RegistryValueKind.DWord),
                Revert = () => Sys.DelMachine(@"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff"),
                Check = () => Sys.IntEquals(Sys.GetMachine(@"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff"), 1)
            });

            list.Add(new Tweak
            {
                Id = "usb_suspend", Category = Cat.Alim, Esport = true,
                Name = "Désactiver la suspension sélective USB (plan actif)",
                Desc = "Évite que la souris/le clavier USB soient mis en veille par Windows. À appliquer après le plan Performances ultimes.",
                Apply = () => Sys.SetUsbSuspend(true),
                Revert = () => Sys.SetUsbSuspend(false),
                Check = () => Sys.PowerAcEquals("2a737441-1930-4402-8d77-b2bebba308a3", "48e6b7a6-50f5-4782-a5d4-53bb8f07e226", 0)
            });

            list.Add(new Tweak
            {
                Id = "hibernate_off", Category = Cat.Alim,
                Name = "Désactiver la veille prolongée (libère hiberfil.sys)",
                Desc = "Supprime l'hibernation ET le démarrage rapide : libère plusieurs Go sur le disque, arrêts 100 % « propres ». Optionnel.",
                Apply = () => Sys.RunThrow(Sys.Sys32("powercfg.exe"), "/hibernate off", "Désactivation de la veille prolongée"),
                Revert = () => Sys.RunThrow(Sys.Sys32("powercfg.exe"), "/hibernate on", "Réactivation de la veille prolongée"),
                Check = () => Sys.IntEquals(Sys.GetMachine(@"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled"), 0)
            });

            list.Add(new Tweak
            {
                Id = "pcie_aspm_off", Category = Cat.Alim, Esport = true,
                Name = "Désactiver l'économie d'énergie PCI Express (ASPM)",
                Desc = "Les liens PCIe (GPU, SSD NVMe, carte réseau) restent à pleine vitesse au lieu de se rendormir : supprime des micro-latences de réveil. « Rétablir » remet le niveau Modéré.",
                Apply = () => Sys.SetPowerValue("501a4d13-42af-4429-9fd1-a8218c268e20", "ee12f906-d277-404b-b6da-e5fa1a576df5", 0, 0),
                Revert = () => Sys.SetPowerValue("501a4d13-42af-4429-9fd1-a8218c268e20", "ee12f906-d277-404b-b6da-e5fa1a576df5", 1, 1),
                Check = () => Sys.PowerAcEquals("501a4d13-42af-4429-9fd1-a8218c268e20", "ee12f906-d277-404b-b6da-e5fa1a576df5", 0)
            });

            // ================= GPU & JEUX =================
            list.Add(new Tweak
            {
                Id = "hags", Category = Cat.Gpu, Esport = true, Reboot = true,
                Name = "Activer la planification GPU accélérée par matériel (HAGS)",
                Desc = "Réduit la latence de la file de rendu sur GPU récents (NVIDIA GTX 10xx+/RTX, AMD RX 5000+). À éviter sur GPU anciens.",
                BackupKeys = new[] { @"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers" },
                Apply = () => Sys.SetMachine(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2, RegistryValueKind.DWord),
                Revert = () => Sys.DelMachine(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode"),
                Check = () => Sys.IntEquals(Sys.GetMachine(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode"), 2)
            });

            list.Add(new Tweak
            {
                Id = "fso_disable", Category = Cat.Gpu, Recommended = true, Esport = true,
                Name = "Désactiver les optimisations plein écran (vrai plein écran exclusif)",
                Desc = "Force le comportement plein écran classique : moins de latence de présentation dans les jeux plein écran.",
                BackupKeys = new[] { @"HKCU\System\GameConfigStore" },
                Apply = () =>
                {
                    Sys.SetUser(@"System\GameConfigStore", "GameDVR_FSEBehaviorMode", 2, RegistryValueKind.DWord);
                    Sys.SetUser(@"System\GameConfigStore", "GameDVR_HonorUserFSEBehaviorMode", 1, RegistryValueKind.DWord);
                    Sys.SetUser(@"System\GameConfigStore", "GameDVR_DXGIHonorFSEWindowsCompatible", 1, RegistryValueKind.DWord);
                    Sys.SetUser(@"System\GameConfigStore", "GameDVR_EFSEFeatureFlags", 0, RegistryValueKind.DWord);
                },
                Revert = () =>
                {
                    Sys.DelUser(@"System\GameConfigStore", "GameDVR_FSEBehaviorMode");
                    Sys.DelUser(@"System\GameConfigStore", "GameDVR_HonorUserFSEBehaviorMode");
                    Sys.DelUser(@"System\GameConfigStore", "GameDVR_DXGIHonorFSEWindowsCompatible");
                    Sys.DelUser(@"System\GameConfigStore", "GameDVR_EFSEFeatureFlags");
                },
                Check = () => Sys.IntEquals(Sys.GetUser(@"System\GameConfigStore", "GameDVR_FSEBehaviorMode"), 2)
            });

            list.Add(new Tweak
            {
                Id = "game_mode", Category = Cat.Gpu, Recommended = true, Esport = true,
                Name = "Activer le Mode Jeu de Windows",
                Desc = "Windows donne la priorité CPU/GPU au jeu au premier plan et suspend certaines tâches de fond.",
                BackupKeys = new[] { @"HKCU\Software\Microsoft\GameBar" },
                Apply = () =>
                {
                    Sys.SetUser(@"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1, RegistryValueKind.DWord);
                    Sys.SetUser(@"Software\Microsoft\GameBar", "AllowAutoGameMode", 1, RegistryValueKind.DWord);
                },
                Revert = () =>
                {
                    Sys.DelUser(@"Software\Microsoft\GameBar", "AutoGameModeEnabled");
                    Sys.DelUser(@"Software\Microsoft\GameBar", "AllowAutoGameMode");
                },
                Check = () => Sys.IntEquals(Sys.GetUser(@"Software\Microsoft\GameBar", "AutoGameModeEnabled"), 1)
            });

            list.Add(new Tweak
            {
                Id = "gamedvr_off", Category = Cat.Gpu, Recommended = true, Esport = true,
                Name = "Désactiver Game DVR / captures Xbox Game Bar",
                Desc = "Supprime l'enregistrement d'écran en arrière-plan (source classique de stutter et de latence).",
                BackupKeys = new[] { @"HKCU\System\GameConfigStore",
                                     @"HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR",
                                     @"HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR" },
                Apply = () =>
                {
                    Sys.SetUser(@"System\GameConfigStore", "GameDVR_Enabled", 0, RegistryValueKind.DWord);
                    Sys.SetUser(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0, RegistryValueKind.DWord);
                    Sys.SetMachine(@"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0, RegistryValueKind.DWord);
                },
                Revert = () =>
                {
                    Sys.SetUser(@"System\GameConfigStore", "GameDVR_Enabled", 1, RegistryValueKind.DWord);
                    Sys.SetUser(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 1, RegistryValueKind.DWord);
                    Sys.DelMachine(@"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR");
                },
                Check = () => Sys.IntEquals(Sys.GetUser(@"System\GameConfigStore", "GameDVR_Enabled"), 0)
            });

            list.Add(new Tweak
            {
                Id = "mpo_off", Category = Cat.Gpu, Esport = true, Reboot = true,
                Name = "Désactiver le MultiPlane Overlay (MPO)",
                Desc = "Corrige scintillements, stutter et frame pacing irrégulier liés au MPO sur certains écrans/GPU (fix historiquement conseillé par NVIDIA).",
                BackupKeys = new[] { @"HKLM\SOFTWARE\Microsoft\Windows\Dwm" },
                Apply = () => Sys.SetMachine(@"SOFTWARE\Microsoft\Windows\Dwm", "OverlayTestMode", 5, RegistryValueKind.DWord),
                Revert = () => Sys.DelMachine(@"SOFTWARE\Microsoft\Windows\Dwm", "OverlayTestMode"),
                Check = () => Sys.IntEquals(Sys.GetMachine(@"SOFTWARE\Microsoft\Windows\Dwm", "OverlayTestMode"), 5)
            });

            list.Add(new Tweak
            {
                Id = "gamebar_popup", Category = Cat.Gpu, Esport = true,
                Name = "Désactiver l'ouverture de la Xbox Game Bar (Win+G / bouton manette)",
                Desc = "La Game Bar ne s'ouvre plus par-dessus le jeu (touche Win+G ou bouton Xbox de la manette).",
                BackupKeys = new[] { @"HKCU\Software\Microsoft\GameBar" },
                Apply = () =>
                {
                    Sys.SetUser(@"Software\Microsoft\GameBar", "UseNexusForGameBarEnabled", 0, RegistryValueKind.DWord);
                    Sys.SetUser(@"Software\Microsoft\GameBar", "ShowStartupPanel", 0, RegistryValueKind.DWord);
                },
                Revert = () =>
                {
                    Sys.DelUser(@"Software\Microsoft\GameBar", "UseNexusForGameBarEnabled");
                    Sys.DelUser(@"Software\Microsoft\GameBar", "ShowStartupPanel");
                },
                Check = () => Sys.IntEquals(Sys.GetUser(@"Software\Microsoft\GameBar", "UseNexusForGameBarEnabled"), 0)
            });

            list.Add(new Tweak
            {
                Id = "windowed_flip", Category = Cat.Gpu, Esport = true,
                Name = "Optimisations pour les jeux en fenêtré (modèle flip, Windows 11)",
                Desc = "Active le nouveau modèle de présentation pour les jeux en fenêtré/borderless : latence proche du plein écran exclusif. Sans effet sur Windows 10.",
                BackupKeys = new[] { @"HKCU\Software\Microsoft\DirectX\UserGpuPreferences" },
                Apply = () => Sys.SetDxToken("SwapEffectUpgradeEnable", "1"),
                Revert = () => Sys.SetDxToken("SwapEffectUpgradeEnable", null),
                Check = () => Sys.DxTokenEquals("SwapEffectUpgradeEnable", "1")
            });

            // ================= SYSTÈME & PLANIFICATEUR =================
            list.Add(new Tweak
            {
                Id = "sysresp", Category = Cat.Systeme, Recommended = true, Esport = true,
                Name = "SystemResponsiveness = 10 (priorité aux applications)",
                Desc = "Réserve moins de CPU aux tâches multimédia de fond (20 % par défaut, 10 % ici).",
                BackupKeys = new[] { @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile" },
                Apply = () => Sys.SetMachine(MMKey, "SystemResponsiveness", 10, RegistryValueKind.DWord),
                Revert = () => Sys.SetMachine(MMKey, "SystemResponsiveness", 20, RegistryValueKind.DWord),
                Check = () => Sys.IntEquals(Sys.GetMachine(MMKey, "SystemResponsiveness"), 10)
            });

            list.Add(new Tweak
            {
                Id = "w32ps", Category = Cat.Systeme, Esport = true,
                Name = "Win32PrioritySeparation = 38 (quanta courts, priorité premier plan)",
                Desc = "Le planificateur CPU réagit plus vite pour l'application au premier plan. Valeur prisée des joueurs (défaut Windows : 2).",
                BackupKeys = new[] { @"HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl" },
                Apply = () => Sys.SetMachine(@"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 38, RegistryValueKind.DWord),
                Revert = () => Sys.SetMachine(@"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 2, RegistryValueKind.DWord),
                Check = () => Sys.IntEquals(Sys.GetMachine(@"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation"), 38)
            });

            list.Add(new Tweak
            {
                Id = "games_task", Category = Cat.Systeme, Esport = true,
                Name = "Profil MMCSS 'Games' : priorité CPU/GPU haute pour les jeux",
                Desc = "Les jeux qui utilisent le profil multimédia Games obtiennent une priorité d'ordonnancement plus élevée.",
                BackupKeys = new[] { @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile" },
                Apply = () =>
                {
                    Sys.SetMachine(GamesKey, "GPU Priority", 8, RegistryValueKind.DWord);
                    Sys.SetMachine(GamesKey, "Priority", 6, RegistryValueKind.DWord);
                    Sys.SetMachine(GamesKey, "Scheduling Category", "High", RegistryValueKind.String);
                    Sys.SetMachine(GamesKey, "SFIO Priority", "High", RegistryValueKind.String);
                },
                Revert = () =>
                {
                    Sys.SetMachine(GamesKey, "GPU Priority", 8, RegistryValueKind.DWord);
                    Sys.SetMachine(GamesKey, "Priority", 2, RegistryValueKind.DWord);
                    Sys.SetMachine(GamesKey, "Scheduling Category", "Medium", RegistryValueKind.String);
                    Sys.SetMachine(GamesKey, "SFIO Priority", "Normal", RegistryValueKind.String);
                },
                Check = () => Sys.StrEquals(Sys.GetMachine(GamesKey, "Scheduling Category"), "High")
            });

            list.Add(new Tweak
            {
                Id = "netthrottle", Category = Cat.Systeme, Esport = true,
                Name = "Désactiver le NetworkThrottlingIndex (bridage réseau multimédia)",
                Desc = "Supprime la limite de paquets réseau traités par milliseconde pendant la lecture multimédia.",
                BackupKeys = new[] { @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile" },
                Apply = () => Sys.SetMachine(MMKey, "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), RegistryValueKind.DWord),
                Revert = () => Sys.SetMachine(MMKey, "NetworkThrottlingIndex", 10, RegistryValueKind.DWord),
                Check = () => Sys.IntEquals(Sys.GetMachine(MMKey, "NetworkThrottlingIndex"), -1)
            });

            list.Add(new Tweak
            {
                Id = "dynamic_tick", Category = Cat.Systeme, Esport = true, Reboot = true,
                Name = "Désactiver le tick dynamique du noyau (bcdedit) — EXPÉRIMENTAL",
                Desc = "Timer noyau à cadence fixe : peut lisser la latence sur certaines machines, augmente la consommation. À tester, réversible.",
                Apply = () => Sys.RunThrow(Sys.Sys32("bcdedit.exe"), "/set disabledynamictick yes", "bcdedit disabledynamictick"),
                Revert = () => Sys.RunThrow(Sys.Sys32("bcdedit.exe"), "/deletevalue disabledynamictick", "bcdedit deletevalue"),
                Check = () =>
                {
                    NativeResult r = Sys.Run(Sys.Sys32("bcdedit.exe"), "/enum {current}");
                    if (r.ExitCode != 0) return null; // pas admin (mode test) ou accès refusé
                    Match m = Regex.Match(r.Output, @"disabledynamictick\s+(\S+)", RegexOptions.IgnoreCase);
                    if (!m.Success) return false;
                    string v = m.Groups[1].Value;
                    return v.Equals("Yes", StringComparison.OrdinalIgnoreCase)
                        || v.Equals("Oui", StringComparison.OrdinalIgnoreCase)
                        || v.Equals("True", StringComparison.OrdinalIgnoreCase);
                }
            });

            list.Add(new Tweak
            {
                Id = "timer_global", Category = Cat.Systeme, Esport = true, Reboot = true,
                Name = "Timer haute résolution GLOBAL (Windows 11)",
                Desc = "Depuis Windows 10 2004/11, les demandes de timer 1 ms sont ignorées pour les fenêtres en arrière-plan. Ce réglage restaure le comportement global : complète la case « Timer 1 ms » de cette app.",
                BackupKeys = new[] { @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\kernel" },
                Apply = () => Sys.SetMachine(@"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "GlobalTimerResolutionRequests", 1, RegistryValueKind.DWord),
                Revert = () => Sys.DelMachine(@"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "GlobalTimerResolutionRequests"),
                Check = () => Sys.IntEquals(Sys.GetMachine(@"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "GlobalTimerResolutionRequests"), 1)
            });

            list.Add(new Tweak
            {
                Id = "paging_executive", Category = Cat.Systeme, Esport = true, Reboot = true,
                Name = "Garder le noyau en RAM (DisablePagingExecutive)",
                Desc = "Empêche Windows de paginer le code noyau/pilotes vers le disque. Conseillé avec 16 Go de RAM ou plus.",
                BackupKeys = new[] { @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management" },
                Apply = () => Sys.SetMachine(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "DisablePagingExecutive", 1, RegistryValueKind.DWord),
                Revert = () => Sys.SetMachine(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "DisablePagingExecutive", 0, RegistryValueKind.DWord),
                Check = () => Sys.IntEquals(Sys.GetMachine(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "DisablePagingExecutive"), 1)
            });

            list.Add(new Tweak
            {
                Id = "hpet_cleanup", Category = Cat.Systeme, Esport = true, Reboot = true,
                Name = "Nettoyage : retirer useplatformclock / useplatformtick (HPET forcé)",
                Desc = "D'anciens guides forçaient l'HPET, ce qui DÉGRADE la latence sur les PC modernes. Ce nettoyage supprime ces réglages s'ils existent. « Rétablir » n'a rien à faire : leur absence EST le défaut Windows.",
                Apply = () =>
                {
                    // Tolérant : code retour non nul = la valeur n'existait pas (déjà propre).
                    Sys.Run(Sys.Sys32("bcdedit.exe"), "/deletevalue useplatformclock");
                    Sys.Run(Sys.Sys32("bcdedit.exe"), "/deletevalue useplatformtick");
                },
                Revert = () => { /* rien à rétablir : l'absence est le défaut Windows */ },
                Check = () =>
                {
                    NativeResult r = Sys.Run(Sys.Sys32("bcdedit.exe"), "/enum {current}");
                    if (r.ExitCode != 0) return null;
                    bool dirty = r.Output.IndexOf("useplatformclock", StringComparison.OrdinalIgnoreCase) >= 0
                              || r.Output.IndexOf("useplatformtick", StringComparison.OrdinalIgnoreCase) >= 0;
                    return !dirty;
                }
            });

            // ================= RAPIDITÉ & DÉMARRAGE =================
            list.Add(new Tweak
            {
                Id = "visualfx", Category = Cat.Rapidite, Esport = true,
                Name = "Effets visuels : réglé sur « Meilleures performances »",
                Desc = "Désactive animations et ombres de Windows : interface plus sèche mais plus rapide. Prend pleinement effet à la prochaine session.",
                BackupKeys = new[] { @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects" },
                Apply = () => Sys.SetUser(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 2, RegistryValueKind.DWord),
                Revert = () => Sys.SetUser(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 0, RegistryValueKind.DWord),
                Check = () => Sys.IntEquals(Sys.GetUser(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting"), 2)
            });

            list.Add(new Tweak
            {
                Id = "startup_delay", Category = Cat.Rapidite, Esport = true,
                Name = "Supprimer le délai de lancement des applis au démarrage",
                Desc = "Windows retarde artificiellement les applications du démarrage ; ce réglage supprime ce délai (StartupDelayInMSec = 0).",
                BackupKeys = new[] { @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize" },
                Apply = () => Sys.SetUser(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", 0, RegistryValueKind.DWord),
                Revert = () => Sys.DelUser(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec"),
                Check = () => Sys.IntEquals(Sys.GetUser(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec"), 0)
            });

            list.Add(new Tweak
            {
                Id = "menu_delay", Category = Cat.Rapidite,
                Name = "Menus instantanés (MenuShowDelay = 0)",
                Desc = "Ne change rien en jeu, mais l'interface Windows répond immédiatement. Prend effet à la prochaine session.",
                BackupKeys = new[] { @"HKCU\Control Panel\Desktop" },
                Apply = () => Sys.SetUser(@"Control Panel\Desktop", "MenuShowDelay", "0", RegistryValueKind.String),
                Revert = () => Sys.SetUser(@"Control Panel\Desktop", "MenuShowDelay", "400", RegistryValueKind.String),
                Check = () => Sys.StrEquals(Sys.GetUser(@"Control Panel\Desktop", "MenuShowDelay"), "0")
            });

            list.Add(new Tweak
            {
                Id = "transparency", Category = Cat.Rapidite,
                Name = "Désactiver la transparence de l'interface",
                Desc = "Moins de travail de composition pour le GPU sur le bureau. Effet purement cosmétique en jeu.",
                BackupKeys = new[] { @"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize" },
                Apply = () => Sys.SetUser(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0, RegistryValueKind.DWord),
                Revert = () => Sys.SetUser(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 1, RegistryValueKind.DWord),
                Check = () => Sys.IntEquals(Sys.GetUser(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency"), 0)
            });

            list.Add(new Tweak
            {
                Id = "fastboot_off", Category = Cat.Rapidite,
                Name = "Désactiver le démarrage rapide (Fast Startup)",
                Desc = "Le démarrage rapide peut laisser pilotes/services dans un état dégradé au fil des arrêts. Boot un peu plus long mais plus « propre ». Optionnel.",
                BackupKeys = new[] { @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Power" },
                Apply = () => Sys.SetMachine(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", 0, RegistryValueKind.DWord),
                Revert = () => Sys.SetMachine(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", 1, RegistryValueKind.DWord),
                Check = () => Sys.IntEquals(Sys.GetMachine(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled"), 0)
            });

            list.Add(new Tweak
            {
                Id = "edge_background", Category = Cat.Rapidite,
                Name = "Empêcher Microsoft Edge de tourner en fond / au démarrage",
                Desc = "Coupe le « Startup Boost » et le mode arrière-plan d'Edge : moins de processus et de RAM utilisés quand Edge est fermé.",
                BackupKeys = new[] { @"HKLM\SOFTWARE\Policies\Microsoft\Edge" },
                Apply = () =>
                {
                    Sys.SetMachine(@"SOFTWARE\Policies\Microsoft\Edge", "StartupBoostEnabled", 0, RegistryValueKind.DWord);
                    Sys.SetMachine(@"SOFTWARE\Policies\Microsoft\Edge", "BackgroundModeEnabled", 0, RegistryValueKind.DWord);
                },
                Revert = () =>
                {
                    Sys.DelMachine(@"SOFTWARE\Policies\Microsoft\Edge", "StartupBoostEnabled");
                    Sys.DelMachine(@"SOFTWARE\Policies\Microsoft\Edge", "BackgroundModeEnabled");
                },
                Check = () => Sys.IntEquals(Sys.GetMachine(@"SOFTWARE\Policies\Microsoft\Edge", "StartupBoostEnabled"), 0)
            });

            list.Add(new Tweak
            {
                Id = "widgets_off", Category = Cat.Rapidite,
                Name = "Désactiver les Widgets (Windows 11) / Actualités (Windows 10)",
                Desc = "Supprime le processus Widgets/Actualités qui tourne en permanence en arrière-plan. Plein effet à la prochaine session.",
                BackupKeys = new[] { @"HKLM\SOFTWARE\Policies\Microsoft\Dsh" },
                Apply = () => Sys.SetMachine(@"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", 0, RegistryValueKind.DWord),
                Revert = () => Sys.DelMachine(@"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests"),
                Check = () => Sys.IntEquals(Sys.GetMachine(@"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests"), 0)
            });

            list.Add(new Tweak
            {
                Id = "search_bing_off", Category = Cat.Rapidite,
                Name = "Recherche du menu Démarrer 100 % locale (sans Bing)",
                Desc = "Le menu Démarrer ne consulte plus le web à chaque frappe : résultats instantanés et moins de réseau en fond.",
                BackupKeys = new[] { @"HKCU\Software\Policies\Microsoft\Windows\Explorer" },
                Apply = () => Sys.SetUser(@"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1, RegistryValueKind.DWord),
                Revert = () => Sys.DelUser(@"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions"),
                Check = () => Sys.IntEquals(Sys.GetUser(@"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions"), 1)
            });

            list.Add(new Tweak
            {
                Id = "fast_shutdown", Category = Cat.Rapidite,
                Name = "Arrêt / redémarrage du PC plus rapide",
                Desc = "Réduit le délai d'attente des services à l'arrêt (5 s -> 2 s) et ferme automatiquement les applications qui bloquent. (Valeur d'origine : 5000, rétablie par le bouton Rétablir.)",
                BackupKeys = new[] { @"HKCU\Control Panel\Desktop" },
                Apply = () =>
                {
                    Sys.SetMachine(@"SYSTEM\CurrentControlSet\Control", "WaitToKillServiceTimeout", "2000", RegistryValueKind.String);
                    Sys.SetUser(@"Control Panel\Desktop", "AutoEndTasks", "1", RegistryValueKind.String);
                },
                Revert = () =>
                {
                    Sys.SetMachine(@"SYSTEM\CurrentControlSet\Control", "WaitToKillServiceTimeout", "5000", RegistryValueKind.String);
                    Sys.DelUser(@"Control Panel\Desktop", "AutoEndTasks");
                },
                Check = () => Sys.StrEquals(Sys.GetMachine(@"SYSTEM\CurrentControlSet\Control", "WaitToKillServiceTimeout"), "2000")
            });

            list.Add(new Tweak
            {
                Id = "mousehover_fast", Category = Cat.Rapidite,
                Name = "Infobulles et survol instantanés (MouseHoverTime = 10)",
                Desc = "La barre des tâches et l'Explorateur réagissent au survol sans le délai de 400 ms. Confort uniquement.",
                BackupKeys = new[] { @"HKCU\Control Panel\Mouse" },
                Apply = () => Sys.SetUser(@"Control Panel\Mouse", "MouseHoverTime", "10", RegistryValueKind.String),
                Revert = () => Sys.SetUser(@"Control Panel\Mouse", "MouseHoverTime", "400", RegistryValueKind.String),
                Check = () => Sys.StrEquals(Sys.GetUser(@"Control Panel\Mouse", "MouseHoverTime"), "10")
            });

            list.Add(new Tweak
            {
                Id = "ntfs_memoryusage", Category = Cat.Rapidite, Reboot = true,
                Name = "Augmenter le cache NTFS (NtfsMemoryUsage = 2)",
                Desc = "Windows garde plus de métadonnées de fichiers en RAM : navigation disque plus rapide. Conseillé avec 16 Go de RAM ou plus.",
                BackupKeys = new[] { @"HKLM\SYSTEM\CurrentControlSet\Control\FileSystem" },
                Apply = () => Sys.RunThrow(Sys.Sys32("fsutil.exe"), "behavior set memoryusage 2", "Réglage du cache NTFS"),
                Revert = () => Sys.RunThrow(Sys.Sys32("fsutil.exe"), "behavior set memoryusage 1", "Réglage du cache NTFS"),
                Check = () => Sys.IntEquals(Sys.GetMachine(@"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsMemoryUsage"), 2)
            });

            list.Add(new Tweak
            {
                Id = "ntfs_83_off", Category = Cat.Rapidite,
                Name = "Désactiver les noms courts 8.3 (NTFS)",
                Desc = "Supprime la génération des noms de fichiers hérités du DOS : créations/listages plus rapides dans les gros dossiers. Seuls de très vieux logiciels 16 bits en dépendent.",
                BackupKeys = new[] { @"HKLM\SYSTEM\CurrentControlSet\Control\FileSystem" },
                Apply = () => Sys.RunThrow(Sys.Sys32("fsutil.exe"), "behavior set disable8dot3 1", "Désactivation des noms 8.3"),
                Revert = () => Sys.RunThrow(Sys.Sys32("fsutil.exe"), "behavior set disable8dot3 2", "Rétablissement des noms 8.3 (défaut par volume)"),
                Check = () => Sys.IntEquals(Sys.GetMachine(@"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsDisable8dot3NameCreation"), 1)
            });

            // ================= SERVICES & ARRIÈRE-PLAN =================
            list.Add(new Tweak
            {
                Id = "bg_apps", Category = Cat.Services, Esport = true,
                Name = "Désactiver les applications en arrière-plan (UWP)",
                Desc = "Empêche les applications du Store de tourner en fond. Peut retarder certaines notifications d'applis du Store. Si un jeu Store/Game Pass a une boutique qui charge à l'infini, rétablis ce réglage (ou menu ☰ → Boutiques en jeu).",
                BackupKeys = new[] { @"HKCU\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications" },
                Apply = () => Sys.SetUser(@"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 1, RegistryValueKind.DWord),
                Revert = () => Sys.DelUser(@"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled"),
                Check = () => Sys.IntEquals(Sys.GetUser(@"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled"), 1)
            });

            list.Add(new Tweak
            {
                Id = "sysmain_off", Category = Cat.Services,
                Name = "Désactiver SysMain / Superfetch (recommandé seulement sur SSD)",
                Desc = "Supprime le préchargement en fond. Sur SSD : libère CPU/disque. Sur disque dur mécanique : à ÉVITER (ralentit les chargements).",
                Apply = () => Sys.ConfigureService("SysMain", "disabled", true, false),
                Revert = () => Sys.ConfigureService("SysMain", "auto", false, true),
                Check = () => Sys.ServiceDisabled("SysMain")
            });

            list.Add(new Tweak
            {
                Id = "diagtrack_off", Category = Cat.Services,
                Name = "Désactiver la télémétrie (service DiagTrack)",
                Desc = "Arrête le service « Expériences utilisateur connectées et télémétrie » : moins d'activité disque/réseau en fond.",
                Apply = () => Sys.ConfigureService("DiagTrack", "disabled", true, false),
                Revert = () => Sys.ConfigureService("DiagTrack", "delayed-auto", false, true),
                Check = () => Sys.ServiceDisabled("DiagTrack")
            });

            list.Add(new Tweak
            {
                Id = "wsearch_off", Category = Cat.Services,
                Name = "Désactiver l'indexation de la recherche (service Windows Search)",
                Desc = "Supprime l'activité disque/CPU de l'indexeur. CONTREPARTIE : la recherche de fichiers dans l'Explorateur devient lente. À réserver aux PC 100 % jeu.",
                Apply = () => Sys.ConfigureService("WSearch", "disabled", true, false),
                Revert = () => Sys.ConfigureService("WSearch", "delayed-auto", false, true),
                Check = () => Sys.ServiceDisabled("WSearch")
            });

            list.Add(new Tweak
            {
                Id = "delivery_opt_off", Category = Cat.Services,
                Name = "Désactiver le partage P2P des mises à jour (Delivery Optimization)",
                Desc = "Windows n'envoie plus les mises à jour aux autres PC via votre connexion : moins de réseau et de disque en arrière-plan.",
                BackupKeys = new[] { @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization" },
                Apply = () => Sys.SetMachine(@"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", 0, RegistryValueKind.DWord),
                Revert = () => Sys.DelMachine(@"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode"),
                Check = () => Sys.IntEquals(Sys.GetMachine(@"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode"), 0)
            });

            list.Add(new Tweak
            {
                Id = "content_delivery_off", Category = Cat.Services,
                Name = "Bloquer les suggestions et applis sponsorisées de Windows",
                Desc = "Stoppe les « suggestions », conseils et applications promues que Windows installe/affiche tout seul en arrière-plan.",
                BackupKeys = new[] { @"HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager" },
                Apply = () =>
                {
                    string cdm = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
                    Sys.SetUser(cdm, "SilentInstalledAppsEnabled", 0, RegistryValueKind.DWord);
                    Sys.SetUser(cdm, "SystemPaneSuggestionsEnabled", 0, RegistryValueKind.DWord);
                    Sys.SetUser(cdm, "SoftLandingEnabled", 0, RegistryValueKind.DWord);
                    Sys.SetUser(cdm, "SubscribedContent-338388Enabled", 0, RegistryValueKind.DWord);
                },
                Revert = () =>
                {
                    string cdm = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
                    Sys.DelUser(cdm, "SilentInstalledAppsEnabled");
                    Sys.DelUser(cdm, "SystemPaneSuggestionsEnabled");
                    Sys.DelUser(cdm, "SoftLandingEnabled");
                    Sys.DelUser(cdm, "SubscribedContent-338388Enabled");
                },
                Check = () => Sys.IntEquals(Sys.GetUser(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SilentInstalledAppsEnabled"), 0)
            });

            list.Add(new Tweak
            {
                Id = "wer_off", Category = Cat.Services,
                Name = "Désactiver le rapport d'erreurs Windows (WER)",
                Desc = "Plus de collecte/envoi de rapports après un plantage : évite les pics disque/CPU de WerFault en pleine session.",
                BackupKeys = new[] { @"HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting" },
                Apply = () => Sys.SetMachine(@"SOFTWARE\Microsoft\Windows\Windows Error Reporting", "Disabled", 1, RegistryValueKind.DWord),
                Revert = () => Sys.DelMachine(@"SOFTWARE\Microsoft\Windows\Windows Error Reporting", "Disabled"),
                Check = () => Sys.IntEquals(Sys.GetMachine(@"SOFTWARE\Microsoft\Windows\Windows Error Reporting", "Disabled"), 1)
            });

            // ================= RÉSEAU =================
            list.Add(new Tweak
            {
                Id = "nagle", Category = Cat.Reseau, Esport = true,
                Name = "Désactiver l'algorithme de Nagle (TcpAckFrequency / TCPNoDelay)",
                Desc = "Envoi TCP immédiat sans regroupement de paquets : utile pour les jeux en ligne TCP. Sans effet sur les jeux en UDP.",
                BackupKeys = new[] { @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces" },
                Apply = () => Sys.SetNagle(true),
                Revert = () => Sys.SetNagle(false),
                Check = () => Sys.NagleActive()
            });

            list.Add(new Tweak
            {
                Id = "rsc_off", Category = Cat.Reseau, Esport = true,
                Name = "Désactiver la coalescence de segments réseau (RSC)",
                Desc = "Les paquets reçus sont traités immédiatement au lieu d'être regroupés : latence réseau réduite de quelques ms, léger surcoût CPU.",
                Apply = () => Sys.RunThrow(Sys.Sys32("netsh.exe"), "interface tcp set global rsc=disabled", "Désactivation du RSC"),
                Revert = () => Sys.RunThrow(Sys.Sys32("netsh.exe"), "interface tcp set global rsc=enabled", "Réactivation du RSC"),
                Check = () => Sys.RscDisabled()
            });

            list.Add(new Tweak
            {
                Id = "nic_power_off", Category = Cat.Reseau, Esport = true, Reboot = true,
                Name = "Empêcher Windows d'éteindre la carte réseau (économie d'énergie NIC)",
                Desc = "Coupe la mise en veille de l'adaptateur réseau : évite micro-coupures et pics de ping après une période calme.",
                Apply = () => Sys.SetNicPowerSaving(true),
                Revert = () => Sys.SetNicPowerSaving(false),
                Check = () => Sys.NicPowerDisabled()
            });

            list.Add(new Tweak
            {
                Id = "qos_reserve_off", Category = Cat.Reseau, Esport = true,
                Name = "Libérer la bande passante réservée par QoS (20 %)",
                Desc = "Windows réserve 20 % de la bande passante pour QoS. Ce réglage la libère entièrement. « Rétablir » remet le comportement par défaut.",
                BackupKeys = new[] { @"HKLM\SOFTWARE\Policies\Microsoft\Windows\Psched" },
                Apply  = () => Sys.SetMachine(@"SOFTWARE\Policies\Microsoft\Windows\Psched", "NonBestEffortLimit", 0, RegistryValueKind.DWord),
                Revert = () => Sys.DelMachine(@"SOFTWARE\Policies\Microsoft\Windows\Psched", "NonBestEffortLimit"),
                Check  = () => Sys.IntEquals(Sys.GetMachine(@"SOFTWARE\Policies\Microsoft\Windows\Psched", "NonBestEffortLimit"), 0)
            });

            list.Add(new Tweak
            {
                Id = "ephemeral_ports", Category = Cat.Reseau,
                Name = "Plus de ports réseau + réutilisation plus rapide (jeux en ligne)",
                Desc = "Augmente le nombre de ports sortants (MaxUserPort=65534) et réduit le délai avant réutilisation (TcpTimedWaitDelay=30 s). Utile quand beaucoup de connexions s'ouvrent.",
                Reboot = true,
                BackupKeys = new[] { @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters" },
                Apply = () =>
                {
                    Sys.SetMachine(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "MaxUserPort", 65534, RegistryValueKind.DWord);
                    Sys.SetMachine(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "TcpTimedWaitDelay", 30, RegistryValueKind.DWord);
                },
                Revert = () =>
                {
                    Sys.DelMachine(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "MaxUserPort");
                    Sys.DelMachine(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "TcpTimedWaitDelay");
                },
                Check = () => Sys.IntEquals(Sys.GetMachine(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "MaxUserPort"), 65534)
            });

            list.Add(new Tweak
            {
                Id = "dns_negative_cache_off", Category = Cat.Reseau, Esport = true,
                Name = "Ne pas mémoriser les échecs DNS (réessai immédiat)",
                Desc = "Windows garde en cache les résolutions DNS échouées pendant 5 s. Ce réglage les oublie aussitôt (MaxNegativeCacheTtl=0) : une résolution qui a raté un instant est retentée tout de suite au lieu d'échouer 5 s. « Rétablir » remet le comportement par défaut.",
                BackupKeys = new[] { @"HKLM\SYSTEM\CurrentControlSet\Services\Dnscache\Parameters" },
                Apply = () =>
                {
                    Sys.SetMachine(@"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters", "MaxNegativeCacheTtl", 0, RegistryValueKind.DWord);
                    Sys.SetMachine(@"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters", "NegativeCacheTime", 0, RegistryValueKind.DWord);
                },
                Revert = () =>
                {
                    Sys.DelMachine(@"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters", "MaxNegativeCacheTtl");
                    Sys.DelMachine(@"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters", "NegativeCacheTime");
                },
                Check = () => Sys.IntEquals(Sys.GetMachine(@"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters", "MaxNegativeCacheTtl"), 0)
            });

            list.Add(new Tweak
            {
                Id = "ipv6_tunnels_off", Category = Cat.Reseau, Esport = true, Reboot = true,
                Name = "Désactiver les tunnels IPv6 (Teredo / 6to4 / ISATAP)",
                Desc = "Coupe les interfaces de tunnel IPv6 (Teredo, 6to4, ISATAP) souvent inutiles et sources de latence/instabilité, tout en gardant l'IPv6 natif et l'IPv4 intacts (DisabledComponents=0x01). « Rétablir » réactive les tunnels.",
                BackupKeys = new[] { @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters" },
                Apply  = () => Sys.SetMachine(@"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters", "DisabledComponents", 1, RegistryValueKind.DWord),
                Revert = () => Sys.DelMachine(@"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters", "DisabledComponents"),
                Check  = () => Sys.IntEquals(Sys.GetMachine(@"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters", "DisabledComponents"), 1)
            });

            list.Add(new Tweak
            {
                Id = "smb_throttle_off", Category = Cat.Reseau, Reboot = true,
                Name = "SMB : débit maximal sur le réseau local (NAS / partages)",
                Desc = "Désactive le bridage de bande passante SMB (DisableBandwidthThrottling=1) pour de meilleurs débits vers un NAS ou un partage Windows sur le LAN. Sans effet si tu ne fais pas de transfert de fichiers réseau. « Rétablir » remet le comportement par défaut.",
                BackupKeys = new[] { @"HKLM\SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters" },
                Apply  = () => Sys.SetMachine(@"SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters", "DisableBandwidthThrottling", 1, RegistryValueKind.DWord),
                Revert = () => Sys.DelMachine(@"SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters", "DisableBandwidthThrottling"),
                Check  = () => Sys.IntEquals(Sys.GetMachine(@"SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters", "DisableBandwidthThrottling"), 1)
            });

            list.Add(new Tweak
            {
                Id = "hung_timeout", Category = Cat.Rapidite,
                Name = "Fermer plus vite les applications qui ne répondent pas",
                Desc = "Réduit les délais avant que Windows considère une appli figée (HungAppTimeout, WaitToKillAppTimeout). Récupération plus rapide en cas de blocage.",
                BackupKeys = new[] { @"HKCU\Control Panel\Desktop" },
                Apply = () =>
                {
                    Sys.SetUser(@"Control Panel\Desktop", "HungAppTimeout", "1000", RegistryValueKind.String);
                    Sys.SetUser(@"Control Panel\Desktop", "WaitToKillAppTimeout", "2000", RegistryValueKind.String);
                },
                Revert = () =>
                {
                    Sys.SetUser(@"Control Panel\Desktop", "HungAppTimeout", "5000", RegistryValueKind.String);
                    Sys.SetUser(@"Control Panel\Desktop", "WaitToKillAppTimeout", "20000", RegistryValueKind.String);
                },
                Check = () => Sys.StrEquals(Sys.GetUser(@"Control Panel\Desktop", "HungAppTimeout"), "1000")
            });

            list.Add(new Tweak
            {
                Id = "explorer_separate", Category = Cat.Rapidite,
                Name = "Explorateur : fenêtres de dossiers dans des processus séparés",
                Desc = "Une fenêtre de l'Explorateur qui plante n'entraîne plus les autres. Interface plus stable, coût mémoire léger.",
                BackupKeys = new[] { @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced" },
                Apply  = () => Sys.SetUser(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "SeparateProcess", 1, RegistryValueKind.DWord),
                Revert = () => Sys.SetUser(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "SeparateProcess", 0, RegistryValueKind.DWord),
                Check  = () => Sys.IntEquals(Sys.GetUser(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "SeparateProcess"), 1)
            });

            list.Add(new Tweak
            {
                Id = "storage_sense_off", Category = Cat.Rapidite,
                Name = "Désactiver l'Assistant Stockage (Storage Sense)",
                Desc = "Empêche le nettoyage automatique en arrière-plan. Sans effet sur l'espace disque tant que tu ne nettoies pas toi-même.",
                BackupKeys = new[] { @"HKLM\SOFTWARE\Policies\Microsoft\Windows\StorageSense" },
                Apply  = () => Sys.SetMachine(@"SOFTWARE\Policies\Microsoft\Windows\StorageSense", "AllowStorageSenseGlobal", 0, RegistryValueKind.DWord),
                Revert = () => Sys.DelMachine(@"SOFTWARE\Policies\Microsoft\Windows\StorageSense", "AllowStorageSenseGlobal"),
                Check  = () => Sys.IntEquals(Sys.GetMachine(@"SOFTWARE\Policies\Microsoft\Windows\StorageSense", "AllowStorageSenseGlobal"), 0)
            });

            list.Add(new Tweak
            {
                Id = "wu_reboot_off", Category = Cat.Systeme,
                Name = "Empêcher le redémarrage auto de Windows Update en session",
                Desc = "Windows ne redémarre plus tout seul pour les mises à jour tant qu'un utilisateur est connecté (fini le reboot en pleine partie).",
                BackupKeys = new[] { @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU" },
                Apply  = () => Sys.SetMachine(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoRebootWithLoggedOnUsers", 1, RegistryValueKind.DWord),
                Revert = () => Sys.DelMachine(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoRebootWithLoggedOnUsers"),
                Check  = () => Sys.IntEquals(Sys.GetMachine(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoRebootWithLoggedOnUsers"), 1)
            });

            list.Add(new Tweak
            {
                Id = "ceip_off", Category = Cat.Privacy, Esport = true,
                Name = "Désactiver le programme d'amélioration (CEIP) et ses tâches",
                Desc = "Coupe la collecte « expérience utilisateur » et ses tâches planifiées en arrière-plan.",
                BackupKeys = new[] { @"HKLM\SOFTWARE\Microsoft\SQMClient\Windows" },
                Apply = () =>
                {
                    Sys.SetMachine(@"SOFTWARE\Microsoft\SQMClient\Windows", "CEIPEnable", 0, RegistryValueKind.DWord);
                    Sys.SetScheduledTask(@"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator", false);
                    Sys.SetScheduledTask(@"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip", false);
                    Sys.SetScheduledTask(@"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser", false);
                },
                Revert = () =>
                {
                    Sys.SetMachine(@"SOFTWARE\Microsoft\SQMClient\Windows", "CEIPEnable", 1, RegistryValueKind.DWord);
                    Sys.SetScheduledTask(@"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator", true);
                    Sys.SetScheduledTask(@"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip", true);
                    Sys.SetScheduledTask(@"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser", true);
                },
                Check = () => Sys.IntEquals(Sys.GetMachine(@"SOFTWARE\Microsoft\SQMClient\Windows", "CEIPEnable"), 0)
            });

            list.Add(new Tweak
            {
                Id = "app_tracking_off", Category = Cat.Privacy, Esport = true,
                Name = "Ne plus suivre les applications lancées",
                Desc = "Windows n'enregistre plus quelles applis tu ouvres pour « personnaliser » Démarrer. Menu Démarrer plus léger.",
                BackupKeys = new[] { @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced" },
                Apply  = () => Sys.SetUser(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_TrackProgs", 0, RegistryValueKind.DWord),
                Revert = () => Sys.SetUser(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_TrackProgs", 1, RegistryValueKind.DWord),
                Check  = () => Sys.IntEquals(Sys.GetUser(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_TrackProgs"), 0)
            });

            list.Add(new Tweak
            {
                Id = "taskbar_anim_off", Category = Cat.Rapidite,
                Name = "Désactiver les animations de la barre des tâches et des fenêtres",
                Desc = "Interface plus sèche et plus rapide (ouverture/minimisation instantanées). Purement visuel.",
                BackupKeys = new[] { @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced" },
                Apply  = () => Sys.SetUser(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAnimations", 0, RegistryValueKind.DWord),
                Revert = () => Sys.SetUser(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAnimations", 1, RegistryValueKind.DWord),
                Check  = () => Sys.IntEquals(Sys.GetUser(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAnimations"), 0)
            });

            list.Add(new Tweak
            {
                Id = "search_highlights_off", Category = Cat.Privacy,
                Name = "Désactiver les « contenus dynamiques » de la recherche",
                Desc = "Supprime les images/suggestions animées dans la zone de recherche : moins de réseau et de fond.",
                BackupKeys = new[] { @"HKCU\Software\Microsoft\Windows\CurrentVersion\SearchSettings" },
                Apply  = () => Sys.SetUser(@"Software\Microsoft\Windows\CurrentVersion\SearchSettings", "IsDynamicSearchBoxEnabled", 0, RegistryValueKind.DWord),
                Revert = () => Sys.SetUser(@"Software\Microsoft\Windows\CurrentVersion\SearchSettings", "IsDynamicSearchBoxEnabled", 1, RegistryValueKind.DWord),
                Check  = () => Sys.IntEquals(Sys.GetUser(@"Software\Microsoft\Windows\CurrentVersion\SearchSettings", "IsDynamicSearchBoxEnabled"), 0)
            });

            list.Add(new Tweak
            {
                Id = "mouse_hover_taskbar", Category = Cat.Rapidite,
                Name = "Aperçus de la barre des tâches instantanés",
                Desc = "Réduit le délai avant l'affichage des vignettes/aperçus au survol de la barre des tâches (ExtendedUIHoverTime).",
                BackupKeys = new[] { @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced" },
                Apply  = () => Sys.SetUser(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ExtendedUIHoverTime", 100, RegistryValueKind.DWord),
                Revert = () => Sys.DelUser(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ExtendedUIHoverTime"),
                Check  = () => Sys.IntEquals(Sys.GetUser(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ExtendedUIHoverTime"), 100)
            });

            list.Add(new Tweak
            {
                Id = "foreground_lock", Category = Cat.Systeme, Esport = true,
                Name = "Focus immédiat de l'application au premier plan (ForegroundLockTimeout = 0)",
                Desc = "Supprime le délai avant qu'une appli puisse prendre le premier plan : alt-tab et retour au jeu plus francs.",
                BackupKeys = new[] { @"HKCU\Control Panel\Desktop" },
                Apply  = () => Sys.SetUser(@"Control Panel\Desktop", "ForegroundLockTimeout", 0, RegistryValueKind.DWord),
                Revert = () => Sys.SetUser(@"Control Panel\Desktop", "ForegroundLockTimeout", 200000, RegistryValueKind.DWord),
                Check  = () => Sys.IntEquals(Sys.GetUser(@"Control Panel\Desktop", "ForegroundLockTimeout"), 0)
            });

            list.Add(new Tweak
            {
                Id = "long_paths", Category = Cat.Systeme, Reboot = true,
                Name = "Activer les chemins de fichiers longs (Win32 > 260 caractères)",
                Desc = "Autorise les chemins longs pour les jeux/outils qui installent dans des arborescences profondes. Sans effet négatif.",
                BackupKeys = new[] { @"HKLM\SYSTEM\CurrentControlSet\Control\FileSystem" },
                Apply  = () => Sys.SetMachine(@"SYSTEM\CurrentControlSet\Control\FileSystem", "LongPathsEnabled", 1, RegistryValueKind.DWord),
                Revert = () => Sys.SetMachine(@"SYSTEM\CurrentControlSet\Control\FileSystem", "LongPathsEnabled", 0, RegistryValueKind.DWord),
                Check  = () => Sys.IntEquals(Sys.GetMachine(@"SYSTEM\CurrentControlSet\Control\FileSystem", "LongPathsEnabled"), 1)
            });

            list.Add(new Tweak
            {
                Id = "tailored_experiences_off", Category = Cat.Privacy, Recommended = true, Esport = true,
                Name = "Désactiver les « expériences personnalisées » (pubs ciblées)",
                Desc = "Windows n'utilise plus tes données de diagnostic pour afficher des conseils/pubs personnalisés.",
                BackupKeys = new[] { @"HKCU\Software\Microsoft\Windows\CurrentVersion\Privacy" },
                Apply  = () => Sys.SetUser(@"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0, RegistryValueKind.DWord),
                Revert = () => Sys.DelUser(@"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled"),
                Check  = () => Sys.IntEquals(Sys.GetUser(@"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled"), 0)
            });

            list.Add(new Tweak
            {
                Id = "suggested_content_off", Category = Cat.Privacy,
                Name = "Désactiver les suggestions dans les Paramètres",
                Desc = "Supprime les « contenus suggérés » (bannières/astuces) affichés dans l'application Paramètres.",
                BackupKeys = new[] { @"HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager" },
                Apply = () =>
                {
                    string cdm = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
                    Sys.SetUser(cdm, "SubscribedContent-338393Enabled", 0, RegistryValueKind.DWord);
                    Sys.SetUser(cdm, "SubscribedContent-353694Enabled", 0, RegistryValueKind.DWord);
                    Sys.SetUser(cdm, "SubscribedContent-353696Enabled", 0, RegistryValueKind.DWord);
                },
                Revert = () =>
                {
                    string cdm = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
                    Sys.DelUser(cdm, "SubscribedContent-338393Enabled");
                    Sys.DelUser(cdm, "SubscribedContent-353694Enabled");
                    Sys.DelUser(cdm, "SubscribedContent-353696Enabled");
                },
                Check = () => Sys.IntEquals(Sys.GetUser(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338393Enabled"), 0)
            });

            list.Add(new Tweak
            {
                Id = "lockscreen_tips_off", Category = Cat.Privacy,
                Name = "Désactiver les astuces/pubs de l'écran de verrouillage",
                Desc = "Plus de « faits amusants », astuces ou promotions sur l'écran de verrouillage.",
                BackupKeys = new[] { @"HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager" },
                Apply = () =>
                {
                    string cdm = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
                    Sys.SetUser(cdm, "RotatingLockScreenOverlayEnabled", 0, RegistryValueKind.DWord);
                    Sys.SetUser(cdm, "SubscribedContent-338387Enabled", 0, RegistryValueKind.DWord);
                },
                Revert = () =>
                {
                    string cdm = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
                    Sys.DelUser(cdm, "RotatingLockScreenOverlayEnabled");
                    Sys.DelUser(cdm, "SubscribedContent-338387Enabled");
                },
                Check = () => Sys.IntEquals(Sys.GetUser(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338387Enabled"), 0)
            });

            list.Add(new Tweak
            {
                Id = "wu_driver_exclude", Category = Cat.Systeme, Esport = true,
                Name = "Empêcher Windows Update de remplacer tes pilotes",
                Desc = "Windows Update n'installe plus de pilotes (GPU, etc.) par-dessus les tiens : fini les régressions de pilote NVIDIA/AMD après une mise à jour.",
                BackupKeys = new[] { @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate" },
                Apply  = () => Sys.SetMachine(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "ExcludeWUDriversInQualityUpdate", 1, RegistryValueKind.DWord),
                Revert = () => Sys.DelMachine(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "ExcludeWUDriversInQualityUpdate"),
                Check  = () => Sys.IntEquals(Sys.GetMachine(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "ExcludeWUDriversInQualityUpdate"), 1)
            });

            // ================= SON & AUDIO =================
            list.Add(new Tweak
            {
                Id = "audio_ducking_off", Category = Cat.Audio, Esport = true,
                Name = "Ne plus baisser le son des jeux pendant une « communication »",
                Desc = "Windows n'atténue plus automatiquement le volume des autres applis quand il détecte un appel/vocal (Discord, etc.).",
                BackupKeys = new[] { @"HKCU\Software\Microsoft\Multimedia\Audio" },
                Apply  = () => Sys.SetUser(@"Software\Microsoft\Multimedia\Audio", "UserDuckingPreference", 3, RegistryValueKind.DWord),
                Revert = () => Sys.DelUser(@"Software\Microsoft\Multimedia\Audio", "UserDuckingPreference"),
                Check  = () => Sys.IntEquals(Sys.GetUser(@"Software\Microsoft\Multimedia\Audio", "UserDuckingPreference"), 3)
            });

            list.Add(new Tweak
            {
                Id = "audio_mmcss_high", Category = Cat.Audio, Esport = true, Reboot = true,
                Name = "Priorité MMCSS de la tâche « Audio » → Haute (moins de coupures)",
                Desc = "Le planificateur multimédia (MMCSS) traite le flux audio en priorité Haute et E/S Haute au lieu de Moyenne. Réduit les micro-coupures / crépitements audio quand le CPU est chargé (jeu + Discord + stream). Réversible (valeurs Windows par défaut restaurées).",
                BackupKeys = new[] { @"HKLM\" + AudioKey },
                Apply = () =>
                {
                    Sys.SetMachine(AudioKey, "Scheduling Category", "High", RegistryValueKind.String);
                    Sys.SetMachine(AudioKey, "SFIO Priority", "High", RegistryValueKind.String);
                },
                Revert = () =>
                {
                    Sys.SetMachine(AudioKey, "Scheduling Category", "Medium", RegistryValueKind.String);
                    Sys.SetMachine(AudioKey, "SFIO Priority", "Normal", RegistryValueKind.String);
                },
                Check = () => Sys.StrEquals(Sys.GetMachine(AudioKey, "Scheduling Category"), "High")
            });

            list.Add(new Tweak
            {
                Id = "audio_startup_sound_off", Category = Cat.Audio,
                Name = "Désactiver le son de démarrage de Windows",
                Desc = "Coupe le jingle joué à l'ouverture de session. Purement cosmétique, sans effet sur le reste du son.",
                BackupKeys = new[] { @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\LogonUI\BootAnimation" },
                Apply  = () => Sys.SetMachine(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\LogonUI\BootAnimation", "DisableStartupSound", 1, RegistryValueKind.DWord),
                Revert = () => Sys.SetMachine(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\LogonUI\BootAnimation", "DisableStartupSound", 0, RegistryValueKind.DWord),
                Check  = () => Sys.IntEquals(Sys.GetMachine(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\LogonUI\BootAnimation", "DisableStartupSound"), 1)
            });

            list.Add(new Tweak
            {
                Id = "audio_bt_absolute_volume_off", Category = Cat.Audio, Reboot = true,
                Name = "Bluetooth : désactiver le « volume absolu » (contrôle fin du casque)",
                Desc = "Sépare le volume Windows de celui du casque Bluetooth. Utile si ton casque BT saute directement de trop bas à trop fort, ou si le réglage est trop grossier. Sans effet si tu n'utilises pas d'audio Bluetooth.",
                BackupKeys = new[] { @"HKLM\SYSTEM\CurrentControlSet\Control\Bluetooth\Audio\AVRCP\CT" },
                Apply  = () => Sys.SetMachine(@"SYSTEM\CurrentControlSet\Control\Bluetooth\Audio\AVRCP\CT", "DisableAbsoluteVolume", 1, RegistryValueKind.DWord),
                Revert = () => Sys.SetMachine(@"SYSTEM\CurrentControlSet\Control\Bluetooth\Audio\AVRCP\CT", "DisableAbsoluteVolume", 0, RegistryValueKind.DWord),
                Check  = () => Sys.IntEquals(Sys.GetMachine(@"SYSTEM\CurrentControlSet\Control\Bluetooth\Audio\AVRCP\CT", "DisableAbsoluteVolume"), 1)
            });

            list.Add(new Tweak
            {
                Id = "consumer_features_off", Category = Cat.Privacy, Recommended = true, Esport = true,
                Name = "Bloquer l'installation automatique d'applications promues",
                Desc = "Windows n'installe plus tout seul de jeux/applis sponsorisés (Candy Crush & co.).",
                BackupKeys = new[] { @"HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent" },
                Apply  = () => Sys.SetMachine(@"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures", 1, RegistryValueKind.DWord),
                Revert = () => Sys.DelMachine(@"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures"),
                Check  = () => Sys.IntEquals(Sys.GetMachine(@"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures"), 1)
            });

            list.Add(new Tweak
            {
                Id = "explorer_ads_off", Category = Cat.Privacy,
                Name = "Supprimer les pubs OneDrive/Office dans l'Explorateur",
                Desc = "Plus de « notifications du fournisseur de synchronisation » (promotions OneDrive/Office 365) dans les fenêtres de fichiers.",
                BackupKeys = new[] { @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced" },
                Apply  = () => Sys.SetUser(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowSyncProviderNotifications", 0, RegistryValueKind.DWord),
                Revert = () => Sys.SetUser(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowSyncProviderNotifications", 1, RegistryValueKind.DWord),
                Check  = () => Sys.IntEquals(Sys.GetUser(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowSyncProviderNotifications"), 0)
            });

            list.Add(new Tweak
            {
                Id = "quick_access_off", Category = Cat.Rapidite,
                Name = "Explorateur : ne plus lister les fichiers récents/fréquents",
                Desc = "Accès rapide plus léger et plus privé : Windows n'affiche/n'enregistre plus les fichiers et dossiers récemment ouverts.",
                BackupKeys = new[] { @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer" },
                Apply = () =>
                {
                    Sys.SetUser(@"Software\Microsoft\Windows\CurrentVersion\Explorer", "ShowRecent", 0, RegistryValueKind.DWord);
                    Sys.SetUser(@"Software\Microsoft\Windows\CurrentVersion\Explorer", "ShowFrequent", 0, RegistryValueKind.DWord);
                },
                Revert = () =>
                {
                    Sys.SetUser(@"Software\Microsoft\Windows\CurrentVersion\Explorer", "ShowRecent", 1, RegistryValueKind.DWord);
                    Sys.SetUser(@"Software\Microsoft\Windows\CurrentVersion\Explorer", "ShowFrequent", 1, RegistryValueKind.DWord);
                },
                Check = () => Sys.IntEquals(Sys.GetUser(@"Software\Microsoft\Windows\CurrentVersion\Explorer", "ShowRecent"), 0)
            });

            list.Add(new Tweak
            {
                Id = "auto_maintenance_off", Category = Cat.Systeme,
                Name = "Désactiver la maintenance automatique de Windows",
                Desc = "Windows ne lance plus ses tâches de maintenance (défrag, analyses…) en arrière-plan de façon imprévisible. À relancer manuellement au besoin.",
                BackupKeys = new[] { @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\Maintenance" },
                Apply  = () => Sys.SetMachine(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\Maintenance", "MaintenanceDisabled", 1, RegistryValueKind.DWord),
                Revert = () => Sys.DelMachine(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\Maintenance", "MaintenanceDisabled"),
                Check  = () => Sys.IntEquals(Sys.GetMachine(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\Maintenance", "MaintenanceDisabled"), 1)
            });

            list.Add(new Tweak
            {
                Id = "fth_off", Category = Cat.Systeme,
                Name = "Désactiver le tas tolérant aux pannes (Fault Tolerant Heap)",
                Desc = "Supprime une couche de compatibilité qui peut ralentir certaines applications qui ont planté par le passé. « Rétablir » le réactive.",
                BackupKeys = new[] { @"HKLM\SOFTWARE\Microsoft\FTH" },
                Apply  = () => Sys.SetMachine(@"SOFTWARE\Microsoft\FTH", "Enabled", 0, RegistryValueKind.DWord),
                Revert = () => Sys.SetMachine(@"SOFTWARE\Microsoft\FTH", "Enabled", 1, RegistryValueKind.DWord),
                Check  = () => Sys.IntEquals(Sys.GetMachine(@"SOFTWARE\Microsoft\FTH", "Enabled"), 0)
            });

            list.Add(new Tweak
            {
                Id = "aero_shake_off", Category = Cat.Rapidite,
                Name = "Désactiver « Aero Shake » (secouer pour réduire les fenêtres)",
                Desc = "Empêche la réduction accidentelle de toutes les fenêtres quand tu bouges vite une fenêtre.",
                BackupKeys = new[] { @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced" },
                Apply  = () => Sys.SetUser(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "DisallowShaking", 1, RegistryValueKind.DWord),
                Revert = () => Sys.DelUser(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "DisallowShaking"),
                Check  = () => Sys.IntEquals(Sys.GetUser(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "DisallowShaking"), 1)
            });

            // ===================================================================
            //  BLOC AVANCÉ (« zéro limite ») — tout réversible, sauvegardé
            // ===================================================================
            const string SubProc = "54533251-82be-4824-96c1-47b60b740d00";
            const string MemMgmt  = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";

            // ---- Alimentation & CPU (avancé) ----
            list.Add(new Tweak
            {
                Id = "proc_min_100", Category = Cat.Alim, Esport = true,
                Name = "État minimal du processeur à 100 % (pas de sous-cadençage)",
                Desc = "Le CPU reste à pleine fréquence au lieu de descendre puis remonter : supprime la latence de montée en régime. Consomme plus au repos. « Rétablir » remet 5 %.",
                Apply  = () => Sys.SetPowerValue(SubProc, "893dee8e-2bef-41e0-89c6-b55d0929964c", 100, 100),
                Revert = () => Sys.SetPowerValue(SubProc, "893dee8e-2bef-41e0-89c6-b55d0929964c", 5, 5),
                Check  = () => Sys.PowerAcEquals(SubProc, "893dee8e-2bef-41e0-89c6-b55d0929964c", 100)
            });

            list.Add(new Tweak
            {
                Id = "cpu_idle_disable", Category = Cat.Alim, Esport = true,
                Name = "Désactiver les états de repos du CPU (C-States) — EXPÉRIMENTAL",
                Desc = "Le CPU ne s'endort jamais : latence d'interruption minimale, mais chaleur/consommation en forte hausse. À réserver à un desktop bien refroidi. « Rétablir » réactive le repos.",
                Apply  = () => Sys.SetPowerValue(SubProc, "5d76a2ca-e8c0-402f-a133-2158492d58ad", 1, 1),
                Revert = () => Sys.SetPowerValue(SubProc, "5d76a2ca-e8c0-402f-a133-2158492d58ad", 0, 0),
                Check  = () => Sys.PowerAcEquals(SubProc, "5d76a2ca-e8c0-402f-a133-2158492d58ad", 1)
            });

            // ---- GPU & jeux (avancé) ----
            list.Add(new Tweak
            {
                Id = "gpu_msi", Category = Cat.Gpu, Esport = true, Reboot = true,
                Name = "Activer le MSI mode sur le GPU (interruptions par message)",
                Desc = "Le GPU utilise des interruptions modernes (MSI) au lieu des IRQ à ligne : réduit la latence et le stutter d'interruption. Standard sur GPU récents. « Rétablir » remet le défaut du pilote.",
                BackupKeys = new[] { @"HKLM\SYSTEM\CurrentControlSet\Enum\PCI" },
                Apply  = () => Sys.SetGpuMsi(true, null),
                Revert = () => Sys.SetGpuMsi(false, null),
                Check  = () => Sys.GpuMsiActive()
            });

            // ---- Système & planificateur (avancé / sécurité) ----
            list.Add(new Tweak
            {
                Id = "spectre_off", Category = Cat.Systeme, Reboot = true,
                Name = "Désactiver les mitigations Spectre/Meltdown — EXPÉRIMENTAL / SÉCURITÉ",
                Desc = "Gros gain CPU sur les processeurs anciens (ex : 9900K), MAIS réduit la protection contre les failles Spectre/Meltdown. À n'activer qu'en connaissance de cause. Entièrement réversible.",
                BackupKeys = new[] { @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management" },
                Apply = () =>
                {
                    Sys.SetMachine(MemMgmt, "FeatureSettingsOverride", 1, RegistryValueKind.DWord);
                    Sys.SetMachine(MemMgmt, "FeatureSettingsOverrideMask", 3, RegistryValueKind.DWord);
                },
                Revert = () =>
                {
                    Sys.DelMachine(MemMgmt, "FeatureSettingsOverride");
                    Sys.DelMachine(MemMgmt, "FeatureSettingsOverrideMask");
                },
                Check = () => Sys.IntEquals(Sys.GetMachine(MemMgmt, "FeatureSettingsOverride"), 1)
            });

            list.Add(new Tweak
            {
                Id = "vbs_off", Category = Cat.Systeme, Reboot = true,
                Name = "Désactiver VBS / Intégrité de la mémoire (HVCI) — SÉCURITÉ",
                Desc = "La sécurité basée sur la virtualisation coûte des performances en jeu (elle génère des DPC hyperviseur, visibles dans l'analyse de latence). La désactiver les récupère, au prix d'une protection noyau réduite. Réversible.",
                BackupKeys = new[] { @"HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard" },
                Apply = () =>
                {
                    Sys.SetMachine(@"SYSTEM\CurrentControlSet\Control\DeviceGuard", "EnableVirtualizationBasedSecurity", 0, RegistryValueKind.DWord);
                    Sys.SetMachine(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled", 0, RegistryValueKind.DWord);
                },
                Revert = () =>
                {
                    Sys.SetMachine(@"SYSTEM\CurrentControlSet\Control\DeviceGuard", "EnableVirtualizationBasedSecurity", 1, RegistryValueKind.DWord);
                    Sys.SetMachine(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled", 1, RegistryValueKind.DWord);
                },
                Check = () => Sys.IntEquals(Sys.GetMachine(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled"), 0)
            });

            list.Add(new Tweak
            {
                Id = "prefetch_off", Category = Cat.Systeme,
                Name = "Désactiver Prefetch/Superfetch (registre) — SSD uniquement",
                Desc = "Coupe le préchargement disque au niveau noyau. Sur SSD : moins d'écritures, aucun intérêt de préchargement. Sur disque mécanique : à ÉVITER. « Rétablir » remet la valeur 3.",
                BackupKeys = new[] { MemMgmt + @"\PrefetchParameters" },
                Apply = () =>
                {
                    Sys.SetMachine(MemMgmt + @"\PrefetchParameters", "EnablePrefetcher", 0, RegistryValueKind.DWord);
                    Sys.SetMachine(MemMgmt + @"\PrefetchParameters", "EnableSuperfetch", 0, RegistryValueKind.DWord);
                },
                Revert = () =>
                {
                    Sys.SetMachine(MemMgmt + @"\PrefetchParameters", "EnablePrefetcher", 3, RegistryValueKind.DWord);
                    Sys.SetMachine(MemMgmt + @"\PrefetchParameters", "EnableSuperfetch", 3, RegistryValueKind.DWord);
                },
                Check = () => Sys.IntEquals(Sys.GetMachine(MemMgmt + @"\PrefetchParameters", "EnablePrefetcher"), 0)
            });

            list.Add(new Tweak
            {
                Id = "lastaccess_off", Category = Cat.Systeme,
                Name = "Désactiver l'horodatage « dernier accès » NTFS",
                Desc = "Windows n'écrit plus la date de dernier accès à chaque lecture de fichier : moins d'écritures disque. « Rétablir » remet le mode géré par le système.",
                BackupKeys = new[] { @"HKLM\SYSTEM\CurrentControlSet\Control\FileSystem" },
                Apply  = () => Sys.RunThrow(Sys.Sys32("fsutil.exe"), "behavior set disablelastaccess 1", "NTFS last-access off"),
                Revert = () => Sys.RunThrow(Sys.Sys32("fsutil.exe"), "behavior set disablelastaccess 2", "NTFS last-access system-managed"),
                Check  = () =>
                {
                    object v = Sys.GetMachine(@"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsDisableLastAccessUpdate");
                    return (v is int) && (((int)v) == 1 || ((int)v) == 3);
                }
            });

            // ---- Confidentialité ----
            list.Add(new Tweak
            {
                Id = "advertising_id_off", Category = Cat.Privacy, Recommended = true, Esport = true,
                Name = "Désactiver l'identifiant de publicité",
                Desc = "Coupe l'ID publicitaire utilisé pour le suivi entre applications.",
                BackupKeys = new[] { @"HKLM\SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo" },
                Apply  = () => Sys.SetMachine(@"SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo", "DisabledByGroupPolicy", 1, RegistryValueKind.DWord),
                Revert = () => Sys.DelMachine(@"SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo", "DisabledByGroupPolicy"),
                Check  = () => Sys.IntEquals(Sys.GetMachine(@"SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo", "DisabledByGroupPolicy"), 1)
            });

            list.Add(new Tweak
            {
                Id = "telemetry_policy", Category = Cat.Privacy, Recommended = true, Esport = true,
                Name = "Télémétrie au minimum (stratégie AllowTelemetry = 0)",
                Desc = "Réduit la collecte de données de diagnostic au niveau stratégie (complète la désactivation du service DiagTrack).",
                BackupKeys = new[] { @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection" },
                Apply  = () => Sys.SetMachine(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0, RegistryValueKind.DWord),
                Revert = () => Sys.DelMachine(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry"),
                Check  = () => Sys.IntEquals(Sys.GetMachine(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry"), 0)
            });

            list.Add(new Tweak
            {
                Id = "activity_history_off", Category = Cat.Privacy, Esport = true,
                Name = "Désactiver l'historique d'activité / Timeline",
                Desc = "Windows n'enregistre ni n'envoie plus l'historique de tes activités.",
                BackupKeys = new[] { @"HKLM\SOFTWARE\Policies\Microsoft\Windows\System" },
                Apply = () =>
                {
                    Sys.SetMachine(@"SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed", 0, RegistryValueKind.DWord);
                    Sys.SetMachine(@"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0, RegistryValueKind.DWord);
                    Sys.SetMachine(@"SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", 0, RegistryValueKind.DWord);
                },
                Revert = () =>
                {
                    Sys.DelMachine(@"SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed");
                    Sys.DelMachine(@"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities");
                    Sys.DelMachine(@"SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities");
                },
                Check = () => Sys.IntEquals(Sys.GetMachine(@"SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed"), 0)
            });

            list.Add(new Tweak
            {
                Id = "location_off", Category = Cat.Privacy,
                Name = "Désactiver le service de localisation",
                Desc = "Bloque l'accès à la localisation pour le système et les applications.",
                BackupKeys = new[] { @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location" },
                Apply  = () => Sys.SetMachine(@"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location", "Value", "Deny", RegistryValueKind.String),
                Revert = () => Sys.SetMachine(@"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location", "Value", "Allow", RegistryValueKind.String),
                Check  = () => Sys.StrEquals(Sys.GetMachine(@"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location", "Value"), "Deny")
            });

            list.Add(new Tweak
            {
                Id = "feedback_off", Category = Cat.Privacy,
                Name = "Ne plus demander de commentaires (feedback Windows)",
                Desc = "Windows ne t'interrompt plus pour demander ton avis.",
                BackupKeys = new[] { @"HKCU\Software\Microsoft\Siuf\Rules" },
                Apply  = () => Sys.SetUser(@"Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0, RegistryValueKind.DWord),
                Revert = () => Sys.DelUser(@"Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod"),
                Check  = () => Sys.IntEquals(Sys.GetUser(@"Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod"), 0)
            });

            list.Add(new Tweak
            {
                Id = "cortana_off", Category = Cat.Privacy,
                Name = "Désactiver Cortana",
                Desc = "Désactive l'assistant Cortana au niveau stratégie.",
                BackupKeys = new[] { @"HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search" },
                Apply  = () => Sys.SetMachine(@"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana", 0, RegistryValueKind.DWord),
                Revert = () => Sys.DelMachine(@"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana"),
                Check  = () => Sys.IntEquals(Sys.GetMachine(@"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana"), 0)
            });

            // ---- Services & arrière-plan (avancé) ----
            list.Add(new Tweak
            {
                Id = "xbox_services_off", Category = Cat.Services,
                Name = "Désactiver les services Xbox (si tu ne joues pas à des jeux Xbox/PC Game Pass)",
                Desc = "Arrête les services Xbox Live/Game Save. À ÉVITER si tu utilises le Game Pass ou des jeux du Microsoft Store. Réversible.",
                Apply = () =>
                {
                    Sys.ConfigureService("XblAuthManager", "disabled", true, false);
                    Sys.ConfigureService("XblGameSave", "disabled", true, false);
                    Sys.ConfigureService("XboxGipSvc", "disabled", true, false);
                    Sys.ConfigureService("XboxNetApiSvc", "disabled", true, false);
                },
                Revert = () =>
                {
                    Sys.ConfigureService("XblAuthManager", "demand", false, false);
                    Sys.ConfigureService("XblGameSave", "demand", false, false);
                    Sys.ConfigureService("XboxGipSvc", "demand", false, false);
                    Sys.ConfigureService("XboxNetApiSvc", "demand", false, false);
                },
                Check = () => Sys.ServiceDisabled("XblAuthManager")
            });

            list.Add(new Tweak
            {
                Id = "mapsbroker_off", Category = Cat.Services,
                Name = "Désactiver le service des cartes hors ligne (MapsBroker)",
                Desc = "Coupe le téléchargement/mise à jour des cartes hors ligne en arrière-plan. Sans effet si tu n'utilises pas l'appli Cartes.",
                Apply  = () => Sys.ConfigureService("MapsBroker", "disabled", true, false),
                Revert = () => Sys.ConfigureService("MapsBroker", "delayed-auto", false, false),
                Check  = () => Sys.ServiceDisabled("MapsBroker")
            });

            list.Add(new Tweak
            {
                Id = "remote_registry_off", Category = Cat.Services,
                Name = "Désactiver le Registre à distance (surface d'attaque)",
                Desc = "Empêche la modification du registre depuis le réseau. Recommandé pour un PC personnel.",
                Apply  = () => Sys.ConfigureService("RemoteRegistry", "disabled", true, false),
                Revert = () => Sys.ConfigureService("RemoteRegistry", "demand", false, false),
                Check  = () => Sys.ServiceDisabled("RemoteRegistry")
            });

            // ================= LOT SUPPLÉMENTAIRE =================

            list.Add(new Tweak
            {
                Id = "filter_toggle_keys_off", Category = Cat.Souris, Recommended = true, Esport = true,
                Name = "Empêcher les touches Filtres/Bascules (pop-ups accidentels en jeu)",
                Desc = "Désactive « Touches filtres » et « Touches bascules » : plus de fenêtre parasite ni de bip quand tu maintiens Maj ou appuies vite en jeu. « Rétablir » remet les valeurs Windows.",
                BackupKeys = new[] { @"HKCU\Control Panel\Accessibility\Keyboard Response", @"HKCU\Control Panel\Accessibility\ToggleKeys" },
                Apply = () =>
                {
                    Sys.SetUser(@"Control Panel\Accessibility\Keyboard Response", "Flags", "122", RegistryValueKind.String);
                    Sys.SetUser(@"Control Panel\Accessibility\ToggleKeys", "Flags", "38", RegistryValueKind.String);
                },
                Revert = () =>
                {
                    Sys.SetUser(@"Control Panel\Accessibility\Keyboard Response", "Flags", "126", RegistryValueKind.String);
                    Sys.SetUser(@"Control Panel\Accessibility\ToggleKeys", "Flags", "62", RegistryValueKind.String);
                },
                Check = () => Sys.StrEquals(Sys.GetUser(@"Control Panel\Accessibility\Keyboard Response", "Flags"), "122")
            });

            list.Add(new Tweak
            {
                Id = "reserved_storage_off", Category = Cat.Rapidite, Reboot = true,
                Name = "Libérer le stockage réservé de Windows (~7 Go)",
                Desc = "Indique à Windows de ne plus réserver d'espace disque pour les mises à jour (ShippedWithReserves=0). Effet complet après la prochaine mise à jour cumulative. « Rétablir » réactive la réserve.",
                BackupKeys = new[] { @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\ReserveManager" },
                Apply  = () => Sys.SetMachine(@"SOFTWARE\Microsoft\Windows\CurrentVersion\ReserveManager", "ShippedWithReserves", 0, RegistryValueKind.DWord),
                Revert = () => Sys.SetMachine(@"SOFTWARE\Microsoft\Windows\CurrentVersion\ReserveManager", "ShippedWithReserves", 1, RegistryValueKind.DWord),
                Check  = () => Sys.IntEquals(Sys.GetMachine(@"SOFTWARE\Microsoft\Windows\CurrentVersion\ReserveManager", "ShippedWithReserves"), 0)
            });

            list.Add(new Tweak
            {
                Id = "taskbar_end_task", Category = Cat.Rapidite,
                Name = "Ajouter « Fin de tâche » au clic droit sur la barre des tâches (Win11)",
                Desc = "Active l'option développeur « Fin de tâche » : tuer une appli figée directement depuis sa vignette dans la barre des tâches, sans passer par le Gestionnaire des tâches.",
                BackupKeys = new[] { @"HKCU\Software\Microsoft\Windows\CurrentVersion\TaskbarDeveloperSettings" },
                Apply  = () => Sys.SetUser(@"Software\Microsoft\Windows\CurrentVersion\TaskbarDeveloperSettings", "TaskbarEndTask", 1, RegistryValueKind.DWord),
                Revert = () => Sys.SetUser(@"Software\Microsoft\Windows\CurrentVersion\TaskbarDeveloperSettings", "TaskbarEndTask", 0, RegistryValueKind.DWord),
                Check  = () => Sys.IntEquals(Sys.GetUser(@"Software\Microsoft\Windows\CurrentVersion\TaskbarDeveloperSettings", "TaskbarEndTask"), 1)
            });

            list.Add(new Tweak
            {
                Id = "online_speech_off", Category = Cat.Privacy, Recommended = true,
                Name = "Désactiver la reconnaissance vocale en ligne",
                Desc = "Empêche l'envoi de ta voix aux services cloud Microsoft pour la reconnaissance vocale. La dictée hors-ligne reste possible. « Rétablir » réactive.",
                BackupKeys = new[] { @"HKCU\Software\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy" },
                Apply  = () => Sys.SetUser(@"Software\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy", "HasAccepted", 0, RegistryValueKind.DWord),
                Revert = () => Sys.SetUser(@"Software\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy", "HasAccepted", 1, RegistryValueKind.DWord),
                Check  = () => Sys.IntEquals(Sys.GetUser(@"Software\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy", "HasAccepted"), 0)
            });

            list.Add(new Tweak
            {
                Id = "inking_personalization_off", Category = Cat.Privacy, Recommended = true,
                Name = "Désactiver la personnalisation saisie/manuscrite (collecte de frappe)",
                Desc = "Windows n'analyse plus ce que tu tapes/écris pour « personnaliser » (collecte implicite de texte et de contacts). « Rétablir » remet les valeurs par défaut.",
                BackupKeys = new[] { @"HKCU\Software\Microsoft\InputPersonalization", @"HKCU\Software\Microsoft\Personalization\Settings" },
                Apply = () =>
                {
                    Sys.SetUser(@"Software\Microsoft\InputPersonalization", "RestrictImplicitInkCollection", 1, RegistryValueKind.DWord);
                    Sys.SetUser(@"Software\Microsoft\InputPersonalization", "RestrictImplicitTextCollection", 1, RegistryValueKind.DWord);
                    Sys.SetUser(@"Software\Microsoft\InputPersonalization\TrainedDataStore", "HarvestContacts", 0, RegistryValueKind.DWord);
                    Sys.SetUser(@"Software\Microsoft\Personalization\Settings", "AcceptedPrivacyPolicy", 0, RegistryValueKind.DWord);
                },
                Revert = () =>
                {
                    Sys.SetUser(@"Software\Microsoft\InputPersonalization", "RestrictImplicitInkCollection", 0, RegistryValueKind.DWord);
                    Sys.SetUser(@"Software\Microsoft\InputPersonalization", "RestrictImplicitTextCollection", 0, RegistryValueKind.DWord);
                    Sys.SetUser(@"Software\Microsoft\InputPersonalization\TrainedDataStore", "HarvestContacts", 1, RegistryValueKind.DWord);
                    Sys.SetUser(@"Software\Microsoft\Personalization\Settings", "AcceptedPrivacyPolicy", 1, RegistryValueKind.DWord);
                },
                Check = () => Sys.IntEquals(Sys.GetUser(@"Software\Microsoft\InputPersonalization", "RestrictImplicitInkCollection"), 1)
            });

            list.Add(new Tweak
            {
                Id = "recent_docs_off", Category = Cat.Privacy,
                Name = "Ne pas mémoriser les documents/fichiers récents",
                Desc = "Empêche Windows de tenir l'historique des fichiers récemment ouverts (Explorateur, menu Démarrer). « Rétablir » réactive l'historique.",
                BackupKeys = new[] { @"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer" },
                Apply  = () => Sys.SetUser(@"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoRecentDocsHistory", 1, RegistryValueKind.DWord),
                Revert = () => Sys.DelUser(@"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoRecentDocsHistory"),
                Check  = () => Sys.IntEquals(Sys.GetUser(@"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoRecentDocsHistory"), 1)
            });

            list.Add(new Tweak
            {
                Id = "store_auto_update_off", Category = Cat.Services,
                Name = "Désactiver les mises à jour automatiques du Microsoft Store",
                Desc = "Le Store ne télécharge plus les apps en arrière-plan (moins d'E/S disque et réseau surprises). Tu peux toujours mettre à jour à la main. « Rétablir » réactive.",
                BackupKeys = new[] { @"HKLM\SOFTWARE\Policies\Microsoft\WindowsStore" },
                Apply  = () => Sys.SetMachine(@"SOFTWARE\Policies\Microsoft\WindowsStore", "AutoDownload", 2, RegistryValueKind.DWord),
                Revert = () => Sys.DelMachine(@"SOFTWARE\Policies\Microsoft\WindowsStore", "AutoDownload"),
                Check  = () => Sys.IntEquals(Sys.GetMachine(@"SOFTWARE\Policies\Microsoft\WindowsStore", "AutoDownload"), 2)
            });

            list.Add(new Tweak
            {
                Id = "ndu_off", Category = Cat.Services, Reboot = true,
                Name = "Désactiver le pilote de suivi de consommation réseau (Ndu)",
                Desc = "Coupe le service Ndu qui surveille l'usage réseau par appli (mémoire en moins, moins de fond). « Rétablir » le remet en démarrage automatique. Optionnel.",
                BackupKeys = new[] { @"HKLM\SYSTEM\CurrentControlSet\Services\Ndu" },
                Apply  = () => Sys.SetMachine(@"SYSTEM\CurrentControlSet\Services\Ndu", "Start", 4, RegistryValueKind.DWord),
                Revert = () => Sys.SetMachine(@"SYSTEM\CurrentControlSet\Services\Ndu", "Start", 2, RegistryValueKind.DWord),
                Check  = () => Sys.IntEquals(Sys.GetMachine(@"SYSTEM\CurrentControlSet\Services\Ndu", "Start"), 4)
            });

            list.Add(new Tweak
            {
                Id = "spooler_off", Category = Cat.Services,
                Name = "Désactiver le spouleur d'impression (si aucune imprimante)",
                Desc = "Arrête le service d'impression : moins de fond et surface d'attaque réduite. À N'ACTIVER QUE si tu n'imprimes pas. « Rétablir » relance l'impression.",
                Apply  = () => Sys.ConfigureService("Spooler", "disabled", true, false),
                Revert = () => Sys.ConfigureService("Spooler", "auto", false, false),
                Check  = () => Sys.ServiceDisabled("Spooler")
            });

            list.Add(new Tweak
            {
                Id = "tablet_service_off", Category = Cat.Services,
                Name = "Désactiver le service clavier tactile / manuscrit (PC sans tactile)",
                Desc = "Arrête TabletInputService (clavier tactile, saisie manuscrite), inutile sur un PC de bureau sans écran tactile. « Rétablir » le remet à la demande.",
                Apply  = () => Sys.ConfigureService("TabletInputService", "disabled", true, false),
                Revert = () => Sys.ConfigureService("TabletInputService", "demand", false, false),
                Check  = () => Sys.ServiceDisabled("TabletInputService")
            });

            // ================= LOT SUPPLÉMENTAIRE 2 =================
            const string SubDisk   = "0012ee47-9041-4b5d-9b77-535fba8b1442";
            const string DiskIdle  = "6738e2c4-e8a5-4a42-b16a-e040e769756e";

            list.Add(new Tweak
            {
                Id = "disk_timeout_off", Category = Cat.Alim, Esport = true,
                Name = "Ne jamais mettre les disques en veille (pas de micro-freeze au réveil)",
                Desc = "Empêche l'arrêt automatique des disques : supprime le petit gel quand un disque « se réveille ». « Rétablir » remet 20 min (défaut).",
                Apply  = () => Sys.SetPowerValue(SubDisk, DiskIdle, 0, 0),
                Revert = () => Sys.SetPowerValue(SubDisk, DiskIdle, 1200, 1200),
                Check  = () => Sys.PowerAcEquals(SubDisk, DiskIdle, 0)
            });

            list.Add(new Tweak
            {
                Id = "llmnr_off", Category = Cat.Reseau, Recommended = true,
                Name = "Désactiver LLMNR (résolution multicast) — sécurité + latence",
                Desc = "Coupe la résolution de noms multicast LLMNR : réduit une surface d'attaque connue et évite des requêtes réseau inutiles. Le DNS classique reste intact. « Rétablir » réactive.",
                BackupKeys = new[] { @"HKLM\SOFTWARE\Policies\Microsoft\Windows NT\DNSClient" },
                Apply  = () => Sys.SetMachine(@"SOFTWARE\Policies\Microsoft\Windows NT\DNSClient", "EnableMulticast", 0, RegistryValueKind.DWord),
                Revert = () => Sys.DelMachine(@"SOFTWARE\Policies\Microsoft\Windows NT\DNSClient", "EnableMulticast"),
                Check  = () => Sys.IntEquals(Sys.GetMachine(@"SOFTWARE\Policies\Microsoft\Windows NT\DNSClient", "EnableMulticast"), 0)
            });

            list.Add(new Tweak
            {
                Id = "crash_dump_minimal", Category = Cat.Systeme,
                Name = "Vidage mémoire minimal en cas de BSOD (moins de disque)",
                Desc = "En cas d'écran bleu, Windows n'écrit qu'un petit fichier minidump au lieu d'un vidage complet (plus rapide, moins d'espace). « Rétablir » remet le vidage automatique.",
                BackupKeys = new[] { @"HKLM\SYSTEM\CurrentControlSet\Control\CrashControl" },
                Apply  = () => Sys.SetMachine(@"SYSTEM\CurrentControlSet\Control\CrashControl", "CrashDumpEnabled", 3, RegistryValueKind.DWord),
                Revert = () => Sys.SetMachine(@"SYSTEM\CurrentControlSet\Control\CrashControl", "CrashDumpEnabled", 7, RegistryValueKind.DWord),
                Check  = () => Sys.IntEquals(Sys.GetMachine(@"SYSTEM\CurrentControlSet\Control\CrashControl", "CrashDumpEnabled"), 3)
            });

            list.Add(new Tweak
            {
                Id = "first_logon_anim_off", Category = Cat.Rapidite,
                Name = "Désactiver l'animation de première connexion",
                Desc = "Supprime l'animation d'accueil (« Bonjour... ») à la première ouverture de session : connexion plus directe. « Rétablir » la réactive.",
                BackupKeys = new[] { @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" },
                Apply  = () => Sys.SetMachine(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableFirstLogonAnimation", 0, RegistryValueKind.DWord),
                Revert = () => Sys.SetMachine(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableFirstLogonAnimation", 1, RegistryValueKind.DWord),
                Check  = () => Sys.IntEquals(Sys.GetMachine(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableFirstLogonAnimation"), 0)
            });

            list.Add(new Tweak
            {
                Id = "low_disk_warning_off", Category = Cat.Rapidite,
                Name = "Désactiver l'avertissement « disque presque plein »",
                Desc = "Supprime la bulle d'avertissement d'espace disque faible. « Rétablir » la réactive.",
                BackupKeys = new[] { @"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer" },
                Apply  = () => Sys.SetUser(@"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoLowDiskSpaceChecks", 1, RegistryValueKind.DWord),
                Revert = () => Sys.DelUser(@"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoLowDiskSpaceChecks"),
                Check  = () => Sys.IntEquals(Sys.GetUser(@"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoLowDiskSpaceChecks"), 1)
            });

            list.Add(new Tweak
            {
                Id = "numlock_boot", Category = Cat.Systeme,
                Name = "Activer le Verr. Num au démarrage",
                Desc = "Le pavé numérique est actif dès l'écran de connexion. « Rétablir » remet le comportement par défaut.",
                BackupKeys = new[] { @"HKCU\Control Panel\Keyboard" },
                Apply  = () => Sys.SetUser(@"Control Panel\Keyboard", "InitialKeyboardIndicators", "2147483650", RegistryValueKind.String),
                Revert = () => Sys.SetUser(@"Control Panel\Keyboard", "InitialKeyboardIndicators", "2147483648", RegistryValueKind.String),
                Check  = () => Sys.StrEquals(Sys.GetUser(@"Control Panel\Keyboard", "InitialKeyboardIndicators"), "2147483650")
            });

            list.Add(new Tweak
            {
                Id = "find_my_device_off", Category = Cat.Privacy,
                Name = "Désactiver « Localiser mon appareil »",
                Desc = "Windows ne synchronise plus la position de l'appareil pour la localisation à distance. « Rétablir » réactive.",
                BackupKeys = new[] { @"HKLM\SOFTWARE\Microsoft\Settings\FindMyDevice" },
                Apply  = () => Sys.SetMachine(@"SOFTWARE\Microsoft\Settings\FindMyDevice", "LocationSyncEnabled", 0, RegistryValueKind.DWord),
                Revert = () => Sys.SetMachine(@"SOFTWARE\Microsoft\Settings\FindMyDevice", "LocationSyncEnabled", 1, RegistryValueKind.DWord),
                Check  = () => Sys.IntEquals(Sys.GetMachine(@"SOFTWARE\Microsoft\Settings\FindMyDevice", "LocationSyncEnabled"), 0)
            });

            list.Add(new Tweak
            {
                Id = "wersvc_off", Category = Cat.Services,
                Name = "Désactiver le service Rapport d'erreurs Windows (WerSvc)",
                Desc = "Arrête l'envoi automatique des rapports de plantage à Microsoft (moins de fond). « Rétablir » le remet à la demande.",
                Apply  = () => Sys.ConfigureService("WerSvc", "disabled", true, false),
                Revert = () => Sys.ConfigureService("WerSvc", "demand", false, false),
                Check  = () => Sys.ServiceDisabled("WerSvc")
            });

            list.Add(new Tweak
            {
                Id = "retail_demo_off", Category = Cat.Services,
                Name = "Désactiver le service Mode Démo magasin (RetailDemo)",
                Desc = "Coupe un service destiné aux PC de démonstration en magasin, inutile sur un PC personnel. « Rétablir » le remet à la demande.",
                Apply  = () => Sys.ConfigureService("RetailDemo", "disabled", true, false),
                Revert = () => Sys.ConfigureService("RetailDemo", "demand", false, false),
                Check  = () => Sys.ServiceDisabled("RetailDemo")
            });

            // ================= LOT SUPPLÉMENTAIRE 3 =================

            list.Add(new Tweak
            {
                Id = "show_file_extensions", Category = Cat.Rapidite, Recommended = true,
                Name = "Afficher les extensions de fichiers (anti-piège .exe)",
                Desc = "Windows affiche l'extension réelle des fichiers : un « photo.jpg.exe » ne peut plus se déguiser en image. Sécurité + confort. « Rétablir » les masque à nouveau.",
                BackupKeys = new[] { @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced" },
                Apply  = () => Sys.SetUser(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HideFileExt", 0, RegistryValueKind.DWord),
                Revert = () => Sys.SetUser(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HideFileExt", 1, RegistryValueKind.DWord),
                Check  = () => Sys.IntEquals(Sys.GetUser(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HideFileExt"), 0)
            });

            list.Add(new Tweak
            {
                Id = "explorer_launch_to_thispc", Category = Cat.Rapidite,
                Name = "Ouvrir l'Explorateur sur « Ce PC » (pas « Accès rapide »)",
                Desc = "L'Explorateur s'ouvre directement sur Ce PC (disques) au lieu de l'Accès rapide : plus rapide et sans historique de fichiers récents affiché. « Rétablir » remet l'Accès rapide.",
                BackupKeys = new[] { @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced" },
                Apply  = () => Sys.SetUser(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "LaunchTo", 1, RegistryValueKind.DWord),
                Revert = () => Sys.SetUser(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "LaunchTo", 2, RegistryValueKind.DWord),
                Check  = () => Sys.IntEquals(Sys.GetUser(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "LaunchTo"), 1)
            });

            list.Add(new Tweak
            {
                Id = "auto_end_tasks", Category = Cat.Rapidite,
                Name = "Fermer automatiquement les applis figées à l'arrêt (arrêt plus rapide)",
                Desc = "À l'extinction/redémarrage, Windows ne bloque plus sur une appli qui ne répond pas : il la ferme tout seul. « Rétablir » remet la demande de confirmation.",
                BackupKeys = new[] { @"HKCU\Control Panel\Desktop" },
                Apply  = () => Sys.SetUser(@"Control Panel\Desktop", "AutoEndTasks", "1", RegistryValueKind.String),
                Revert = () => Sys.DelUser(@"Control Panel\Desktop", "AutoEndTasks"),
                Check  = () => Sys.StrEquals(Sys.GetUser(@"Control Panel\Desktop", "AutoEndTasks"), "1")
            });

            list.Add(new Tweak
            {
                Id = "wait_kill_service", Category = Cat.Rapidite,
                Name = "Réduire le délai d'arrêt des services (arrêt plus rapide)",
                Desc = "Windows attend moins longtemps un service récalcitrant avant de l'arrêter (2 s au lieu de 5). « Rétablir » remet 5 s.",
                BackupKeys = new[] { @"HKLM\SYSTEM\CurrentControlSet\Control" },
                Apply  = () => Sys.SetMachine(@"SYSTEM\CurrentControlSet\Control", "WaitToKillServiceTimeout", "2000", RegistryValueKind.String),
                Revert = () => Sys.SetMachine(@"SYSTEM\CurrentControlSet\Control", "WaitToKillServiceTimeout", "5000", RegistryValueKind.String),
                Check  = () => Sys.StrEquals(Sys.GetMachine(@"SYSTEM\CurrentControlSet\Control", "WaitToKillServiceTimeout"), "2000")
            });

            list.Add(new Tweak
            {
                Id = "voice_activation_off", Category = Cat.Privacy, Recommended = true,
                Name = "Désactiver l'activation vocale des applis (micro en écoute)",
                Desc = "Empêche les applis de rester à l'écoute du micro pour un mot-clé vocal. « Rétablir » réactive.",
                BackupKeys = new[] { @"HKCU\Software\Microsoft\Speech_OneCore\Settings\VoiceActivation\UserPreferenceForAllApps" },
                Apply  = () => Sys.SetUser(@"Software\Microsoft\Speech_OneCore\Settings\VoiceActivation\UserPreferenceForAllApps", "AgentActivationEnabled", 0, RegistryValueKind.DWord),
                Revert = () => Sys.SetUser(@"Software\Microsoft\Speech_OneCore\Settings\VoiceActivation\UserPreferenceForAllApps", "AgentActivationEnabled", 1, RegistryValueKind.DWord),
                Check  = () => Sys.IntEquals(Sys.GetUser(@"Software\Microsoft\Speech_OneCore\Settings\VoiceActivation\UserPreferenceForAllApps", "AgentActivationEnabled"), 0)
            });

            list.Add(new Tweak
            {
                Id = "clipboard_cloud_off", Category = Cat.Privacy,
                Name = "Désactiver le presse-papiers cloud (synchronisation entre appareils)",
                Desc = "Ce que tu copies ne part plus dans le cloud Microsoft pour être partagé entre appareils. Le presse-papiers local reste normal. « Rétablir » réactive.",
                BackupKeys = new[] { @"HKLM\SOFTWARE\Policies\Microsoft\Windows\System" },
                Apply  = () => Sys.SetMachine(@"SOFTWARE\Policies\Microsoft\Windows\System", "AllowCrossDeviceClipboard", 0, RegistryValueKind.DWord),
                Revert = () => Sys.DelMachine(@"SOFTWARE\Policies\Microsoft\Windows\System", "AllowCrossDeviceClipboard"),
                Check  = () => Sys.IntEquals(Sys.GetMachine(@"SOFTWARE\Policies\Microsoft\Windows\System", "AllowCrossDeviceClipboard"), 0)
            });

            list.Add(new Tweak
            {
                Id = "dmwappush_off", Category = Cat.Services,
                Name = "Désactiver le service de routage WAP Push (dmwappushservice)",
                Desc = "Coupe un service lié à la télémétrie/messages WAP, inutile pour un usage bureautique/jeu. « Rétablir » le remet à la demande.",
                Apply  = () => Sys.ConfigureService("dmwappushservice", "disabled", true, false),
                Revert = () => Sys.ConfigureService("dmwappushservice", "demand", false, false),
                Check  = () => Sys.ServiceDisabled("dmwappushservice")
            });

            list.Add(new Tweak
            {
                Id = "phone_service_off", Category = Cat.Services,
                Name = "Désactiver le service Téléphone (PhoneSvc)",
                Desc = "Arrête le service de gestion de téléphonie, inutile si tu ne relies pas de téléphone à Windows. « Rétablir » le remet à la demande.",
                Apply  = () => Sys.ConfigureService("PhoneSvc", "disabled", true, false),
                Revert = () => Sys.ConfigureService("PhoneSvc", "demand", false, false),
                Check  = () => Sys.ServiceDisabled("PhoneSvc")
            });

            list.Add(new Tweak
            {
                Id = "printnotify_off", Category = Cat.Services,
                Name = "Désactiver les notifications d'imprimante (PrintNotify)",
                Desc = "Coupe le service de notifications d'impression. À laisser actif si tu imprimes régulièrement. « Rétablir » le remet à la demande.",
                Apply  = () => Sys.ConfigureService("PrintNotify", "disabled", true, false),
                Revert = () => Sys.ConfigureService("PrintNotify", "demand", false, false),
                Check  = () => Sys.ServiceDisabled("PrintNotify")
            });

            // ================= GPU & AUDIO (compléments) =================

            list.Add(new Tweak
            {
                Id = "dx_vrr", Category = Cat.Gpu, Esport = true,
                Name = "Fréquence d'actualisation variable (VRR) pour les jeux fenêtrés",
                Desc = "Active l'optimisation VRR de Windows pour les jeux en fenêtré/sans bordure : moins de déchirure et de latence sur un écran G-Sync/FreeSync. Sans effet si l'écran ne gère pas le VRR. « Rétablir » la retire.",
                BackupKeys = new[] { @"HKCU\Software\Microsoft\DirectX\UserGpuPreferences" },
                Apply  = () => Sys.SetDxToken("VRROptimizeEnable", "1"),
                Revert = () => Sys.SetDxToken("VRROptimizeEnable", null),
                Check  = () => Sys.DxTokenEquals("VRROptimizeEnable", "1")
            });

            list.Add(new Tweak
            {
                Id = "audio_proaudio_mmcss", Category = Cat.Audio, Esport = true,
                Name = "Priorité MMCSS « Pro Audio » → Haute (Voicemeeter, DAW, ASIO)",
                Desc = "Donne aux applis audio faible latence (Voicemeeter, stations audio, moteurs ASIO) une priorité de planification et d'E/S Haute. Complète la tâche « Audio ». « Rétablir » remet les valeurs Windows.",
                BackupKeys = new[] { @"HKLM\" + ProAudioKey },
                Apply = () =>
                {
                    Sys.SetMachine(ProAudioKey, "Scheduling Category", "High", RegistryValueKind.String);
                    Sys.SetMachine(ProAudioKey, "SFIO Priority", "High", RegistryValueKind.String);
                },
                Revert = () =>
                {
                    Sys.SetMachine(ProAudioKey, "Scheduling Category", "High", RegistryValueKind.String);
                    Sys.SetMachine(ProAudioKey, "SFIO Priority", "Normal", RegistryValueKind.String);
                },
                Check = () => Sys.StrEquals(Sys.GetMachine(ProAudioKey, "SFIO Priority"), "High")
            });

            // ================= MODE MSI (interruptions par message) =================

            list.Add(new Tweak
            {
                Id = "msi_usb", Category = Cat.Souris, Esport = true, Reboot = true,
                Name = "Mode MSI sur les contrôleurs USB (latence souris/clavier)",
                Desc = "Passe les contrôleurs USB en interruptions par message (MSI) au lieu des IRQ classiques : traitement plus direct des périphériques USB, dont ta souris et ton clavier. Réduit la latence et le jitter d'entrée. « Rétablir » remet le mode par défaut. Redémarrage requis.",
                Apply  = () => Sys.SetMsiForClass(Sys.MsiUsbClass, true, null),
                Revert = () => Sys.SetMsiForClass(Sys.MsiUsbClass, false, null),
                Check  = () => Sys.MsiActiveForClass(Sys.MsiUsbClass)
            });

            list.Add(new Tweak
            {
                Id = "msi_network", Category = Cat.Reseau, Reboot = true,
                Name = "Mode MSI sur les cartes réseau (latence réseau)",
                Desc = "Passe les cartes réseau en interruptions par message (MSI) : traitement des paquets plus direct, moins de DPC réseau. « Rétablir » remet le mode par défaut. Redémarrage requis.",
                Apply  = () => Sys.SetMsiForClass(Sys.MsiNetClass, true, null),
                Revert = () => Sys.SetMsiForClass(Sys.MsiNetClass, false, null),
                Check  = () => Sys.MsiActiveForClass(Sys.MsiNetClass)
            });

            list.Add(new Tweak
            {
                Id = "msi_storage", Category = Cat.Systeme, Reboot = true,
                Name = "Mode MSI sur les contrôleurs de stockage (avancé)",
                Desc = "Passe les contrôleurs NVMe/SATA en interruptions par message (MSI) : moins de latence disque sous charge. AVANCÉ : sur de rares configurations, un contrôleur gère mal le MSI — teste au redémarrage ; si souci, « Rétablir » depuis l'app (ou Mode sans échec). Redémarrage requis.",
                Apply  = () => Sys.SetMsiForClass(Sys.MsiStorageClass, true, null),
                Revert = () => Sys.SetMsiForClass(Sys.MsiStorageClass, false, null),
                Check  = () => Sys.MsiActiveForClass(Sys.MsiStorageClass)
            });

            // ================= LOT SUPPLÉMENTAIRE 4 =================

            list.Add(new Tweak
            {
                Id = "disable_paging_combining", Category = Cat.Systeme, Reboot = true,
                Name = "Désactiver la combinaison de pages mémoire (PC avec beaucoup de RAM)",
                Desc = "Windows scanne la RAM pour fusionner les pages identiques — utile si la RAM manque, coûteux en CPU si tu en as beaucoup (16 Go+). Ce réglage l'arrête. « Rétablir » le réactive.",
                BackupKeys = new[] { @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management" },
                Apply  = () => Sys.SetMachine(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "DisablePagingCombining", 1, RegistryValueKind.DWord),
                Revert = () => Sys.DelMachine(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "DisablePagingCombining"),
                Check  = () => Sys.IntEquals(Sys.GetMachine(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "DisablePagingCombining"), 1)
            });

            list.Add(new Tweak
            {
                Id = "copilot_off", Category = Cat.Privacy,
                Name = "Désactiver Windows Copilot",
                Desc = "Retire l'assistant Copilot (bouton barre des tâches + processus de fond). « Rétablir » le réactive.",
                BackupKeys = new[] { @"HKCU\Software\Policies\Microsoft\Windows\WindowsCopilot" },
                Apply  = () => Sys.SetUser(@"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1, RegistryValueKind.DWord),
                Revert = () => Sys.DelUser(@"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot"),
                Check  = () => Sys.IntEquals(Sys.GetUser(@"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot"), 1)
            });

            list.Add(new Tweak
            {
                Id = "chat_taskbar_off", Category = Cat.Rapidite,
                Name = "Retirer l'icône Chat (Teams) de la barre des tâches",
                Desc = "Enlève le bouton Chat/Teams de la barre des tâches. « Rétablir » le remet.",
                BackupKeys = new[] { @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced" },
                Apply  = () => Sys.SetUser(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarMn", 0, RegistryValueKind.DWord),
                Revert = () => Sys.SetUser(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarMn", 1, RegistryValueKind.DWord),
                Check  = () => Sys.IntEquals(Sys.GetUser(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarMn"), 0)
            });

            list.Add(new Tweak
            {
                Id = "taskbar_search_hide", Category = Cat.Rapidite,
                Name = "Masquer la barre de recherche de la barre des tâches",
                Desc = "Retire la zone de recherche (gain de place, moins de suggestions web/télémétrie). La recherche reste accessible via le menu Démarrer. « Rétablir » la réaffiche.",
                BackupKeys = new[] { @"HKCU\Software\Microsoft\Windows\CurrentVersion\Search" },
                Apply  = () => Sys.SetUser(@"Software\Microsoft\Windows\CurrentVersion\Search", "SearchboxTaskbarMode", 0, RegistryValueKind.DWord),
                Revert = () => Sys.SetUser(@"Software\Microsoft\Windows\CurrentVersion\Search", "SearchboxTaskbarMode", 1, RegistryValueKind.DWord),
                Check  = () => Sys.IntEquals(Sys.GetUser(@"Software\Microsoft\Windows\CurrentVersion\Search", "SearchboxTaskbarMode"), 0)
            });

            list.Add(new Tweak
            {
                Id = "lang_list_access_off", Category = Cat.Privacy,
                Name = "Ne pas exposer ta liste de langues aux sites web",
                Desc = "Empêche les sites de lire ta liste de langues préférées (utilisée pour le pistage). « Rétablir » réactive.",
                BackupKeys = new[] { @"HKCU\Control Panel\International\User Profile" },
                Apply  = () => Sys.SetUser(@"Control Panel\International\User Profile", "HttpAcceptLanguageOptOut", 1, RegistryValueKind.DWord),
                Revert = () => Sys.DelUser(@"Control Panel\International\User Profile", "HttpAcceptLanguageOptOut"),
                Check  = () => Sys.IntEquals(Sys.GetUser(@"Control Panel\International\User Profile", "HttpAcceptLanguageOptOut"), 1)
            });

            list.Add(new Tweak
            {
                Id = "wisvc_off", Category = Cat.Services,
                Name = "Désactiver le service Windows Insider (wisvc)",
                Desc = "Coupe le service du programme Windows Insider, inutile si tu n'es pas dans les préversions. « Rétablir » le remet à la demande.",
                Apply  = () => Sys.ConfigureService("wisvc", "disabled", true, false),
                Revert = () => Sys.ConfigureService("wisvc", "demand", false, false),
                Check  = () => Sys.ServiceDisabled("wisvc")
            });

            list.Add(new Tweak
            {
                Id = "wmp_network_off", Category = Cat.Services,
                Name = "Désactiver le partage réseau Windows Media Player",
                Desc = "Coupe WMPNetworkSvc (partage de médias en réseau), inutile pour la plupart. « Rétablir » le remet à la demande.",
                Apply  = () => Sys.ConfigureService("WMPNetworkSvc", "disabled", true, false),
                Revert = () => Sys.ConfigureService("WMPNetworkSvc", "demand", false, false),
                Check  = () => Sys.ServiceDisabled("WMPNetworkSvc")
            });

            list.Add(new Tweak
            {
                Id = "smartcard_off", Category = Cat.Services,
                Name = "Désactiver le service Carte à puce (si non utilisé)",
                Desc = "Coupe SCardSvr, inutile sans lecteur de carte à puce. À laisser actif en entreprise. « Rétablir » le remet à la demande.",
                Apply  = () => Sys.ConfigureService("SCardSvr", "disabled", true, false),
                Revert = () => Sys.ConfigureService("SCardSvr", "demand", false, false),
                Check  = () => Sys.ServiceDisabled("SCardSvr")
            });

            list.Add(new Tweak
            {
                Id = "geo_service_off", Category = Cat.Services,
                Name = "Désactiver le service de géolocalisation (lfsvc)",
                Desc = "Coupe le service de localisation Windows. Les applis météo/carte ne connaîtront plus ta position. « Rétablir » le remet à la demande. Optionnel.",
                Apply  = () => Sys.ConfigureService("lfsvc", "disabled", true, false),
                Revert = () => Sys.ConfigureService("lfsvc", "demand", false, false),
                Check  = () => Sys.ServiceDisabled("lfsvc")
            });

            // ================= LOT SUPPLÉMENTAIRE 5 =================

            list.Add(new Tweak
            {
                Id = "cpu_perf_boost_aggressive", Category = Cat.Alim, Esport = true,
                Name = "Turbo processeur agressif (Perf Boost = Aggressive)",
                Desc = "Le CPU monte en turbo plus tôt et plus fort au lieu d'attendre. Gagne en réactivité/FPS, consomme un peu plus. « Rétablir » remet le mode par défaut.",
                Apply  = () => Sys.SetPowerValue(SubProc, "be337238-0d82-4146-a960-4f3749d470c7", 2, 2),
                Revert = () => Sys.SetPowerValue(SubProc, "be337238-0d82-4146-a960-4f3749d470c7", 3, 3),
                Check  = () => Sys.PowerAcEquals(SubProc, "be337238-0d82-4146-a960-4f3749d470c7", 2)
            });

            list.Add(new Tweak
            {
                Id = "no_lock_screen", Category = Cat.Rapidite,
                Name = "Passer l'écran de verrouillage (connexion plus directe)",
                Desc = "Va directement à la saisie du mot de passe sans l'écran de verrouillage intermédiaire. « Rétablir » le remet.",
                BackupKeys = new[] { @"HKLM\SOFTWARE\Policies\Microsoft\Windows\Personalization" },
                Apply  = () => Sys.SetMachine(@"SOFTWARE\Policies\Microsoft\Windows\Personalization", "NoLockScreen", 1, RegistryValueKind.DWord),
                Revert = () => Sys.DelMachine(@"SOFTWARE\Policies\Microsoft\Windows\Personalization", "NoLockScreen"),
                Check  = () => Sys.IntEquals(Sys.GetMachine(@"SOFTWARE\Policies\Microsoft\Windows\Personalization", "NoLockScreen"), 1)
            });

            list.Add(new Tweak
            {
                Id = "clipboard_history_off", Category = Cat.Privacy,
                Name = "Désactiver l'historique du presse-papiers",
                Desc = "Windows ne garde plus l'historique (Win+V) de ce que tu copies. « Rétablir » le réactive. Optionnel — pratique pour certains.",
                BackupKeys = new[] { @"HKLM\SOFTWARE\Policies\Microsoft\Windows\System" },
                Apply  = () => Sys.SetMachine(@"SOFTWARE\Policies\Microsoft\Windows\System", "AllowClipboardHistory", 0, RegistryValueKind.DWord),
                Revert = () => Sys.DelMachine(@"SOFTWARE\Policies\Microsoft\Windows\System", "AllowClipboardHistory"),
                Check  = () => Sys.IntEquals(Sys.GetMachine(@"SOFTWARE\Policies\Microsoft\Windows\System", "AllowClipboardHistory"), 0)
            });

            list.Add(new Tweak
            {
                Id = "lmhosts_off", Category = Cat.Reseau,
                Name = "Désactiver la recherche LMHOSTS (NetBIOS hérité)",
                Desc = "Coupe la résolution de noms via le fichier LMHOSTS, héritage inutile aujourd'hui : petite réduction de surface et de requêtes. « Rétablir » la réactive.",
                BackupKeys = new[] { @"HKLM\SYSTEM\CurrentControlSet\Services\NetBT\Parameters" },
                Apply  = () => Sys.SetMachine(@"SYSTEM\CurrentControlSet\Services\NetBT\Parameters", "EnableLMHOSTS", 0, RegistryValueKind.DWord),
                Revert = () => Sys.SetMachine(@"SYSTEM\CurrentControlSet\Services\NetBT\Parameters", "EnableLMHOSTS", 1, RegistryValueKind.DWord),
                Check  = () => Sys.IntEquals(Sys.GetMachine(@"SYSTEM\CurrentControlSet\Services\NetBT\Parameters", "EnableLMHOSTS"), 0)
            });

            list.Add(new Tweak
            {
                Id = "ajrouter_off", Category = Cat.Services,
                Name = "Désactiver le routeur AllJoyn (AJRouter)",
                Desc = "Coupe un service d'objets connectés (IoT AllJoyn) inutile sur un PC de jeu/bureautique. « Rétablir » le remet à la demande.",
                Apply  = () => Sys.ConfigureService("AJRouter", "disabled", true, false),
                Revert = () => Sys.ConfigureService("AJRouter", "demand", false, false),
                Check  = () => Sys.ServiceDisabled("AJRouter")
            });

            list.Add(new Tweak
            {
                Id = "fax_off", Category = Cat.Services,
                Name = "Désactiver le service Fax",
                Desc = "Coupe le service de télécopie, inutile pour la quasi-totalité des usages. « Rétablir » le remet à la demande.",
                Apply  = () => Sys.ConfigureService("Fax", "disabled", true, false),
                Revert = () => Sys.ConfigureService("Fax", "demand", false, false),
                Check  = () => Sys.ServiceDisabled("Fax")
            });

            list.Add(new Tweak
            {
                Id = "wallet_service_off", Category = Cat.Services,
                Name = "Désactiver le service Portefeuille (WalletService)",
                Desc = "Coupe le service Portefeuille Windows, rarement utilisé. « Rétablir » le remet à la demande. Optionnel.",
                Apply  = () => Sys.ConfigureService("WalletService", "disabled", true, false),
                Revert = () => Sys.ConfigureService("WalletService", "demand", false, false),
                Check  = () => Sys.ServiceDisabled("WalletService")
            });

            // ================= LOT SUPPLÉMENTAIRE 6 =================
            const string SubSleep  = "238c9fa8-0aad-41ed-83f4-97be242c8f20";
            const string WakeTimer = "bd3b718a-0680-4d9d-8ab2-e1d2b4ac806d";

            list.Add(new Tweak
            {
                Id = "wake_timers_off", Category = Cat.Alim,
                Name = "Interdire les minuteries de réveil (pas de réveil surprise)",
                Desc = "Empêche Windows de sortir le PC de veille tout seul (tâches planifiées, mises à jour). « Rétablir » les réautorise.",
                Apply  = () => Sys.SetPowerValue(SubSleep, WakeTimer, 0, 0),
                Revert = () => Sys.SetPowerValue(SubSleep, WakeTimer, 1, 1),
                Check  = () => Sys.PowerAcEquals(SubSleep, WakeTimer, 0)
            });

            list.Add(new Tweak
            {
                Id = "settings_sync_off", Category = Cat.Privacy,
                Name = "Désactiver la synchronisation des paramètres (cloud)",
                Desc = "Tes paramètres Windows (thème, mots de passe, préférences) ne sont plus synchronisés vers le cloud Microsoft. « Rétablir » réactive.",
                BackupKeys = new[] { @"HKLM\SOFTWARE\Policies\Microsoft\Windows\SettingSync" },
                Apply  = () => Sys.SetMachine(@"SOFTWARE\Policies\Microsoft\Windows\SettingSync", "DisableSettingSync", 2, RegistryValueKind.DWord),
                Revert = () => Sys.DelMachine(@"SOFTWARE\Policies\Microsoft\Windows\SettingSync", "DisableSettingSync"),
                Check  = () => Sys.IntEquals(Sys.GetMachine(@"SOFTWARE\Policies\Microsoft\Windows\SettingSync", "DisableSettingSync"), 2)
            });

            list.Add(new Tweak
            {
                Id = "nvidia_telemetry_off", Category = Cat.Services,
                Name = "Désactiver la télémétrie NVIDIA (NvTelemetryContainer)",
                Desc = "Coupe le service de télémétrie du pilote NVIDIA, sans effet sur les jeux ni les performances. Sans effet si tu n'as pas de GPU NVIDIA. « Rétablir » le remet.",
                Apply  = () => Sys.ConfigureService("NvTelemetryContainer", "disabled", true, false),
                Revert = () => Sys.ConfigureService("NvTelemetryContainer", "auto", false, false),
                Check  = () => Sys.ServiceDisabled("NvTelemetryContainer")
            });

            list.Add(new Tweak
            {
                Id = "ssdp_off", Category = Cat.Services,
                Name = "Désactiver la découverte SSDP/UPnP",
                Desc = "Coupe SSDPSRV (découverte de périphériques UPnP sur le réseau). Peut gêner le partage média/DLNA — optionnel. « Rétablir » le remet à la demande.",
                Apply  = () => Sys.ConfigureService("SSDPSRV", "disabled", true, false),
                Revert = () => Sys.ConfigureService("SSDPSRV", "demand", false, false),
                Check  = () => Sys.ServiceDisabled("SSDPSRV")
            });

            list.Add(new Tweak
            {
                Id = "trkwks_off", Category = Cat.Services,
                Name = "Désactiver le suivi de liens distribués (TrkWks)",
                Desc = "Coupe le service qui suit les fichiers liés déplacés sur le réseau NTFS, rarement utile en usage personnel. « Rétablir » le remet en automatique.",
                Apply  = () => Sys.ConfigureService("TrkWks", "disabled", true, false),
                Revert = () => Sys.ConfigureService("TrkWks", "auto", false, false),
                Check  = () => Sys.ServiceDisabled("TrkWks")
            });

            list.Add(new Tweak
            {
                Id = "icssvc_off", Category = Cat.Services,
                Name = "Désactiver le point d'accès mobile (icssvc)",
                Desc = "Coupe le service de partage de connexion (hotspot Windows). À laisser actif si tu partages ta connexion. « Rétablir » le remet à la demande.",
                Apply  = () => Sys.ConfigureService("icssvc", "disabled", true, false),
                Revert = () => Sys.ConfigureService("icssvc", "demand", false, false),
                Check  = () => Sys.ServiceDisabled("icssvc")
            });

            list.Add(new Tweak
            {
                Id = "semgr_off", Category = Cat.Services,
                Name = "Désactiver le gestionnaire de paiements/NFC (SEMgrSvc)",
                Desc = "Coupe le service de paiements sans contact et éléments sécurisés, inutile sur un PC de bureau. « Rétablir » le remet à la demande.",
                Apply  = () => Sys.ConfigureService("SEMgrSvc", "disabled", true, false),
                Revert = () => Sys.ConfigureService("SEMgrSvc", "demand", false, false),
                Check  = () => Sys.ServiceDisabled("SEMgrSvc")
            });

            list.Add(new Tweak
            {
                Id = "sensor_service_off", Category = Cat.Services,
                Name = "Désactiver le service de capteurs (PC sans capteur)",
                Desc = "Coupe SensorService (luminosité auto, orientation), inutile sur un PC fixe sans capteur. À laisser actif sur un portable. « Rétablir » le remet à la demande.",
                Apply  = () => Sys.ConfigureService("SensorService", "disabled", true, false),
                Revert = () => Sys.ConfigureService("SensorService", "demand", false, false),
                Check  = () => Sys.ServiceDisabled("SensorService")
            });

            // ================= LOT SUPPLÉMENTAIRE 7 =================

            list.Add(new Tweak
            {
                Id = "hybrid_sleep_off", Category = Cat.Alim,
                Name = "Désactiver la veille hybride (mise en veille plus rapide)",
                Desc = "La veille hybride écrit la RAM sur le disque à chaque mise en veille (lent, usure SSD). La désactiver rend la veille immédiate. « Rétablir » la réactive.",
                Apply  = () => Sys.SetPowerValue(SubSleep, "94ac6d29-73ce-41a6-809f-6363ba21b47e", 0, 0),
                Revert = () => Sys.SetPowerValue(SubSleep, "94ac6d29-73ce-41a6-809f-6363ba21b47e", 1, 1),
                Check  = () => Sys.PowerAcEquals(SubSleep, "94ac6d29-73ce-41a6-809f-6363ba21b47e", 0)
            });

            list.Add(new Tweak
            {
                Id = "search_history_off", Category = Cat.Privacy,
                Name = "Ne pas mémoriser l'historique de recherche de l'appareil",
                Desc = "Windows ne garde plus la trace de tes recherches locales. « Rétablir » réactive.",
                BackupKeys = new[] { @"HKCU\Software\Microsoft\Windows\CurrentVersion\SearchSettings" },
                Apply  = () => Sys.SetUser(@"Software\Microsoft\Windows\CurrentVersion\SearchSettings", "IsDeviceSearchHistoryEnabled", 0, RegistryValueKind.DWord),
                Revert = () => Sys.SetUser(@"Software\Microsoft\Windows\CurrentVersion\SearchSettings", "IsDeviceSearchHistoryEnabled", 1, RegistryValueKind.DWord),
                Check  = () => Sys.IntEquals(Sys.GetUser(@"Software\Microsoft\Windows\CurrentVersion\SearchSettings", "IsDeviceSearchHistoryEnabled"), 0)
            });

            list.Add(new Tweak
            {
                Id = "snap_assist_off", Category = Cat.Rapidite,
                Name = "Désactiver les suggestions d'ancrage de fenêtres (Snap flyout)",
                Desc = "Retire le petit menu de dispositions qui apparaît au survol du bouton agrandir. « Rétablir » le remet.",
                BackupKeys = new[] { @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced" },
                Apply  = () => Sys.SetUser(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "EnableSnapAssistFlyout", 0, RegistryValueKind.DWord),
                Revert = () => Sys.SetUser(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "EnableSnapAssistFlyout", 1, RegistryValueKind.DWord),
                Check  = () => Sys.IntEquals(Sys.GetUser(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "EnableSnapAssistFlyout"), 0)
            });

            list.Add(new Tweak
            {
                Id = "wcncsvc_off", Category = Cat.Services,
                Name = "Désactiver Windows Connect Now (wcncsvc)",
                Desc = "Coupe un service de configuration sans fil hérité (WPS), rarement utile. « Rétablir » le remet à la demande.",
                Apply  = () => Sys.ConfigureService("wcncsvc", "disabled", true, false),
                Revert = () => Sys.ConfigureService("wcncsvc", "demand", false, false),
                Check  = () => Sys.ServiceDisabled("wcncsvc")
            });

            list.Add(new Tweak
            {
                Id = "dot3svc_off", Category = Cat.Services,
                Name = "Désactiver l'authentification filaire 802.1X (dot3svc)",
                Desc = "Coupe le service d'authentification réseau filaire (802.1X), inutile hors entreprise. « Rétablir » le remet à la demande.",
                Apply  = () => Sys.ConfigureService("dot3svc", "disabled", true, false),
                Revert = () => Sys.ConfigureService("dot3svc", "demand", false, false),
                Check  = () => Sys.ServiceDisabled("dot3svc")
            });

            list.Add(new Tweak
            {
                Id = "wfds_off", Category = Cat.Services,
                Name = "Désactiver les services Wi-Fi Direct (WFDSConMgrSvc)",
                Desc = "Coupe le gestionnaire Wi-Fi Direct (partage sans fil pair-à-pair). À laisser si tu utilises Miracast/impression Wi-Fi Direct. « Rétablir » le remet à la demande.",
                Apply  = () => Sys.ConfigureService("WFDSConMgrSvc", "disabled", true, false),
                Revert = () => Sys.ConfigureService("WFDSConMgrSvc", "demand", false, false),
                Check  = () => Sys.ServiceDisabled("WFDSConMgrSvc")
            });

            list.Add(new Tweak
            {
                Id = "diag_collector_off", Category = Cat.Services,
                Name = "Désactiver le collecteur de diagnostics Microsoft",
                Desc = "Coupe diagnosticshub.standardcollector.service (collecte de traces de diagnostic). « Rétablir » le remet à la demande.",
                Apply  = () => Sys.ConfigureService("diagnosticshub.standardcollector.service", "disabled", true, false),
                Revert = () => Sys.ConfigureService("diagnosticshub.standardcollector.service", "demand", false, false),
                Check  = () => Sys.ServiceDisabled("diagnosticshub.standardcollector.service")
            });

            list.Add(new Tweak
            {
                Id = "displayenhancement_off", Category = Cat.Services,
                Name = "Désactiver le service d'amélioration d'affichage (PC fixe)",
                Desc = "Coupe DisplayEnhancementService (luminosité adaptative / veilleuse). À laisser actif sur un portable. « Rétablir » le remet à la demande.",
                Apply  = () => Sys.ConfigureService("DisplayEnhancementService", "disabled", true, false),
                Revert = () => Sys.ConfigureService("DisplayEnhancementService", "demand", false, false),
                Check  = () => Sys.ServiceDisabled("DisplayEnhancementService")
            });

            // ================= LOT CIBLÉ (impact réel) =================

            list.Add(new Tweak
            {
                Id = "gamedvr_policy_off", Category = Cat.Gpu, Esport = true,
                Name = "Désactiver GameDVR par stratégie (verrou machine)",
                Desc = "Force GameDVR à OFF au niveau machine (stratégie), en plus du réglage utilisateur : certains jeux/MAJ réactivent GameDVR côté utilisateur, ceci l'empêche. Gain de FPS et moins d'overhead de capture. « Rétablir » lève la stratégie.",
                BackupKeys = new[] { @"HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR" },
                Apply  = () => Sys.SetMachine(@"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0, RegistryValueKind.DWord),
                Revert = () => Sys.DelMachine(@"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR"),
                Check  = () => Sys.IntEquals(Sys.GetMachine(@"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR"), 0)
            });

            list.Add(new Tweak
            {
                Id = "dns_priority", Category = Cat.Reseau, Esport = true,
                Name = "Prioriser la résolution locale des noms (DNS plus réactif)",
                Desc = "Réordonne les fournisseurs de résolution pour privilégier le cache/hosts/DNS local avant NetBIOS : résolution de noms plus rapide. « Rétablir » remet les priorités Windows.",
                BackupKeys = new[] { @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\ServiceProvider" },
                Apply = () =>
                {
                    Sys.SetMachine(@"SYSTEM\CurrentControlSet\Services\Tcpip\ServiceProvider", "LocalPriority", 4, RegistryValueKind.DWord);
                    Sys.SetMachine(@"SYSTEM\CurrentControlSet\Services\Tcpip\ServiceProvider", "HostsPriority", 5, RegistryValueKind.DWord);
                    Sys.SetMachine(@"SYSTEM\CurrentControlSet\Services\Tcpip\ServiceProvider", "DnsPriority", 6, RegistryValueKind.DWord);
                    Sys.SetMachine(@"SYSTEM\CurrentControlSet\Services\Tcpip\ServiceProvider", "NetbtPriority", 7, RegistryValueKind.DWord);
                },
                Revert = () =>
                {
                    Sys.SetMachine(@"SYSTEM\CurrentControlSet\Services\Tcpip\ServiceProvider", "LocalPriority", 499, RegistryValueKind.DWord);
                    Sys.SetMachine(@"SYSTEM\CurrentControlSet\Services\Tcpip\ServiceProvider", "HostsPriority", 500, RegistryValueKind.DWord);
                    Sys.SetMachine(@"SYSTEM\CurrentControlSet\Services\Tcpip\ServiceProvider", "DnsPriority", 2000, RegistryValueKind.DWord);
                    Sys.SetMachine(@"SYSTEM\CurrentControlSet\Services\Tcpip\ServiceProvider", "NetbtPriority", 2001, RegistryValueKind.DWord);
                },
                Check = () => Sys.IntEquals(Sys.GetMachine(@"SYSTEM\CurrentControlSet\Services\Tcpip\ServiceProvider", "LocalPriority"), 4)
            });

            list.Add(new Tweak
            {
                Id = "edge_startup_boost_off", Category = Cat.Rapidite, Recommended = true,
                Name = "Désactiver le « démarrage rapide » d'Edge (Startup Boost)",
                Desc = "Edge ne se pré-lance plus en arrière-plan à l'ouverture de session : RAM et CPU libérés au démarrage. « Rétablir » réactive.",
                BackupKeys = new[] { @"HKLM\SOFTWARE\Policies\Microsoft\Edge" },
                Apply  = () => Sys.SetMachine(@"SOFTWARE\Policies\Microsoft\Edge", "StartupBoostEnabled", 0, RegistryValueKind.DWord),
                Revert = () => Sys.DelMachine(@"SOFTWARE\Policies\Microsoft\Edge", "StartupBoostEnabled"),
                Check  = () => Sys.IntEquals(Sys.GetMachine(@"SOFTWARE\Policies\Microsoft\Edge", "StartupBoostEnabled"), 0)
            });

            list.Add(new Tweak
            {
                Id = "remote_assistance_off", Category = Cat.Services, Recommended = true,
                Name = "Désactiver l'Assistance à distance (surface d'attaque)",
                Desc = "Empêche les invitations d'assistance à distance entrantes. Recommandé pour un PC personnel. « Rétablir » la réautorise.",
                BackupKeys = new[] { @"HKLM\SYSTEM\CurrentControlSet\Control\Remote Assistance" },
                Apply  = () => Sys.SetMachine(@"SYSTEM\CurrentControlSet\Control\Remote Assistance", "fAllowToGetHelp", 0, RegistryValueKind.DWord),
                Revert = () => Sys.SetMachine(@"SYSTEM\CurrentControlSet\Control\Remote Assistance", "fAllowToGetHelp", 1, RegistryValueKind.DWord),
                Check  = () => Sys.IntEquals(Sys.GetMachine(@"SYSTEM\CurrentControlSet\Control\Remote Assistance", "fAllowToGetHelp"), 0)
            });

            list.Add(new Tweak
            {
                Id = "modern_standby_off", Category = Cat.Alim, Reboot = true,
                Name = "Forcer la veille S3 classique (corrige la veille moderne)",
                Desc = "Désactive la « veille moderne » (S0) au profit de la veille S3 : corrige la décharge de batterie en veille et les réveils intempestifs sur les machines où S0 est mal géré. « Rétablir » remet la veille moderne. Avancé.",
                BackupKeys = new[] { @"HKLM\SYSTEM\CurrentControlSet\Control\Power" },
                Apply  = () => Sys.SetMachine(@"SYSTEM\CurrentControlSet\Control\Power", "PlatformAoAcOverride", 0, RegistryValueKind.DWord),
                Revert = () => Sys.DelMachine(@"SYSTEM\CurrentControlSet\Control\Power", "PlatformAoAcOverride"),
                Check  = () => Sys.IntEquals(Sys.GetMachine(@"SYSTEM\CurrentControlSet\Control\Power", "PlatformAoAcOverride"), 0)
            });

            // ================= OBJECTIF 500 FPS =================

            list.Add(new Tweak
            {
                Id = "mmcss_no_lazy", Category = Cat.Systeme, Esport = true,
                Name = "MMCSS : désactiver le mode paresseux (NoLazyMode)",
                Desc = "Empêche le planificateur multimédia de regrouper ses réveils pour économiser l'énergie : il reste réactif en continu pendant le jeu. Cible les très hauts FPS (240-500 Hz). Expérimental, réversible.",
                BackupKeys = new[] { @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile" },
                Apply  = () => Sys.SetMachine(MMKey, "NoLazyMode", 1, RegistryValueKind.DWord),
                Revert = () => Sys.DelMachine(MMKey, "NoLazyMode"),
                Check  = () => Sys.IntEquals(Sys.GetMachine(MMKey, "NoLazyMode"), 1)
            });

            list.Add(new Tweak
            {
                Id = "games_cpu_priority_high", Category = Cat.Systeme, Esport = true,
                Name = "Priorité CPU « Haute » pour les jeux compétitifs (CS2, OW2, Valorant…)",
                Desc = "Windows lance ces jeux en priorité processeur Haute (PerfOptions, mécanisme officiel — aucune injection, compatible anti-cheat) : quand le CPU sature, le jeu passe devant les tâches de fond. Précieux sur les machines limitées par le CPU. Réversible.",
                BackupKeys = new[] { @"HKLM\" + GameScan.IfeoKey },
                Apply = () =>
                {
                    foreach (string exe in GameScan.PriorityExes())
                        Sys.SetMachine(GameScan.IfeoKey + "\\" + exe + "\\PerfOptions", "CpuPriorityClass", 3, RegistryValueKind.DWord);
                },
                Revert = () =>
                {
                    foreach (string exe in GameScan.PriorityExes())
                        Sys.DelMachine(GameScan.IfeoKey + "\\" + exe + "\\PerfOptions", "CpuPriorityClass");
                },
                Check = () =>
                {
                    foreach (string exe in GameScan.PriorityExes())
                        if (!Sys.IntEquals(Sys.GetMachine(GameScan.IfeoKey + "\\" + exe + "\\PerfOptions", "CpuPriorityClass"), 3))
                            return false;
                    return true;
                }
            });

            return list;
        }
    }
}
