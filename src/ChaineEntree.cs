using System;
using System.Collections.Generic;
using System.Globalization;

namespace BTOptimizer
{
    /// <summary>
    /// DU CLIC AU PIXEL — LA CHAÎNE ENTIÈRE, EN MILLISECONDES.
    ///
    /// ONYX mesure déjà très bien chaque maillon SÉPARÉMENT : le temps noyau par pilote, le taux
    /// de rapport réel de la souris, le retard du compositeur, le plafond d'images. Ce qu'il ne
    /// faisait nulle part, c'est les ADDITIONNER — et c'est pourtant la seule question que se pose
    /// quelqu'un qui trouve sa visée molle : combien de millisecondes entre mon geste et l'image,
    /// et lequel des maillons en coûte le plus ?
    ///
    /// Le « Guide latence » existant répondait à côté : une liste de cases à cocher, qui renvoyait
    /// même à des étapes hors de l'application. Une checklist ne hiérarchise rien, et c'est
    /// exactement ce dont on a besoin ici, parce que les ordres de grandeur sont ÉCRASANTS.
    ///
    /// LE FAIT QUI CHANGE TOUT, ET QU'AUCUN TUTORIEL NE DIT :
    ///
    ///   Sur un écran 240 Hz, l'attente d'affichage vaut environ 2,1 ms et une souris à 1000 Hz
    ///   coûte 0,5 ms. Passer cette souris à 8000 Hz fait gagner 0,44 ms. Retirer UNE image en
    ///   attente dans le pilote graphique en fait gagner 4,2 ms — dix fois plus, gratuitement.
    ///   Les forums vendent pourtant le premier et ignorent le second.
    ///
    ///   C'est la même règle que l'inventaire des services, appliquée au temps : on ne vante pas
    ///   un levier à 0,4 ms quand un autre en rend 4, et un levier est classé par ce qu'il RAPPORTE
    ///   ici, pas par sa réputation.
    ///
    /// CE QUE CE MODULE REFUSE D'INVENTER :
    ///
    ///   La dalle. Le temps de réponse d'un écran ne se lit par aucune API : il n'est donc PAS
    ///   estimé, il est déclaré non mesuré. Un budget qui s'invente son dernier terme n'est plus
    ///   un budget, c'est un argumentaire.
    ///
    ///   La moyenne et le pire cas ne se mélangent pas. Le temps noyau (DPC) est un MAXIMUM
    ///   observé, pas une contribution permanente : l'ajouter au budget moyen gonflerait le total
    ///   d'un chiffre qui ne se produit presque jamais. Il a sa colonne, séparée.
    ///
    ///   Une mesure qu'on n'a pas faite. Tant que le taux de rapport n'a pas été mesuré sur CETTE
    ///   souris, le maillon reste vide — pas rempli avec le chiffre écrit sur la boîte.
    /// </summary>
    internal static partial class ChaineEntree
    {
        /// <summary>D'où sort un chiffre. Affiché tel quel : un nombre sans provenance ne se
        /// discute pas, et c'est ce qui permet à un mauvais chiffre de survivre des années.</summary>
        public enum Source
        {
            /// <summary>Relevé sur cette machine par une mesure d'ONYX.</summary>
            Mesure,
            /// <summary>Lu dans Windows ou dans le pilote (registre, API d'affichage, profil).</summary>
            Lu,
            /// <summary>Déduit des précédents par un calcul explicite.</summary>
            Calcule,
            /// <summary>Rien de fiable à dire. On le dit.</summary>
            NonMesure
        }

        /// <summary>Un maillon de la chaîne, avec ce qu'il coûte et d'où vient le chiffre.</summary>
        public sealed class Maillon
        {
            public string Nom = "";
            public string Valeur = "";        // « 1000 Hz », « 240 Hz », « 2 images »
            public string Explique = "";      // pourquoi ce maillon coûte ce temps-là
            public double? Ms;                // contribution MOYENNE
            public double? MsPire;            // pire cas, quand il a un sens
            public Source Source = Source.NonMesure;
        }

