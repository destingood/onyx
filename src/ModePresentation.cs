// Règles de classement inspirées de PresentMon, Intel, licence MIT
// (Copyright (C) 2017-2024 Intel Corporation). Version simplifiée réécrite : aucun code copié.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace BTOptimizer
{
    /// <summary>
    /// Mode de présentation de l'image d'un jeu : directement à l'écran (Independent Flip, MPO,
    /// plein écran exclusif) ou par le compositeur de Windows (Composed Flip / Composed Copy).
    /// Logique PURE (aucune dépendance à ETW ni à WinForms) : la session ETW vit dans
    /// ModePresentationEtw.cs. Règles conservatrices : en cas de doute, aucun constat.
    /// </summary>
    internal static class ModePresentation
    {
        public enum ModeImage { Inconnu, PleinEcranExclusif, Direct, Compose, CopieComposee, Mixte }

        /// <summary>Compteurs relevés pendant la fenêtre d'observation.</summary>
        public sealed class Releve
        {
            public int FlipsJeu;      // Flip_Info / FlipMultiPlaneOverlay_Info du jeu
            public int CopiesJeu;     // Blit_Info, ou present historique de modèle BLT (3)
            public int FlipsCompo;    // present historique de modèle REDIRECTED_FLIP
            public int Presents;      // séquences de present distinctes du jeu
            public int Directs;       // présents retrouvés dans un flip émis par un autre processus (DWM)
            public int FlipsVus;      // flips de jointure lus (séquence non nulle), tous processus : 0 = jointure aveugle
        }

        public const int TotalMini = 30;
        public const int PresentsMini = 30;
        public const double RatioDirect = 0.5;
        public const double RatioCompose = 0.1;
        public const double FenetreJointureMs = 250;
        public const double MemoireMs = 2000;

        /// <summary>
        /// Jointure par séquence : un present du jeu est « direct » si un autre processus
        /// (IndependentFlip / MMIOFlip) émet le flip de la MÊME séquence dans les 250 ms qui suivent.
        /// Non thread-safe : l'appelant verrouille.
        /// </summary>
        public sealed class Suivi
        {
            private sealed class Entree { public double Ms; public bool Direct; }

            private readonly Dictionary<ulong, Entree> _presents = new Dictionary<ulong, Entree>();
            private readonly Queue<KeyValuePair<ulong, double>> _ordre = new Queue<KeyValuePair<ulong, double>>();
            private int _presentsVus, _directs, _flipsVus;

            public void PresentJeu(ulong seq, double ms)
            {
                Oublie(ms);
                if (_presents.ContainsKey(seq)) return;
                _presents[seq] = new Entree { Ms = ms };
                _ordre.Enqueue(new KeyValuePair<ulong, double>(seq, ms));
                _presentsVus++;
            }

            public void FlipDirect(ulong seq, double ms)
            {
                if (seq == 0) return;   // champ illisible : ni preuve de direct, ni preuve de jointure
                _flipsVus++;
                Entree e;
                if (!_presents.TryGetValue(seq, out e) || e.Direct) return;
                double ecart = ms - e.Ms;
                if (ecart < 0 || ecart > FenetreJointureMs) return;
                e.Direct = true;
                _directs++;
            }

            public Releve Releve()
            {
                return new ModePresentation.Releve { Presents = _presentsVus, Directs = _directs, FlipsVus = _flipsVus };
            }

            public void Reinitialise()
            {
                _presents.Clear();
                _ordre.Clear();
                _presentsVus = 0;
                _directs = 0;
                _flipsVus = 0;
            }

            // Mémoire bornée : on oublie les séquences vieilles de plus de 2 s.
            private void Oublie(double maintenant)
            {
                while (_ordre.Count > 0 && _ordre.Peek().Value < maintenant - MemoireMs)
                    _presents.Remove(_ordre.Dequeue().Key);
            }
        }

        public static ModeImage Classe(Releve r)
        {
            if (r == null) return ModeImage.Inconnu;
            int total = r.FlipsJeu + r.CopiesJeu + r.FlipsCompo;
            if (total < TotalMini) return ModeImage.Inconnu;
            if (r.FlipsJeu * 2 >= total) return ModeImage.PleinEcranExclusif;
            if (r.CopiesJeu * 2 >= total) return ModeImage.CopieComposee;
            if (r.FlipsCompo * 2 >= total)
            {
                if (r.Presents < PresentsMini) return ModeImage.Inconnu;
                // Aucun flip de jointure lu : la jointure n'a rien vu (événements absents ou champs
                // illisibles). « 0 direct » ne prouverait alors rien : pas de faux « composé ».
                if (r.FlipsVus == 0) return ModeImage.Inconnu;
                double ratio = (double)r.Directs / r.Presents;
                if (ratio >= RatioDirect) return ModeImage.Direct;
                if (ratio <= RatioCompose) return ModeImage.Compose;
                return ModeImage.Mixte;
            }
            return ModeImage.Inconnu;
        }

        /// <summary>Texte du constat, ou null s'il n'y a rien à signaler.</summary>
        public static string Constat(ModeImage m, string jeu, int hz, bool windowedFlipActif)
        {
            if (m != ModeImage.Compose && m != ModeImage.CopieComposee) return null;
            string chemin = m == ModeImage.Compose ? "Composed: Flip" : "Composed: Copy";
            string t = jeu + " : l'image passe par le compositeur de Windows (" + chemin + "). "
                + "Elle arrive environ un rafraîchissement plus tard";
            if (hz > 0)
                t += ", soit ~" + (1000.0 / hz).ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',')
                    + " ms à " + hz + " Hz (calcul, non mesuré)";
            t += ". ";
            t += windowedFlipActif
                ? "Le réglage « Optimisations pour les jeux en fenêtré » est déjà actif : mets le jeu en plein écran "
                  + "(sans bordure ou exclusif) et ferme ce qui se superpose à lui (superpositions, fenêtres toujours au premier plan)."
                : "Le réglage « Optimisations pour les jeux en fenêtré » peut la faire passer directement à l'écran.";
            return t;
        }

        public static string Libelle(ModeImage m)
        {
            switch (m)
            {
                case ModeImage.PleinEcranExclusif: return "plein écran exclusif (direct)";
                case ModeImage.Direct: return "direct à l'écran (Independent Flip)";
                case ModeImage.Compose: return "composé par Windows (Composed: Flip)";
                case ModeImage.CopieComposee: return "copie composée (Composed: Copy)";
                case ModeImage.Mixte: return "mixte";
                default: return "indéterminé";
            }
        }
    }
}
