using System;
using System.Globalization;
using System.IO;

namespace BTOptimizer
{
    /// <summary>
    /// PLAFOND D'IMAGES — la version sans injection de ce que cherche le « Scanline Sync ».
    ///
    /// Le problème posé est réel : sans synchronisation, l'image se déchire ; avec la V-Sync
    /// classique, le jeu se retrouve à attendre l'écran et la latence grimpe d'une image entière ou
    /// plus. RTSS le résout en calant la déchirure hors du champ visible — mais pour ça il
    /// S'INJECTE dans le jeu. ONYX ne s'injecte dans aucun processus, par choix : c'est ce qui le
    /// rend compatible avec les anticheats. Cette voie lui est donc fermée, définitivement.
    ///
    /// LA VOIE OUVERTE, ET ELLE EST MEILLEURE SUR UN ÉCRAN MODERNE. Avec un écran à fréquence
    /// variable (G-Sync / FreeSync), la recette de référence est : garder la synchronisation
    /// adaptative ACTIVE, et PLAFONNER les images juste sous la fréquence maximale. Le plafond
    /// empêche le jeu d'atteindre le haut de la plage variable, là où l'écran retombe en V-Sync
    /// classique et où la latence saute. On obtient une image sans déchirure ET sans l'attente de la
    /// V-Sync — le résultat que le Scanline Sync cherche à approcher, sans rien injecter.
    ///
    /// Le plafond est posé par le PILOTE lui-même, via l'outil officiel déjà livré avec ONYX.
    /// L'identifiant du réglage et l'encodage de sa valeur ne sont pas devinés : ils sont lus dans
    /// la table de référence du pilote livrée à côté (Reference.xml), qui documente le bit
    /// d'activation, le masque des images/seconde et le jeu de drapeaux employé par le panneau
    /// NVIDIA pour son propre « Max Frame Rate ».
    ///
    /// À QUI ÇA NE SERT PAS : sur un écran SANS fréquence variable, un plafond seul ne supprime pas
    /// la déchirure — il la rend seulement plus régulière. C'est écrit plutôt que caché.
    /// </summary>
    internal static class FrameCap
    {
        /// <summary>« Frame Rate Limiter » dans la table du pilote.</summary>
        public const int IdReglage = 0x10834FEE;

        /// <summary>MFR_FLAGS — le jeu de drapeaux que le pilote emploie pour son propre
        /// « Max Frame Rate » : activé, imposé sur secteur comme sur batterie, valable en fenêtré,
        /// mode précis. Reprendre exactement le sien évite d'inventer une combinaison.</summary>
        public const uint Drapeaux = 0xF0802000;

        /// <summary>Masque des images/seconde : 10 bits, donc 1023 au maximum. La description du
        /// réglage parle d'un octet ; le masque publié à côté dit 0x3FF. C'est le masque qui fait
        /// foi — s'en tenir à l'octet plafonnerait tout le monde à 255.</summary>
        public const uint MasqueFps = 0x000003FF;

        // ------------------------------------------------------------------ pur

        /// <summary>Valeur PURE à écrire pour un plafond donné. 0 = pas de plafond (réglage rendu au
        /// pilote). Une demande hors bornes rend 0 plutôt qu'une valeur tronquée : un plafond
        /// silencieusement ramené à autre chose serait pire que pas de plafond.</summary>
        public static uint Valeur(int fps)
        {
            if (fps <= 0) return 0;
            if (fps > (int)MasqueFps) return 0;
            return Drapeaux | (uint)fps;
        }

        /// <summary>Lecture PURE : quel plafond porte cette valeur ? 0 si le bit d'activation est
        /// absent — un nombre d'images sans le bit ne plafonne rien.</summary>
        public static int Lit(uint valeur)
        {
            if ((valeur & 0x80000000u) == 0) return 0;
            return (int)(valeur & MasqueFps);
        }

        /// <summary>
        /// Plafond PUR recommandé pour une fréquence d'écran donnée, en Hz.
        ///
        /// La marge n'est pas fixe : elle doit croître avec la fréquence, parce que l'écart entre
        /// deux images se réduit quand la fréquence monte, et qu'il faut rester sous le seuil où
        /// l'écran bascule en V-Sync. La règle employée ici est celle qui circule depuis longtemps
        /// chez les mesureurs de latence : retrancher Hz² / 3600. Elle redonne les valeurs connues
        /// — 144 Hz → 138, 240 Hz → 224 — et s'étend proprement aux fréquences intermédiaires.
        ///
        /// 0 si la fréquence est absurde : on ne recommande pas un plafond au hasard.
        /// </summary>
        public static int Recommande(int hz)
        {
            if (hz < 30 || hz > 1000) return 0;
            int marge = (int)Math.Round(hz * (double)hz / 3600.0);
            if (marge < 1) marge = 1;
            int cap = hz - marge;
            return cap < 20 ? 0 : cap;
        }

        /// <summary>Contenu .nip PUR — testable sans pilote ni matériel.</summary>
        public static string Nip(int fps)
        {
            uint v = Valeur(fps);
            var sb = new System.Text.StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"utf-16\"?>\r\n<ArrayOfProfile>\r\n  <Profile>\r\n");
            sb.Append("    <ProfileName>Base Profile</ProfileName>\r\n    <Executeables />\r\n    <Settings>\r\n");
            sb.Append("      <ProfileSetting><SettingNameInfo>Frame Rate Limiter</SettingNameInfo><SettingID>");
            sb.Append(IdReglage.ToString(CultureInfo.InvariantCulture));
            sb.Append("</SettingID><SettingValue>");
            sb.Append(v.ToString(CultureInfo.InvariantCulture));
            sb.Append("</SettingValue><ValueType>Dword</ValueType></ProfileSetting>\r\n");
            sb.Append("    </Settings>\r\n    <ExecutableFindFiles />\r\n  </Profile>\r\n</ArrayOfProfile>");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ matériel

