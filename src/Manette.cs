using System;
using System.Collections.Generic;

namespace BTOptimizer
{
    /// <summary>
    /// MODE MANETTE — l'équivalent du « Handheld Mode » de Razer Cortex : parcourir Mes jeux à la
    /// manette Xbox (XInput). Partie PURE : boutons et stick → actions (front montant, répétition
    /// quand on maintient une direction), et déplacement dans la grille. La lecture XInput vit dans
    /// ManetteVeille.cs, l'effet dans PageGames.
    /// </summary>
    internal static class Manette
    {
        public enum Geste { Aucun, Gauche, Droite, Haut, Bas, Valider, Retour, Precedent, Suivant, Menu }

        // Masques XINPUT_GAMEPAD (documentation XInput de Microsoft).
        public const ushort CroixHaut = 0x0001, CroixBas = 0x0002, CroixGauche = 0x0004, CroixDroite = 0x0008;
        public const ushort BoutonLB = 0x0100, BoutonRB = 0x0200, BoutonA = 0x1000, BoutonB = 0x2000;
        /// <summary>Bouton Menu (« Start ») : entre dans le mode console ou en sort.</summary>
        public const ushort BoutonMenu = 0x0010;

        /// <summary>Stick à mi-course au moins (sur 32 767) : un stick au repos qui dérive ne déplace rien.</summary>
        public const int SeuilStick = 16000;
        /// <summary>Direction maintenue : première répétition après 400 ms, puis toutes les 120 ms.</summary>
        public const int PremiereRepetitionMs = 400, RepetitionMs = 120;

        public sealed class Etat
        {
            public ushort Boutons;
            public Geste Direction;
            public long ProchaineMs;
            public bool Initialise;
        }

        /// <summary>Direction lue : la croix d'abord, sinon l'axe dominant du stick gauche.</summary>
        public static Geste Direction(ushort boutons, short lx, short ly)
        {
            if ((boutons & CroixHaut) != 0) return Geste.Haut;
            if ((boutons & CroixBas) != 0) return Geste.Bas;
            if ((boutons & CroixGauche) != 0) return Geste.Gauche;
            if ((boutons & CroixDroite) != 0) return Geste.Droite;
            int ax = Math.Abs((int)lx), ay = Math.Abs((int)ly);
            if (Math.Max(ax, ay) < SeuilStick) return Geste.Aucun;
            if (ax >= ay) return lx > 0 ? Geste.Droite : Geste.Gauche;
            return ly > 0 ? Geste.Haut : Geste.Bas;   // XInput : Y positif vers le haut
        }

        /// <summary>Un relevé. Le premier ne fait que prendre l'état de référence : un bouton déjà
        /// enfoncé (A pressé dans le jeu qu'on vient de quitter) ne déclenche rien.</summary>
        public static List<Geste> Avance(Etat e, ushort boutons, short lx, short ly, long ms)
        {
            var l = new List<Geste>();
            if (e == null) return l;
            Geste d = Direction(boutons, lx, ly);
            if (!e.Initialise)
            {
                e.Initialise = true; e.Boutons = boutons; e.Direction = d; e.ProchaineMs = long.MaxValue;
                return l;
            }
            ushort neufs = (ushort)(boutons & ~e.Boutons);
            if ((neufs & BoutonA) != 0) l.Add(Geste.Valider);
            if ((neufs & BoutonB) != 0) l.Add(Geste.Retour);
            if ((neufs & BoutonLB) != 0) l.Add(Geste.Precedent);
            if ((neufs & BoutonRB) != 0) l.Add(Geste.Suivant);
            if ((neufs & BoutonMenu) != 0) l.Add(Geste.Menu);
            e.Boutons = boutons;

            if (d != e.Direction)
            {
                e.Direction = d;
                if (d != Geste.Aucun) { l.Add(d); e.ProchaineMs = ms + PremiereRepetitionMs; }
            }
            else if (d != Geste.Aucun && ms >= e.ProchaineMs)
            {
                l.Add(d);
                e.ProchaineMs = ms + RepetitionMs;
            }
            return l;
        }

        /// <summary>Nouvelle case dans une grille remplie ligne par ligne. -1 (rien de choisi) → la
        /// première case. Les bords arrêtent ; « bas » vers une dernière ligne incomplète va à sa
        /// dernière case.</summary>
        public static int Deplace(int index, int nombre, int colonnes, Geste g)
        {
            if (nombre <= 0) return -1;
            colonnes = Math.Max(1, colonnes);
            if (index < 0 || index >= nombre) return 0;
            switch (g)
            {
                case Geste.Gauche: return index % colonnes > 0 ? index - 1 : index;
                case Geste.Droite: return index % colonnes < colonnes - 1 && index + 1 < nombre ? index + 1 : index;
                case Geste.Haut: return index - colonnes >= 0 ? index - colonnes : index;
                case Geste.Bas:
                    if (index + colonnes < nombre) return index + colonnes;
                    return index / colonnes < (nombre - 1) / colonnes ? nombre - 1 : index;
                default: return index;
            }
        }
    }
}
