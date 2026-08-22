using System;
using System.IO;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// LE RELEVÉ DE <see cref="ChaineEntree"/> — la moitié qui a besoin d'une machine.
    ///
    /// Elle ne mesure presque rien elle-même, et c'est voulu : chaque chiffre vient du module
    /// d'ONYX qui sait déjà le produire — l'affichage de DisplayInfo, le plafond d'images de
    /// FrameCap, le profil graphique de NvProfile, le pire temps noyau du dernier rapport DPC/ISR.
    /// Refaire ces mesures ici donnerait deux chiffres pour la même chose, et le jour où ils
    /// diffèrent, personne ne saurait lequel croire.
    ///
    /// Ce qui manque n'est PAS comblé. Un maillon sans source reste vide jusqu'à ce qu'on l'ait
    /// mesuré pour de bon.
    /// </summary>
    internal static partial class ChaineEntree
    {
        /// <summary>Nom du pilote responsable du pire temps noyau, s'il a été relevé.</summary>
        public static string PirePilote { get; private set; }

        /// <summary>Date du rapport DPC/ISR utilisé, pour dire ce qu'on lit.</summary>
        public static DateTime QuandDpc { get; private set; }

        /// <summary>
        /// Assemble le relevé à partir de ce que la machine et les modules d'ONYX savent dire.
        /// Chaque source est isolée : un pilote graphique absent ne doit pas priver la chaîne de
        /// sa fréquence d'affichage.
        /// </summary>
        public static Releve Mesure()
        {
            var r = new Releve();

            // ---- souris : la mesure retenue par le testeur, jamais une valeur de catalogue
            try
            {
                DateTime quand;
                int hz = DerniereSouris(out quand);
                if (hz > 0) { r.SourisHz = hz; r.SourisQuand = quand; }
            }
            catch (Exception ex) { JournalTechnique.Echec("ChaineEntree.Mesure/souris", ex); }

            // ---- affichage : fréquence actuelle et maximale À LA RÉSOLUTION ACTUELLE
            try
            {
                foreach (DisplayInfo.DisplayMode m in DisplayInfo.Query())
                {
                    if (m == null || m.CurrentHz <= 0) continue;
                    // L'écran le plus RAPIDE : c'est celui sur lequel on joue. Prendre le premier
                    // venu ferait chiffrer la chaîne sur l'écran secondaire de navigation.
                    if (!r.EcranHz.HasValue || m.CurrentHz > r.EcranHz.Value)
                    {
                        r.EcranHz = m.CurrentHz;
                        r.EcranHzMax = m.MaxHz > 0 ? m.MaxHz : m.CurrentHz;
                    }
                }
            }
            catch (Exception ex) { JournalTechnique.Echec("ChaineEntree.Mesure/ecran", ex); }

            // ---- plafond d'images posé par le pilote
            try { int fps = FrameCap.Pose(); if (fps > 0) r.PlafondFps = fps; }
            catch (Exception ex) { JournalTechnique.Echec("ChaineEntree.Mesure/plafond", ex); }

            // ---- file de rendu : lue dans le profil graphique appliqué
            //
            // « Ultra faible latence » (profils Sûr et Ultra d'ONYX) borne la file à UNE image :
            // c'est le comportement documenté du réglage, donc un chiffre lisible. Au profil par
            // défaut, en revanche, la file est laissée au jeu — on ne la connaît PAS, et supposer
            // trois images parce que c'est le cas le plus fréquent serait exactement le genre de
            // supposition que ce module refuse.
            try
            {
                NvProfile.Kind kind; DateTime quand;
                if (NvProfile.Applique(out kind, out quand) && kind != NvProfile.Kind.Defaut)
                    r.ImagesEnAttente = 1;
            }
            catch (Exception ex) { JournalTechnique.Echec("ChaineEntree.Mesure/profil", ex); }

            // ---- pire temps noyau : le dernier rapport DPC/ISR produit par ONYX
            try
            {
                string tools = Path.Combine(Application.StartupPath, "tools");
                if (Directory.Exists(tools))
                {
                    FileInfo[] f = new DirectoryInfo(tools).GetFiles("dpcisr-*.txt");
                    if (f.Length > 0)
                    {
                        Array.Sort(f, delegate (FileInfo a, FileInfo b) { return b.LastWriteTime.CompareTo(a.LastWriteTime); });
                        DpcIsrReport rap = DpcIsrReport.Parse(f[0].FullName);
                        if (rap != null && rap.WorstUs > 0)
                        {
                            r.DpcPireMs = rap.WorstUs / 1000.0;
                            PirePilote = rap.MaxDpcUs >= rap.MaxIsrUs ? rap.MaxDpcModule : rap.MaxIsrModule;
                            QuandDpc = f[0].LastWriteTime;
                        }
                    }
                }
            }
            catch (Exception ex) { JournalTechnique.Echec("ChaineEntree.Mesure/dpc", ex); }

            return r;
        }
    }
}
