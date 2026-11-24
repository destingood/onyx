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

            // Décision de Lucas du 24/11/2026 : l'état minimal du processeur à 100 % est coché par tous les
            // presets. Sur batterie, 5 % : un portable débranché ne chauffe pas pour rien.
            string min = BlocDe(src, "proc_min_100");
            Banc.Verifie("proc_min_100 : coché par Recommandé et eSport", true,
                min.Contains("Recommended = true") && min.Contains("Esport = true"));
            Banc.Verifie("proc_min_100 : 100 % sur secteur, 5 % sur batterie", true, min.Contains(", 100, 5)"));
            Banc.Verifie("proc_min_100 : ne s'annonce plus « jamais coché »", false, min.Contains("JAMAIS"));
            Banc.Verifie("proc_min_100 : coût annoncé, gain non démontré", true,
                min.Contains("non démontré") && min.Contains("chaleur"));
            string materiel = Source("Hardware.cs") ?? "";
            Banc.Verifie("mode Auto : seuls les C-States restent manuels", true,
                materiel.Contains("NeverAuto = { \"cpu_idle_disable\" };"));
            Banc.Verifie("mode Auto : proc_min_100 toujours écarté sur portable", true,
                Regex.IsMatch(materiel, @"if \(hw\.IsLaptop\)\s*\{\s*ids\.Remove\(""proc_min_100""\);"));

            // Décision de Lucas du 24/11/2026 : ce qui a marché sur son PC entre dans tous les presets.
            string msi = BlocDe(src, "audio_msi");
            Banc.Verifie("audio_msi : coché par Recommandé et eSport", true,
                msi.Contains("Recommended = true") && msi.Contains("Esport = true"));
            Banc.Verifie("audio_msi : risque de perte de son toujours annoncé", true,
                msi.Contains("Risque = Risque.Fonction") && msi.Contains("SANS SON"));
            Banc.Verifie("audio_msi : dit qu'il est vérifié sur un seul PC et comment revenir", true,
                msi.Contains("un seul PC") && msi.Contains("-revert ids=audio_msi"));
            Banc.Verifie("audio_msi : ne s'annonce plus hors des presets", false,
                msi.Contains("Ne prends ce réglage que si") || (Source("IrqTuning.cs") ?? "").Contains("Jamais dans un preset"));
            string mpo = BlocDe(src, "mpo_off");
            Banc.Verifie("mpo_off : dans aucun preset", false,
                mpo.Contains("Esport = true") || mpo.Contains("Recommended = true"));
            Banc.Verifie("mpo_off : effet sur la latence annoncé comme non tranché", true,
                mpo.Contains("Preuve = NiveauPreuve.NonMesure") && mpo.Contains("non tranché"));
            string coeur = BlocDe(src, "gpu_coeur_dedie");
            Banc.Verifie("gpu_coeur_dedie : coché par Recommandé et eSport, effet au redémarrage", true,
                coeur.Contains("Recommended = true") && coeur.Contains("Esport = true") && coeur.Contains("Reboot = true"));
            Banc.Verifie("gpu_coeur_dedie : gain de latence annoncé comme non mesuré", true,
                coeur.Contains("Preuve = NiveauPreuve.NonMesure") && coeur.Contains("non mesuré"));

            // Mesure du 25/11/2026 sur le PC de test : sans veille, la durée des interruptions n'a pas baissé,
            // le processeur est passé de 49 à 171 W et un cœur seul a calculé 9 % moins vite.
            string veille = BlocDe(src, "cpu_idle_disable");
            Banc.Verifie("cpu_idle_disable : ne promet plus une latence d'interruption minimale", false,
                veille.Contains("latence d'interruption minimale"));
            Banc.Verifie("cpu_idle_disable : gain annoncé comme non démontré, coût mesuré cité", true,
                veille.Contains("Preuve = NiveauPreuve.NonMesure") && veille.Contains("non démontré") && veille.Contains("171 W"));
            Banc.Verifie("cpu_idle_disable : toujours annoncé « jamais coché automatiquement »", true,
                veille.Contains("JAMAIS"));

            // « Rétablir » du profil NVIDIA : valeur d'usine du mode d'alimentation = 5
            // (PREFERRED_PSTATE_DEFAULT = OPTIMAL_POWER, « Normal », NvApiDriverSettings.h), pas 0 (Adaptatif).
            string nv = Source("NvProfile.cs") ?? "";
            Banc.Verifie("profil NVIDIA Défaut : mode d'alimentation remis à 5 (usine)", true,
                Regex.IsMatch(nv, @"kind == Kind\.Defaut\) sb\.Append\(Setting\(""Power management mode"", IdPowerMode, PowerModeUsine\)\)")
                && nv.Contains("PowerModeUsine = 5;"));

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

        private static string Source(string nom = "Tweaks.cs")
        {
            for (DirectoryInfo d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            {
                string f = Path.Combine(d.FullName, "src", nom);
                if (File.Exists(f)) return File.ReadAllText(f);
            }
            return null;
        }
    }
}