        private static string EtatPath { get { return AppPaths.File("bt-framecap.txt"); } }

        /// <summary>Fréquence de l'écran principal, en Hz. 0 si illisible.</summary>
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential,
            CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private struct DEVMODE
        {
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields;
            public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern bool EnumDisplaySettings(string nomEcran, int mode, ref DEVMODE dm);

        private const int ModeCourant = -1;

        /// <summary>
        /// Fréquence de l'écran PRINCIPAL, en Hz. 0 si illisible.
        ///
        /// ATTENTION AU PIÈGE, ET IL A ÉTÉ VÉRIFIÉ SUR UNE MACHINE À TROIS ÉCRANS : la propriété
        /// CurrentRefreshRate de Win32_VideoController décrit LA CARTE, pas l'écran sur lequel on
        /// joue. Sur un poste multi-écrans elle rend l'une des sorties, pas forcément la principale.
        /// Mesuré : trois dalles à 500, 200 et 180 Hz, et WMI répondait 200 — la sortie du milieu.
        /// Un plafond d'images calculé là-dessus aurait bridé un écran de 500 Hz à 189 images par
        /// seconde. Le pire genre de bogue : silencieux, plausible, et présenté comme une
        /// optimisation.
        ///
        /// EnumDisplaySettings(null, …) interroge l'écran PRINCIPAL et rend son mode courant, ce qui
        /// est exactement la question posée. WMI ne sert plus que de repli.
        /// </summary>
        public static int FrequenceEcran()
        {
            try
            {
                var dm = new DEVMODE();
                dm.dmSize = (short)System.Runtime.InteropServices.Marshal.SizeOf(typeof(DEVMODE));
                if (EnumDisplaySettings(null, ModeCourant, ref dm))
                {
                    int hz = dm.dmDisplayFrequency;
                    if (hz >= 30 && hz <= 1000) return hz;
                }
            }
            catch { }
            try
            {
                using (var s = new System.Management.ManagementObjectSearcher(
                    "SELECT CurrentRefreshRate FROM Win32_VideoController"))
                    foreach (System.Management.ManagementObject mo in s.Get())
                    {
                        object v = mo["CurrentRefreshRate"];
                        if (v == null) continue;
                        int hz = Convert.ToInt32(v);
                        if (hz >= 30 && hz <= 1000) return hz;
                    }
            }
            catch { }
            return 0;
        }

        /// <summary>Pose le plafond via l'outil officiel du pilote. fps = 0 rend la main au pilote.</summary>
        public static bool Applique(int fps, Action<string, int> log)
        {
            if (fps != 0 && Valeur(fps) == 0)
            {
                if (log != null) log("Plafond de " + fps + " images/s hors bornes (1 à " + MasqueFps
                                   + ") — rien n'a été changé.", 3);
                return false;
            }
            try
            {
                string exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"tools\npi\nvidiaProfileInspector.exe");
                if (!File.Exists(exe))
                {
                    if (log != null) log("L'outil de profil du pilote est introuvable : plafond non appliqué.", 3);
                    return false;
                }
                string nip = Path.Combine(Path.GetTempPath(), "onyx-framecap.nip");
                File.WriteAllText(nip, Nip(fps), new System.Text.UnicodeEncoding(false, true));
                NativeResult r = Sys.Run(exe, "-silentImport \"" + nip + "\"");
                try { File.Delete(nip); } catch { }
                if (r.ExitCode != 0)
                {
                    if (log != null) log("Le pilote a refusé le plafond (code " + r.ExitCode + ").", 3);
                    return false;
                }
            }
            catch (Exception ex)
            {
                if (log != null) log("Plafond impossible : " + ex.Message, 3);
                return false;
            }

            try
            {
                if (fps == 0) { if (File.Exists(EtatPath)) File.Delete(EtatPath); }
                else File.WriteAllText(EtatPath, fps.ToString(CultureInfo.InvariantCulture) + "\n");
            }
            catch { }

            if (log != null)
                log(fps == 0
                    ? "Plafond d'images retiré : le pilote reprend la main."
                    : "Plafond posé à " + fps + " images/s. Sur un écran à fréquence variable, garde "
                      + "G-Sync/FreeSync ACTIF : c'est la combinaison des deux qui supprime la "
                      + "déchirure sans l'attente de la V-Sync. Sans fréquence variable, le plafond "
                      + "régularise les images mais ne supprime pas la déchirure.", 2);
            return true;
        }

        /// <summary>Plafond posé par ONYX, 0 si aucun. On se fie à la mémoire de l'app : le pilote
        /// n'expose pas ce réglage en lecture par un chemin fiable.</summary>
        public static int Pose()
        {
            try
            {
                if (!File.Exists(EtatPath)) return 0;
                int v;
                return int.TryParse(File.ReadAllText(EtatPath).Trim(), out v) && v > 0 ? v : 0;
            }
            catch { return 0; }
        }
    }
}
