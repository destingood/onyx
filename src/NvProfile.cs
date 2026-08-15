using System;
using System.Globalization;
using System.IO;

namespace BTOptimizer
{
    /// <summary>
    /// PROFIL PILOTE NVIDIA — et le piège qu'il faut cesser de tendre à l'utilisateur.
    ///
    /// « Ultra Low Latency » + « 1 image pré-rendue » suppriment la file d'attente de rendu. Ce
    /// tampon de 2-3 images est précisément ce qui ABSORBE les à-coups du processeur : sans lui,
    /// chaque hoquet du CPU devient immédiatement une image perdue. NVIDIA le dit lui-même — le
    /// mode Ultra est bénéfique quand on est limité par le GPU, et il FAIT PERDRE DES IMAGES quand
    /// on est limité par le processeur.
    ///
    /// Cas réel qui a motivé ce module : 9900K à 90 % d'occupation, RTX 4080 SUPER à 40 % et 110 W
    /// sur 400 — la carte attendait des images livrées « juste à temps ». Résultat : moins de FPS
    /// qu'avant « optimisation », et des chutes brutales à chaque pic CPU.
    ///
    /// D'où trois règles ici :
    ///  • le profil SÛR (latence basse SANS assécher la file) est le défaut ;
    ///  • le profil ULTRA reste disponible, mais annoncé pour ce qu'il est : un échange
    ///    images-contre-latence, mauvais sur une machine limitée par le processeur ;
    ///  • tout ce qui est appliqué peut être RETIRÉ par l'app (avant, il fallait aller le défaire
    ///    à la main dans le panneau NVIDIA).
    /// </summary>
    internal static class NvProfile
    {
        public enum Kind
        {
            /// <summary>Réglages d'usine du pilote : plus aucune contrainte imposée.</summary>
            Defaut,
            /// <summary>Latence réduite sans assécher la file de rendu — recommandé partout.</summary>
            Sur,
            /// <summary>Latence minimale absolue. Ne vaut QUE sur une machine limitée par le GPU.</summary>
            Ultra
        }

        // Identifiants NVAPI (les mêmes que ceux qu'utilise nvidiaProfileInspector).
        private const int IdCplState = 390467;      // 0 = Off, 1 = On, 2 = Ultra
        private const int IdPreRendered = 8102046;  // 0 = laisser l'application décider
        private const int IdPowerMode = 274197361;  // 1 = privilégier les performances maximales
        private const int IdUllEnabled = 277041152; // 0/1

        /// <summary>Contenu .nip d'un profil. PUR → testable sans pilote ni matériel.</summary>
        public static string Nip(Kind kind)
        {
            int cpl, pre, ull;
            switch (kind)
            {
                case Kind.Ultra: cpl = 2; pre = 1; ull = 1; break;
                // « On » plutôt qu'« Ultra », et surtout la file de rendu RENDUE au jeu (0 =
                // l'application décide) : c'est elle qui amortit les à-coups du processeur.
                case Kind.Sur: cpl = 1; pre = 0; ull = 1; break;
                default: cpl = 0; pre = 0; ull = 0; break;
            }
            var sb = new System.Text.StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"utf-16\"?>\r\n<ArrayOfProfile>\r\n  <Profile>\r\n");
            sb.Append("    <ProfileName>Base Profile</ProfileName>\r\n    <Executeables />\r\n    <Settings>\r\n");
            sb.Append(Setting("Ultra Low Latency - CPL State", IdCplState, cpl));
            sb.Append(Setting("Maximum pre-rendered frames", IdPreRendered, pre));
            sb.Append(Setting("Ultra Low Latency - Enabled", IdUllEnabled, ull));

            // LE MODE « PERFORMANCES MAXIMALES » N'EST PLUS POSÉ D'OFFICE.
            //
            // Le commentaire précédent disait « ne coûte aucune image ». C'est vrai des FPS, et
            // faux de tout le reste : ce réglage INTERDIT à la carte de redescendre en fréquence.
            // Elle reste en P0 en permanence — au bureau, pendant qu'on lit un texte, écran de
            // veille compris.
            //
            // Constaté sur une machine réelle, profil « Sûr » posé le matin même : GPU à
            // 2 625 MHz, P-state P0, 56 W, sur un bureau vide. Et le pilote graphique consommait
            // 3 850 ms de temps noyau par minute — contre 57 ms avant que le profil soit posé,
            // soit soixante-sept fois moins. L'utilisateur cherchait d'où venait sa latence ;
            // elle venait de nous.
            //
            // « Sûr » doit être sûr : il règle la latence de rendu, il n'a pas à clouer la carte
            // à sa fréquence maximale vingt-quatre heures sur vingt-quatre. Seul « Ultra », qui
            // est un choix explicite de performance, le conserve.
            if (kind == Kind.Ultra) sb.Append(Setting("Power management mode", IdPowerMode, 1));

