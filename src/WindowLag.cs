using System;
using System.Collections.Generic;
using System.Management;
using Microsoft.Win32;

namespace BTOptimizer
{
    /// <summary>
    /// « Les fenêtres saccadent quand je les déplace » — le symptôme le plus courant… et le
    /// plus mal diagnostiqué. Ce n'est presque jamais la carte graphique : c'est le compositeur
    /// de Windows (DWM) mis en difficulté par la configuration d'affichage.
    ///
    /// Constats recherchés, du plus fréquent au plus rare :
    ///  1. écrans à fréquences MIXTES (un 500 Hz à côté d'un 180 Hz : DWM doit servir les deux) ;
    ///  2. écran VIRTUEL fantôme (Parsec, OBS, iDisplay…) que DWM compose en plus des vrais ;
    ///  3. MPO (MultiPlane Overlay) actif — scintillements et saccades connus sur NVIDIA ;
    ///  4. HAGS actif avec un pilote qui le supporte mal ;
    ///  5. transparence + effets visuels ;
    ///  6. pilote graphique ancien.
    ///
    /// Tout est en LECTURE. La seule correction proposée (aligner les fréquences) passe par
    /// l'API d'affichage de Windows et se rétablit d'un clic.
    /// </summary>
    internal static class WindowLag
    {
        public class Finding
        {
            public int Impact;         // 0-100 : sert au tri et à la couleur
            public string Title;
            public string Detail;
            public string FixLabel;    // null = pas de correction automatique (geste manuel)
            public Action Fix;
            public bool Manual;        // vrai = le geste est à faire par l'utilisateur
        }

        public class Report
        {
            public readonly List<Finding> Findings = new List<Finding>();
            public List<DisplayInfo.DisplayMode> Screens = new List<DisplayInfo.DisplayMode>();
            public int CommonHz = -1;      // fréquence commune proposée si écrans mixtes
            public string VirtualAdapter;  // nom de l'écran virtuel trouvé (ou null)
        }

        // Adaptateurs d'affichage VIRTUELS courants (télétravail, streaming, capture).
        private static readonly string[] VirtualHints =
        {
            "parsec", "virtual display", "idisplay", "spacedesk", "duet", "amyuni",
            "usb display", "displaylink", "vnc mirror", "obs virtual"
        };

        public static Report Analyse()
        {
            var r = new Report();
            try { r.Screens = DisplayInfo.Query() ?? new List<DisplayInfo.DisplayMode>(); }
            catch { r.Screens = new List<DisplayInfo.DisplayMode>(); }

            CheckMixedRefresh(r);
            CheckVirtualAdapter(r);
            CheckMpo(r);
            CheckHags(r);
            CheckVisuals(r);
            CheckDriverAge(r);

            r.Findings.Sort((a, b) => b.Impact.CompareTo(a.Impact));
            return r;
        }

        // ---- 1. Fréquences mixtes : LA cause n°1 -------------------------------------
        private static void CheckMixedRefresh(Report r)
        {
            if (r.Screens.Count < 2) return;
            int min = int.MaxValue, max = 0;
            foreach (var s in r.Screens)
            {
                if (s.CurrentHz <= 0) continue;
                if (s.CurrentHz < min) min = s.CurrentHz;
                if (s.CurrentHz > max) max = s.CurrentHz;
            }
            if (max <= 0 || min == int.MaxValue || max - min <= 5) return;

            // La fréquence commune la plus haute que TOUS les écrans savent tenir.
            int common = int.MaxValue;
            foreach (var s in r.Screens) if (s.MaxHz > 0 && s.MaxHz < common) common = s.MaxHz;
            if (common == int.MaxValue) common = min;
            r.CommonHz = common;

            var list = new System.Text.StringBuilder();
            foreach (var s in r.Screens)
                list.Append("• ").Append(s.Name).Append(" : ").Append(s.CurrentHz).Append(" Hz\r\n");

            r.Findings.Add(new Finding
            {
                Impact = 90,
                Title = "Écrans à fréquences mixtes (" + min + " Hz ↔ " + max + " Hz)",
                Detail = "Windows compose TOUT le bureau en une seule passe (DWM). Quand tes écrans tournent à des "
                       + "fréquences très différentes, le déplacement d'une fenêtre saccade — surtout sur l'écran le "
                       + "plus lent ou à cheval sur deux écrans. C'est la cause n°1 de ce symptôme, et elle n'a rien "
                       + "à voir avec la puissance de la carte graphique.\r\n\r\n" + list
                       + "\r\nAligner tous les écrans sur " + common + " Hz supprime le problème. Tu perds les images "
                       + "au-delà sur l'écran rapide — c'est un vrai compromis, à toi de juger : teste, et remets "
                       + "comme avant si tu préfères le haut rafraîchissement.",
                FixLabel = "Aligner tous les écrans sur " + common + " Hz",
                Fix = delegate
                {
                    foreach (var s in r.Screens)
                        if (s.CurrentHz != common) DisplayInfo.SetHz(s.Device, common);
                }
            });
        }