        /// <summary>Ce que la machine a rendu. Tout est facultatif : un relevé partiel doit
        /// produire une chaîne partielle, jamais une chaîne inventée.</summary>
        public sealed class Releve
        {
            public int? SourisHz;             // taux de rapport MESURÉ (médiane tenue)
            public DateTime? SourisQuand;
            public int? EcranHz;
            public int? EcranHzMax;
            public int? PlafondFps;           // plafond d'images posé par le pilote, 0/null = aucun
            public int? ImagesEnAttente;      // images pré-rendues autorisées (profil graphique)
            public double? DpcPireMs;         // pire temps noyau observé

            public Releve Copie()
            {
                return new Releve
                {
                    SourisHz = SourisHz, SourisQuand = SourisQuand, EcranHz = EcranHz,
                    EcranHzMax = EcranHzMax, PlafondFps = PlafondFps,
                    ImagesEnAttente = ImagesEnAttente, DpcPireMs = DpcPireMs
                };
            }
        }

        // ==================================================================
        //  Les trois seules formules — PURES, et chacune tient en une phrase
        // ==================================================================

        /// <summary>
        /// PUR : attente MOYENNE d'un périphérique qui rapporte <paramref name="hz"/> fois par
        /// seconde. Le geste tombe à un instant quelconque entre deux rapports, donc on attend en
        /// moyenne une DEMI-période — et une période entière au pire.
        /// </summary>
        public static double MsAttente(int hz) { return hz > 0 ? 500.0 / hz : 0; }

        /// <summary>PUR : période complète, c'est-à-dire le pire cas de la même attente.</summary>
        public static double MsPeriode(int hz) { return hz > 0 ? 1000.0 / hz : 0; }

        /// <summary>
        /// PUR : coût de la file de rendu. Chaque image que le pilote garde d'avance retarde
        /// l'affichage d'exactement une période d'image — c'est le maillon le plus cher de toute
        /// la chaîne, et le seul qui se règle sans rien acheter.
        /// </summary>
        public static double MsFileRendu(int images, int fps)
        {
            if (images <= 0 || fps <= 0) return 0;
            return images * (1000.0 / fps);
        }

        /// <summary>
        /// PUR : les images par seconde à retenir pour chiffrer la file de rendu.
        ///
        /// Le plafond s'il existe — c'est lui qui commande alors la cadence. Sinon la fréquence de
        /// l'écran, en MEILLEUR cas assumé : un jeu qui tourne moins vite paie DAVANTAGE par image
        /// en attente, jamais moins. On ne se flatte donc pas.
        /// </summary>
        public static int FpsRetenu(Releve r)
        {
            if (r == null) return 0;
            if (r.PlafondFps.HasValue && r.PlafondFps.Value > 0) return r.PlafondFps.Value;
            return r.EcranHz.HasValue ? r.EcranHz.Value : 0;
        }

        // ==================================================================
        //  La chaîne — PURE
        // ==================================================================

        public const string NomSouris = "Souris";
        public const string NomNoyau = "Pilotes (noyau)";
        public const string NomRendu = "File de rendu";
        public const string NomEcran = "Affichage";
        public const string NomDalle = "Dalle";

