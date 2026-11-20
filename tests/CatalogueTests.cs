using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Catalogue des réglages, lu dans le SOURCE (Tweaks.cs dépend du registre et ne se compile
    /// pas dans le banc) : identifiants uniques, et réglages sans gain mesuré annoncés comme tels.
    /// </summary>
    internal static class CatalogueTests
    {
        public static void Tout()
        {
            Banc.Titre("Catalogue des réglages");
            string src = Source();
            Banc.Verifie("src/Tweaks.cs trouvé", true, src != null);
            if (src == null) return;

            var vus = new HashSet<string>();
            var doublons = new List<string>();
            foreach (Match m in Regex.Matches(src, "\\bId = \"([^\"]+)\""))
                if (!vus.Add(m.Groups[1].Value)) doublons.Add(m.Groups[1].Value);
            Banc.Verifie("identifiants uniques" + (doublons.Count > 0 ? " (doublon : " + string.Join(", ", doublons) + ")" : ""),
                true, doublons.Count == 0);

            foreach (string id in new[] { "sysresp", "netthrottle", "tcp_bbr2" })
            {
                Match d = Regex.Match(src, "\\bId = \"" + id + "\"[\\s\\S]*?\\bDesc = \"((?:[^\"\\\\]|\\\\.)*)\"");
                Banc.Verifie(id + " : effet non démontré annoncé", true, d.Success && d.Groups[1].Value.Contains("non démontré"));
            }

            Surete(src);
        }

        /// <summary>Risque et preuve de chaque réglage, et filet contre une écriture non journalisée « Sûre ».</summary>
        private static void Surete(string src)
        {
            // Model.cs est compilé dans le banc : la règle « Sûr » se vérifie sur le vrai type.
            Banc.Verifie("Sûr : risque aucun + mesuré", true, new Tweak { Preuve = NiveauPreuve.Mesure }.Sur);
            Banc.Verifie("Sûr : risque aucun + sans objet", true, new Tweak { Preuve = NiveauPreuve.SansObjet }.Sur);
            Banc.Verifie("Sûr : non mesuré exclu", false, new Tweak().Sur);
            Banc.Verifie("Sûr : sécurité exclue", false, new Tweak { Risque = Risque.Securite, Preuve = NiveauPreuve.Mesure }.Sur);
            Banc.Verifie("Sûr : annulation non garantie exclue", false,
                new Tweak { Risque = Risque.AnnulationNonGarantie, Preuve = NiveauPreuve.Mesure }.Sur);

            foreach (string id in new[] { "spectre_off", "vbs_off" })
                Banc.Verifie(id + " : Risque.Securite", true, BlocDe(src, id).Contains("Risque = Risque.Securite"));
            Banc.Verifie("hpet_cleanup : annulation non garantie", true,
                BlocDe(src, "hpet_cleanup").Contains("Risque.AnnulationNonGarantie"));

            foreach (string id in new[] { "audio_msi", "windowed_flip", "frame_cap" })
                Banc.Verifie(id + " : gain mesuré", true, BlocDe(src, id).Contains("Preuve = NiveauPreuve.Mesure"));
            foreach (string id in new[] { "gpu_msi", "msi_usb", "msi_storage", "msi_network" })
            {
                Banc.Verifie(id + " : dépend du PC", true, BlocDe(src, id).Contains("Preuve = NiveauPreuve.DependDuPc"));
                // Sans journal, l'origine est inconnue : « Rétablir » ne doit pas supprimer la valeur du pilote.
                Banc.Verifie(id + " : Rétablir sans journal ne supprime rien", true,
                    BlocDe(src, id).Contains("Revert = () => { }") && !BlocDe(src, id).Contains("false, null)"));
            }
            foreach (string id in new[] { "timer_global", "distribuer_minuteurs", "irq_priorite_gpu", "w32ps", "fso_disable",
                                          "nagle", "netthrottle", "sysresp", "tcp_bbr2" })
                Banc.Verifie(id + " : non mesuré explicite", true, BlocDe(src, id).Contains("Preuve = NiveauPreuve.NonMesure"));

            // Règle posée en fin de Catalog.All() : Confidentialité et Souris sans enjeu de performance,
            // sans jamais écraser une preuve déjà posée.
            Banc.Verifie("fin de Catalog.All() : SansObjet pour Confidentialité et Souris", true,
                Regex.IsMatch(src, @"Cat\.Privacy[\s\S]{0,200}Cat\.Souris[\s\S]{0,200}NiveauPreuve\.SansObjet")
                && Regex.IsMatch(src, @"Preuve == NiveauPreuve\.NonMesure"));

            // Filet : tout bloc qui appelle Sys.Run/RunThrow, NvProfile., FrameCap. ou Native. porte un risque autre que Aucun.
            int vus = 0;
            foreach (var (id, bloc) in Blocs(src))
            {
                vus++;
                if (Regex.IsMatch(bloc, @"Sys\.Run(Throw)?\(|NvProfile\.|FrameCap\.|Native\."))
                    Banc.Verifie(id + " : écriture non journalisée hors « Sûr »", true,
                        Regex.IsMatch(bloc, @"Risque = Risque\.(Securite|Fonction|AnnulationNonGarantie)"));
            }
            Banc.Verifie("filet : 210 blocs lus", true, vus >= 210);

            ListeSure(src);
        }

        /// <summary>
        /// Liste « Sûr » figée : un réglage qui y entre ou en sort doit faire échouer le banc (décision
        /// à prendre exprès). Recalculée depuis le source comme Model.Sur : risque Aucun et preuve autre
        /// que NonMesure, avec la règle de fin de Catalog.All() (Confidentialité, Souris → SansObjet).
        /// </summary>
        private static void ListeSure(string src)
        {
            var sur = new List<string>();
            foreach (var (id, bloc) in Blocs(src))
            {
                Match r = Regex.Match(bloc, @"\bRisque = Risque\.(\w+)");
                if (r.Success && r.Groups[1].Value != "Aucun") continue;
                Match p = Regex.Match(bloc, @"\bPreuve = NiveauPreuve\.(\w+)");
                string preuve = p.Success ? p.Groups[1].Value : "NonMesure";
                Match c = Regex.Match(bloc, @"\bCategory = Cat\.(\w+)");
                if (preuve == "NonMesure" && c.Success && (c.Groups[1].Value == "Privacy" || c.Groups[1].Value == "Souris"))
                    preuve = "SansObjet";
                if (preuve != "NonMesure") sur.Add(id);
            }
            sur.Sort(StringComparer.Ordinal);
            const string attendu = "advertising_id_off consumer_features_off etw_diagtrack_off explorer_ads_off feedback_off "
                + "filter_toggle_keys_off gpu_msi inking_personalization_off input_queues keyboard_delay lang_list_access_off "
                + "lockscreen_tips_off mouse_markc_curve msi_network msi_usb search_highlights_off search_history_off "
                + "sticky_keys_off suggested_content_off tailored_experiences_off telemetry_policy typing_insights_off "
                + "usb_device_power windowed_flip";
            Banc.Egal("liste « Sûr » figée (24 réglages)", attendu, string.Join(" ", sur));
        }

        /// <summary>Texte d'un réglage : de « Id = "&lt;id&gt;" » jusqu'au « }); » suivant.</summary>
        private static string BlocDe(string src, string id)
        {
            Match m = Regex.Match(src, "\\bId = \"" + Regex.Escape(id) + "\"[\\s\\S]*?\\}\\);");
            return m.Success ? m.Value : "";
        }

        private static IEnumerable<(string Id, string Bloc)> Blocs(string src)
        {
            foreach (Match m in Regex.Matches(src, "\\bId = \"([^\"]+)\"[\\s\\S]*?\\}\\);"))
                yield return (m.Groups[1].Value, m.Value);
        }

        private static string Source()
        {
            for (DirectoryInfo d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            {
                string f = Path.Combine(d.FullName, "src", "Tweaks.cs");
                if (File.Exists(f)) return File.ReadAllText(f);
            }
            return null;
        }
    }
}