        // ---- 2. Écran virtuel fantôme ------------------------------------------------
        private static void CheckVirtualAdapter(Report r)
        {
            try
            {
                using (var mos = new ManagementObjectSearcher("SELECT Name, ConfigManagerErrorCode FROM Win32_VideoController"))
                    foreach (ManagementObject mo in mos.Get())
                    {
                        string name = mo["Name"] as string;
                        if (string.IsNullOrEmpty(name)) continue;
                        object err = mo["ConfigManagerErrorCode"];
                        if (err != null && Convert.ToInt32(err) == 22) continue;   // déjà désactivé
                        string low = name.ToLowerInvariant();
                        foreach (string hint in VirtualHints)
                        {
                            if (low.IndexOf(hint, StringComparison.Ordinal) < 0) continue;
                            r.VirtualAdapter = name;
                            r.Findings.Add(new Finding
                            {
                                Impact = 70,
                                Title = "Écran virtuel actif : « " + name + " »",
                                Detail = "Cet adaptateur ajoute un écran FANTÔME que Windows doit composer en plus de tes "
                                       + "vrais écrans — cause connue de saccades du bureau. Si tu ne t'en sers pas en ce "
                                       + "moment (bureau à distance, streaming, capture), désactive-le le temps du test : "
                                       + "Gestionnaire de périphériques → Cartes graphiques → clic droit → Désactiver. "
                                       + "Tu le réactives d'un clic quand tu en as besoin.",
                                FixLabel = "Ouvrir le Gestionnaire de périphériques",
                                Manual = true,
                                Fix = delegate
                                {
                                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("devmgmt.msc") { UseShellExecute = true }); }
                                    catch { }
                                }
                            });
                            return;
                        }
                    }
            }
            catch { }
        }

        // ---- 3. MPO ------------------------------------------------------------------
        private const string DwmKey = @"SOFTWARE\Microsoft\Windows\Dwm";

        private static void CheckMpo(Report r)
        {
            try
            {
                if (Sys.IntEquals(Sys.GetMachine(DwmKey, "OverlayTestMode"), 5)) return;   // déjà désactivé
                r.Findings.Add(new Finding
                {
                    Impact = 60,
                    Title = "MPO (MultiPlane Overlay) actif",
                    Detail = "Le MPO laisse le GPU afficher certaines fenêtres « par-dessus » le bureau sans passer par "
                           + "le compositeur. Sur NVIDIA notamment, il provoque des scintillements et des saccades bien "
                           + "documentés, en particulier en multi-écrans. Le désactiver est le remède standard ; l'effet "
                           + "sur la consommation est négligeable. Redémarrage nécessaire.",
                    FixLabel = "Désactiver le MPO (redémarrage requis)",
                    Fix = delegate { Sys.SetMachine(DwmKey, "OverlayTestMode", 5, RegistryValueKind.DWord); }
                });
            }
            catch { }
        }

        // ---- 4. HAGS -----------------------------------------------------------------
        private const string GfxKey = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";

        private static void CheckHags(Report r)
        {
            try
            {
                if (!Sys.IntEquals(Sys.GetMachine(GfxKey, "HwSchMode"), 2)) return;   // pas actif : rien à dire
                r.Findings.Add(new Finding
                {
                    Impact = 45,
                    Title = "Planification GPU matérielle (HAGS) active",
                    Detail = "HAGS confie la file d'attente du GPU au GPU lui-même. Selon les pilotes, ça aide… ou ça "
                           + "provoque des micro-saccades du bureau et des « dispositif de rendu perdu ». Si le reste "
                           + "n'a rien donné, teste SANS : c'est réversible et ça se juge en une minute. Redémarrage "
                           + "nécessaire.",
                    FixLabel = "Désactiver HAGS (redémarrage requis)",
                    Fix = delegate { Sys.SetMachine(GfxKey, "HwSchMode", 1, RegistryValueKind.DWord); }
                });
            }
            catch { }
        }

        // ---- 5. Transparence / effets ------------------------------------------------
        private static void CheckVisuals(Report r)
        {
            try
            {
                object t = Sys.GetUser(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency");
                if (t == null || !Sys.IntEquals(t, 1)) return;
                r.Findings.Add(new Finding
                {
                    Impact = 25,
                    Title = "Transparence de l'interface activée",
                    Detail = "Les effets de transparence (acrylique, Mica) demandent au compositeur un travail "
                           + "supplémentaire à chaque image, y compris pendant qu'une fenêtre bouge. La couper est "
                           + "sans risque, immédiat, et se remet quand tu veux — c'est un gain modeste, à tester en "
                           + "dernier.",
                    FixLabel = "Couper la transparence",
                    Fix = delegate
                    {
                        Sys.SetUser(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                                    "EnableTransparency", 0, RegistryValueKind.DWord);
                    }
                });
            }
            catch { }
        }

        // ---- 6. Pilote graphique ancien ----------------------------------------------
        private static void CheckDriverAge(Report r)
        {
            try
            {
                using (var mos = new ManagementObjectSearcher("SELECT Name, DriverDate FROM Win32_VideoController"))
                    foreach (ManagementObject mo in mos.Get())
                    {
                        string name = mo["Name"] as string;
                        string date = mo["DriverDate"] as string;
                        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(date) || date.Length < 8) continue;
                        bool virt = false;
                        string low = name.ToLowerInvariant();
                        foreach (string h in VirtualHints) if (low.IndexOf(h, StringComparison.Ordinal) >= 0) virt = true;
                        if (virt) continue;

                        int yy, mm, dd;
                        if (!int.TryParse(date.Substring(0, 4), out yy)) continue;
                        if (!int.TryParse(date.Substring(4, 2), out mm)) continue;
                        if (!int.TryParse(date.Substring(6, 2), out dd)) continue;
                        var d = new DateTime(yy, mm, dd);
                        int months = (int)((DateTime.Now - d).TotalDays / 30);
                        if (months < 12) continue;

                        r.Findings.Add(new Finding
                        {
                            Impact = 30,
                            Title = "Pilote graphique ancien (" + name + ", ~" + months + " mois)",
                            Detail = "Un pilote de plus d'un an peut traîner des bogues de composition corrigés depuis. "
                                   + "Une mise à jour propre (et, si les crashs persistent, une réinstallation avec DDU) "
                                   + "fait partie des gestes de base avant d'accuser le matériel.",
                            Manual = true
                        });
                        return;
                    }
            }
            catch { }
        }
    }
}