        /// <summary>Construit la chaîne à partir du relevé. Aucun maillon n'est omis : un maillon
        /// non mesuré s'affiche VIDE, parce que sa disparition ferait croire à un budget complet.</summary>
        public static List<Maillon> Construit(Releve r)
        {
            var l = new List<Maillon>();
            if (r == null) return l;

            // ---- souris
            var souris = new Maillon { Nom = NomSouris };
            if (r.SourisHz.HasValue && r.SourisHz.Value > 0)
            {
                int hz = r.SourisHz.Value;
                souris.Valeur = hz + " Hz";
                souris.Ms = MsAttente(hz);
                souris.MsPire = MsPeriode(hz);
                souris.Source = Source.Mesure;
                souris.Explique = "un rapport toutes les " + Nombre(MsPeriode(hz))
                                + " ms : ton geste attend en moyenne la moitié de cet intervalle";
            }
            else
            {
                souris.Valeur = "non mesuré";
                souris.Source = Source.NonMesure;
                souris.Explique = "le taux de rapport réel n'a pas été mesuré sur cette souris — "
                                + "celui écrit sur la boîte ne vaut pas une mesure";
            }
            l.Add(souris);

            // ---- noyau : PIRE CAS uniquement, jamais dans la moyenne
            var noyau = new Maillon { Nom = NomNoyau };
            if (r.DpcPireMs.HasValue && r.DpcPireMs.Value > 0)
            {
                noyau.Valeur = Nombre(r.DpcPireMs.Value) + " ms";
                noyau.MsPire = r.DpcPireMs.Value;
                noyau.Source = Source.Mesure;
                noyau.Explique = "pire temps pendant lequel un pilote a empêché le traitement d'un "
                               + "rapport. C'est un MAXIMUM observé, pas un coût permanent : il ne "
                               + "s'ajoute pas au budget moyen";
            }
            else
            {
                noyau.Valeur = "non mesuré";
                noyau.Source = Source.NonMesure;
                noyau.Explique = "aucun relevé de temps noyau — l'analyse DPC d'ONYX le fournit";
            }
            l.Add(noyau);

            // ---- file de rendu
            int fps = FpsRetenu(r);
            var rendu = new Maillon { Nom = NomRendu };
            if (r.ImagesEnAttente.HasValue && fps > 0)
            {
                int n = r.ImagesEnAttente.Value;
                rendu.Valeur = n + (n > 1 ? " images" : " image");
                rendu.Ms = MsFileRendu(n, fps);
                rendu.MsPire = rendu.Ms;
                rendu.Source = Source.Lu;
                rendu.Explique = "chaque image gardée d'avance par le pilote retarde l'affichage "
                               + "d'une période complète (" + Nombre(MsPeriode(fps)) + " ms à " + fps + " im/s)";
            }
            else
            {
                // File INCONNUE — et on ne la devine pas. Mais le coût PAR IMAGE, lui, se
                // calcule : c'est le fait actionnable, et il suffit à faire comprendre que ce
                // maillon pèse plus lourd que tous les autres réunis.
                rendu.Valeur = "laissée au jeu";
                rendu.Source = Source.NonMesure;
                rendu.Explique = fps > 0
                    ? "le pilote n'impose aucune limite : chaque image gardée d'avance coûte "
                      + Nombre(MsPeriode(fps)) + " ms. Combien y en a-t-il ? Personne ne le lit — "
                      + "et supposer trois parce que c'est le cas le plus fréquent serait une invention"
                    : "ni plafond d'images ni fréquence d'écran connus : le coût d'une image en attente "
                      + "ne peut pas être chiffré";
            }
            l.Add(rendu);

            // ---- affichage
            var ecran = new Maillon { Nom = NomEcran };
            if (r.EcranHz.HasValue && r.EcranHz.Value > 0)
            {
                int hz = r.EcranHz.Value;
                ecran.Valeur = hz + " Hz";
                ecran.Ms = MsAttente(hz);
                ecran.MsPire = MsPeriode(hz);
                ecran.Source = Source.Lu;
                ecran.Explique = "une image terminée attend le prochain balayage : en moyenne une "
                               + "demi-période, soit " + Nombre(MsAttente(hz)) + " ms";
            }
            else
            {
                ecran.Valeur = "non lu";
                ecran.Source = Source.NonMesure;
                ecran.Explique = "fréquence d'affichage non lue";
            }
            l.Add(ecran);

            // ---- dalle : jamais estimée
            l.Add(new Maillon
            {
                Nom = NomDalle,
                Valeur = "non mesurable",
                Source = Source.NonMesure,
                Explique = "le temps de réponse d'une dalle ne se lit par aucune API. ONYX ne "
                         + "l'invente pas : le budget ci-dessus est donc un PLANCHER, pas un total"
            });

            return l;
        }

        /// <summary>PUR : budget MOYEN — la somme des seuls maillons qui ont une contribution
        /// permanente et mesurée. Le pire cas n'y entre pas.</summary>
        public static double TotalMoyen(List<Maillon> chaine)
        {
            double t = 0;
            if (chaine == null) return 0;
            foreach (Maillon m in chaine) if (m != null && m.Ms.HasValue) t += m.Ms.Value;
            return t;
        }

        /// <summary>PUR : combien de maillons manquent. Un budget bâti sur trois maillons sur
        /// quatre doit le dire, sinon il se lit comme un total.</summary>
        public static int NonMesures(List<Maillon> chaine)
        {
            int n = 0;
            if (chaine == null) return 0;
            foreach (Maillon m in chaine) if (m != null && m.Source == Source.NonMesure) n++;
            return n;
        }