            // Retour aux défauts : on REMET la valeur au lieu de simplement omettre la ligne.
            // Omettre ne rétablit rien — nvidiaProfileInspector n'applique que ce qu'on lui
            // donne. « Retire() » laissait donc la carte en performances maximales pour
            // toujours, en annonçant un retour aux réglages d'usine.
            if (kind == Kind.Defaut) sb.Append(Setting("Power management mode", IdPowerMode, 0));
            sb.Append("    </Settings>\r\n    <ExecutableFindFiles />\r\n  </Profile>\r\n</ArrayOfProfile>");
            return sb.ToString();
        }

        private static string Setting(string name, int id, int value)
        {
            return "      <ProfileSetting><SettingNameInfo>" + name + "</SettingNameInfo><SettingID>"
                 + id.ToString(CultureInfo.InvariantCulture) + "</SettingID><SettingValue>"
                 + value.ToString(CultureInfo.InvariantCulture)
                 + "</SettingValue><ValueType>Dword</ValueType></ProfileSetting>\r\n";
        }

        /// <summary>
        /// Décision PURE : quel profil proposer, connaissant la charge CPU et l'utilisation GPU
        /// relevées EN JEU (−1 = mesure absente). Sans mesure, on ne prend pas de risque : SÛR.
        /// </summary>
        public static Kind Recommande(double cpuAvg, double gpuAvg)
        {
            if (cpuAvg < 0 || gpuAvg < 0) return Kind.Sur;
            // Limité par le GPU (la carte travaille à fond) : la file de rendu ne sert plus à
            // absorber quoi que ce soit, Ultra devient réellement gagnant sur la latence.
            if (gpuAvg >= 93 && cpuAvg < 85) return Kind.Ultra;
            return Kind.Sur;
        }

        /// <summary>
        /// Le profil Ultra est-il nocif dans cet état mesuré ?
        ///
        /// Le critère tient en une ligne : Ultra n'est bénéfique QUE si la carte graphique travaille
        /// déjà à fond. Dès qu'elle a de la marge, supprimer la file de rendu ne fait que sérialiser
        /// le travail — et ça se paie en images.
        ///
        /// La première version exigeait en plus un CPU au-dessus de 70 %. C'était trop étroit, et
        /// une mesure réelle l'a montré : Destiny 2 tournait à CPU 45 % / GPU 42 %, les DEUX à
        /// moitié occupés. C'est justement la signature du mal — sans tampon, le processeur prépare
        /// une image puis attend la carte, qui rend puis attend le processeur. Chacun passe la
        /// moitié du temps à ne rien faire, et l'ancien critère ne voyait rien.
        /// </summary>
        public static bool UltraNocif(double cpuAvg, double gpuAvg)
        {
            if (cpuAvg < 0 || gpuAvg < 0) return false;   // sans mesure, aucune accusation
            return gpuAvg < 85;
        }

        public static string Libelle(Kind k)
        {
            switch (k)
            {
                case Kind.Ultra: return "Ultra (latence minimale, coûte des images si le CPU limite)";
                case Kind.Sur: return "Sûr (latence réduite, file de rendu préservée)";
                default: return "Réglages d'usine du pilote";
            }
        }

        // ---------- Mémoire de ce qui a été appliqué ----------

        private static string StatePath { get { return AppPaths.File("bt-nvprofile.txt"); } }

