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
                Id = "input_queues", Category = Cat.Souris, Reboot = true,
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
                Check = () => null
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
                Check = () => null
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
                Id = "dynamic_tick", Category = Cat.Systeme, Reboot = true,
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
                Desc = "Empêche les applications du Store de tourner en fond. Peut retarder certaines notifications d'applis du Store.",
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
                Id = "nagle", Category = Cat.Reseau,
                Name = "Désactiver l'algorithme de Nagle (TcpAckFrequency / TCPNoDelay)",
                Desc = "Envoi TCP immédiat sans regroupement de paquets : utile pour les jeux en ligne TCP. Sans effet sur les jeux en UDP.",
                BackupKeys = new[] { @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces" },
                Apply = () => Sys.SetNagle(true),
                Revert = () => Sys.SetNagle(false),
                Check = () => null
            });

            list.Add(new Tweak
            {
                Id = "rsc_off", Category = Cat.Reseau, Esport = true,
                Name = "Désactiver la coalescence de segments réseau (RSC)",
                Desc = "Les paquets reçus sont traités immédiatement au lieu d'être regroupés : latence réseau réduite de quelques ms, léger surcoût CPU.",
                Apply = () => Sys.RunThrow(Sys.Sys32("netsh.exe"), "interface tcp set global rsc=disabled", "Désactivation du RSC"),
                Revert = () => Sys.RunThrow(Sys.Sys32("netsh.exe"), "interface tcp set global rsc=enabled", "Réactivation du RSC"),
                Check = () => null
            });

            list.Add(new Tweak
            {
                Id = "nic_power_off", Category = Cat.Reseau, Esport = true, Reboot = true,
                Name = "Empêcher Windows d'éteindre la carte réseau (économie d'énergie NIC)",
                Desc = "Coupe la mise en veille de l'adaptateur réseau : évite micro-coupures et pics de ping après une période calme.",
                Apply = () => Sys.SetNicPowerSaving(true),
                Revert = () => Sys.SetNicPowerSaving(false),
                Check = () => null
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
                Check  = () => null
            });

            list.Add(new Tweak
            {
                Id = "cpu_idle_disable", Category = Cat.Alim,
                Name = "Désactiver les états de repos du CPU (C-States) — EXPÉRIMENTAL",
                Desc = "Le CPU ne s'endort jamais : latence d'interruption minimale, mais chaleur/consommation en forte hausse. À réserver à un desktop bien refroidi. « Rétablir » réactive le repos.",
                Apply  = () => Sys.SetPowerValue(SubProc, "5d76a2ca-e8c0-402f-a133-2158492d58ad", 1, 1),
                Revert = () => Sys.SetPowerValue(SubProc, "5d76a2ca-e8c0-402f-a133-2158492d58ad", 0, 0),
                Check  = () => null
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

            return list;
        }
    }
}