        /// <summary>PUR : budget moyen d'un relevé, sans passer par la chaîne.</summary>
        public static double Budget(Releve r) { return TotalMoyen(Construit(r)); }

        // ==================================================================
        //  Les leviers — classés par ce qu'ils RENDENT, pas par leur réputation
        // ==================================================================

        /// <summary>Un levier : ce qu'on change, et les millisecondes que ça rend ICI.</summary>
        public sealed class Levier
        {
            public string Nom = "";
            public string Quoi = "";        // l'action, en une phrase
            public string Coute = "";       // ce que ça coûte par ailleurs, ou ""
            public double GainMs;
            /// <summary>Part du budget moyen actuel. C'est ce chiffre-là qui range les forums.</summary>
            public double Part;
        }

        /// <summary>
        /// PUR : le gain d'un levier = la DIFFÉRENCE entre deux budgets.
        ///
        /// Calculer chaque gain à la main par une formule dédiée serait la façon sûre d'en rater
        /// un : monter la fréquence de l'écran raccourcit AUSSI la période d'image, donc la file
        /// de rendu. En rejouant le budget entier sur un relevé modifié, l'effet indirect est
        /// compté sans qu'on ait à y penser.
        /// </summary>
        public static double Gain(Releve avant, Releve apres)
        {
            double g = Budget(avant) - Budget(apres);
            return g > 0 ? g : 0;
        }

        /// <summary>
        /// PUR : tous les leviers applicables à ce relevé, du plus payant au moins payant.
        /// Un levier qui ne rend rien n'est pas listé — même règle que pour les services.
        /// </summary>
        public static List<Levier> Leviers(Releve r)
        {
            var l = new List<Levier>();
            if (r == null) return l;
            double budget = Budget(r);

            // --- images en attente : presque toujours le plus gros, et gratuit
            if (r.ImagesEnAttente.HasValue && r.ImagesEnAttente.Value > 1 && FpsRetenu(r) > 0)
            {
                Releve apres = r.Copie(); apres.ImagesEnAttente = 1;
                Ajoute(l, budget, "Images en attente", Gain(r, apres),
                    "ramener la file de rendu à une seule image (mode faible latence du pilote, ou Reflex dans le jeu)",
                    "sur une machine limitée par le processeur, une file plus courte peut coûter quelques images par seconde");
            }

            // --- écran sous son maximum : gratuit aussi, et souvent ignoré
            if (r.EcranHz.HasValue && r.EcranHzMax.HasValue && r.EcranHzMax.Value > r.EcranHz.Value + 4)
            {
                Releve apres = r.Copie(); apres.EcranHz = r.EcranHzMax;
                Ajoute(l, budget, "Fréquence d'affichage", Gain(r, apres),
                    "passer l'écran à " + r.EcranHzMax.Value + " Hz, sa fréquence maximale à cette résolution",
                    "");
            }

            // --- souris : le levier dont tout le monde parle, chiffré comme les autres
            if (r.SourisHz.HasValue && r.SourisHz.Value > 0 && r.SourisHz.Value < 1000)
            {
                Releve apres = r.Copie(); apres.SourisHz = 1000;
                Ajoute(l, budget, "Taux de rapport", Gain(r, apres),
                    "monter la souris à 1000 Hz si elle le permet",
                    "");
            }

            l.Sort(delegate (Levier a, Levier b) { return b.GainMs.CompareTo(a.GainMs); });
            return l;
        }

        private static void Ajoute(List<Levier> l, double budget, string nom, double gain, string quoi, string coute)
        {
            if (gain < 0.01) return;   // un gain sous le centième de milliseconde n'est pas un levier
            l.Add(new Levier
            {
                Nom = nom, GainMs = gain, Quoi = quoi, Coute = coute,
                Part = budget > 0 ? gain / budget : 0
            });
        }