        /// <summary>Lecture PURE de l'état mémorisé : « Ultra|2026-07-14 ».</summary>
        public static bool Parse(string content, out Kind kind, out DateTime when)
        {
            kind = Kind.Defaut; when = DateTime.MinValue;
            if (string.IsNullOrEmpty(content)) return false;
            string line = content.Replace("\r", "").Split('\n')[0].Trim();
            if (line.Length == 0) return false;
            string name = line, date = null;
            int bar = line.IndexOf('|');
            if (bar > 0) { name = line.Substring(0, bar).Trim(); date = line.Substring(bar + 1).Trim(); }
            if (string.Equals(name, "Ultra", StringComparison.OrdinalIgnoreCase)) kind = Kind.Ultra;
            else if (string.Equals(name, "Sur", StringComparison.OrdinalIgnoreCase)) kind = Kind.Sur;
            else if (string.Equals(name, "Defaut", StringComparison.OrdinalIgnoreCase)) kind = Kind.Defaut;
            else return false;
            if (date != null)
                DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out when);
            return true;
        }

        public static string Render(Kind kind, DateTime when)
        {
            return kind.ToString() + "|" + (when == DateTime.MinValue ? "" : when.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) + "\n";
        }

        /// <summary>Dernier profil appliqué PAR L'APP. false si elle n'a jamais rien appliqué.</summary>
        public static bool Applique(out Kind kind, out DateTime when)
        {
            kind = Kind.Defaut; when = DateTime.MinValue;
            try
            {
                if (!File.Exists(StatePath)) return false;
                return Parse(File.ReadAllText(StatePath), out kind, out when);
            }
            catch { return false; }
        }

        private static void Memorise(Kind kind)
        {
            try { File.WriteAllText(StatePath, Render(kind, DateTime.Now.Date)); }
            catch { }
        }

        // ---------- Application ----------

        /// <summary>Écrit le .nip puis le fait importer par nvidiaProfileInspector. true si le
        /// pilote l'a accepté.</summary>
        public static bool Applique(Kind kind, Action<string, int> log)
        {
            string exe = Sys.FindNvpi();
            if (exe == null)
            {
                if (log != null) log("nvidiaProfileInspector.exe introuvable (attendu dans tools\\npi\\) : profil NVIDIA non modifié.", 3);
                return false;
            }
            string nip;
            try
            {
                nip = AppPaths.File("bt-nvidia-" + kind.ToString().ToLowerInvariant() + ".nip");
                File.WriteAllText(nip, Nip(kind), new System.Text.UnicodeEncoding(false, true));
            }
            catch (Exception ex)
            {
                if (log != null) log("Profil NVIDIA : écriture impossible (" + ex.Message + ").", 3);
                return false;
            }
            try
            {
                NativeResult r = Sys.Run(exe, "-silentImport \"" + nip + "\"");
                if (r.ExitCode != 0)
                {
                    if (log != null) log("nvidiaProfileInspector a retourné le code " + r.ExitCode + ".", 2);
                    return false;
                }
            }
            catch (Exception ex)
            {
                if (log != null) log("Profil NVIDIA : échec (" + ex.Message + ").", 3);
                return false;
            }
            Memorise(kind);
            if (log != null) log("Profil pilote NVIDIA appliqué — " + Libelle(kind) + ".", 1);
            return true;
        }

        /// <summary>Retire tout ce que l'app a imposé au pilote (retour aux réglages d'usine).</summary>
        public static bool Retire(Action<string, int> log) { return Applique(Kind.Defaut, log); }

        /// <summary>
        /// AUTO-RÉPARATION AU LANCEMENT — ONYX défait de lui-même le tort qu'il a causé.
        ///
        /// Attendre que l'utilisateur ouvre un panneau et clique n'est pas suffisant ici : le
        /// réglage fautif a été posé PAR L'APP, il coûte des images à chaque partie, et la personne
        /// concernée ne soupçonne même pas qu'il existe. On le retire donc tout seul.
        ///
        /// Quatre verrous, parce qu'on touche au pilote graphique sans rien demander :
        ///  1. seulement si c'est ONYX qui a posé « Ultra » (jamais un réglage fait à la main) ;
        ///  2. seulement s'il existe une VRAIE mesure faite en jeu — sans preuve, on ne touche à rien ;
        ///  3. seulement si cette mesure montre un PC limité par le processeur ;
        ///  4. et l'action est écrite au journal, jamais silencieuse.
        /// </summary>
        public static bool SoigneSiNocif(Action<string, int> log)
        {
            try
            {
                Kind pose; DateTime quand;
                if (!Applique(out pose, out quand) || pose != Kind.Ultra) return false;   // (1)
                double cpuAvg, gpuAvg; DateTime mesureLe;
                if (!Bottleneck.LastMeasure(out cpuAvg, out gpuAvg, out mesureLe)) return false;   // (2)
                if (!UltraNocif(cpuAvg, gpuAvg)) return false;   // (3)
                if (Sys.FindNvpi() == null) return false;
                if (!Applique(Kind.Sur, null)) return false;
                if (log != null)   // (4)
                    log("Profil NVIDIA « Ultra faible latence » retiré automatiquement : ta machine est limitée par le "
                        + "processeur (CPU " + Math.Round(cpuAvg) + " %, GPU " + Math.Round(gpuAvg) + " % en jeu), ce profil "
                        + "supprimait la file de rendu et te faisait perdre des images. Profil sûr appliqué à la place.", 2);
                return true;
            }
            catch { return false; }
        }
    }
}