        /// <summary>
        /// PUR : le constat, en une phrase. Il dit le budget, ce qui manque pour le compléter, et
        /// — s'il y a lieu — que le levier le plus payant n'est PAS celui qu'on croit.
        /// </summary>
        public static string Constat(Releve r)
        {
            List<Maillon> chaine = Construit(r);
            double budget = TotalMoyen(chaine);
            int manquants = NonMesures(chaine);

            if (budget <= 0)
                return "Rien de mesuré pour l'instant : la chaîne ne peut pas être chiffrée.";

            string s = "Au minimum " + Nombre(budget) + " ms entre ton geste et l'image, sur les maillons mesurés";
            s += manquants > 0 ? " (" + manquants + " maillon(s) manquant(s), dont la dalle). " : ". ";

            List<Levier> lev = Leviers(r);
            if (lev.Count == 0) return s + "Aucun levier ne rendrait quoi que ce soit ici.";

            Levier meilleur = lev[0];
            s += "Le levier le plus payant est « " + meilleur.Nom + " » : "
               + Nombre(meilleur.GainMs) + " ms, soit " + Math.Round(meilleur.Part * 100) + " % du budget.";
            return s;
        }

        /// <summary>
        /// PUR : la piste qu'on ne peut PAS chiffrer, quand la file de rendu est laissée au jeu.
        ///
        /// Elle ne rejoint pas la liste des leviers : ceux-là se classent par millisecondes, et
        /// y glisser une entrée sans nombre reviendrait à lui inventer un rang. Elle est donc
        /// rendue à part, avec le seul chiffre qu'on ait le droit d'écrire — le coût d'UNE image.
        /// Rend null quand la file est déjà connue et courte.
        /// </summary>
        public static string PisteFileRendu(Releve r)
        {
            if (r == null) return null;
            if (r.ImagesEnAttente.HasValue) return null;   // déjà lue : elle a son levier chiffré
            int fps = FpsRetenu(r);
            if (fps <= 0) return null;
            return "La file de rendu est laissée au jeu : chaque image gardée d'avance coûte "
                 + Nombre(MsPeriode(fps)) + " ms — plus que tous les autres leviers réunis. "
                 + "Le mode faible latence du pilote (profil « Sûr » d'ONYX) ou Reflex dans le jeu "
                 + "la ramène à une seule image. Le gain n'est pas chiffré ici parce que le nombre "
                 + "actuel n'est pas lisible — pas parce qu'il serait petit.";
        }

        /// <summary>Format court et français : deux décimales sous 10 ms, une au-dessus.</summary>
        public static string Nombre(double ms)
        {
            string s = ms < 10 ? ms.ToString("0.00", CultureInfo.InvariantCulture)
                               : ms.ToString("0.0", CultureInfo.InvariantCulture);
            return s.Replace('.', ',');
        }

        // ==================================================================
        //  La mesure de souris, retenue au lieu d'être jetée
        // ==================================================================

        private static string CheminSouris { get { return AppPaths.File("bt-souris-hz.txt"); } }

        /// <summary>
        /// Retient le taux de rapport mesuré par le testeur de souris.
        ///
        /// Il le calculait déjà, l'affichait, puis le PERDAIT à la fermeture de la fenêtre — le
        /// défaut que PollingVerdict reproche lui-même à l'ancien verdict, un cran plus loin. Sans
        /// ce chiffre, le premier maillon de la chaîne reste vide alors qu'on vient de le mesurer.
        /// </summary>
        public static void NoteSouris(int hzMediane)
        {
            if (hzMediane <= 0) return;
            try
            {
                System.IO.File.WriteAllText(CheminSouris,
                    hzMediane.ToString(CultureInfo.InvariantCulture) + "\t"
                    + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            }
            catch (Exception ex) { JournalTechnique.Echec("ChaineEntree.NoteSouris", ex); }
        }

        /// <summary>PUR : relecture d'une ligne de mesure. Rend 0 si elle n'a rien d'exploitable.</summary>
        public static int RelitSouris(string ligne, out DateTime quand)
        {
            quand = DateTime.MinValue;
            if (string.IsNullOrEmpty(ligne)) return 0;
            string[] p = ligne.Split('\t');
            int hz;
            if (!int.TryParse(p[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out hz)) return 0;
            if (hz <= 0) return 0;
            DateTime d;
            if (p.Length > 1 && DateTime.TryParse(p[1], CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
                quand = d;
            return hz;
        }

        public static int DerniereSouris(out DateTime quand)
        {
            quand = DateTime.MinValue;
            try
            {
                if (!System.IO.File.Exists(CheminSouris)) return 0;
                return RelitSouris(System.IO.File.ReadAllText(CheminSouris), out quand);
            }
            catch (Exception ex) { JournalTechnique.Echec("ChaineEntree.DerniereSouris", ex); return 0; }
        }
    }
}
