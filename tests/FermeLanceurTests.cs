using System;

namespace BTOptimizer.Tests
{
    /// <summary>Vérifie <see cref="FermeLanceur"/> : Steam n'est fermé qu'après la partie du jeu lancé par ONYX, et seulement si ONYX l'a ouvert.</summary>
    internal static class FermeLanceurTests
    {
        public static void Tout()
        {
            Banc.Titre("Fermer Steam à la fin de la partie");
            DateTime t = new DateTime(2026, 11, 28, 20, 0, 0);
            var l = new FermeLanceur.Lancement { Jeu = "Counter-Strike 2", Quand = t, SteamTournait = false };
            Banc.Egal("nom comparable", "counterstrike2", FermeLanceur.Norme("Counter-Strike 2"));
            Banc.Verifie("partie du jeu lancé : Steam fermé", true, FermeLanceur.Concerne(l, "Counter-Strike 2", t.AddMinutes(40)));
            Banc.Verifie("même jeu, ponctuation différente", true, FermeLanceur.Concerne(l, "counter strike 2", t.AddMinutes(40)));
            Banc.Verifie("autre jeu : rien", false, FermeLanceur.Concerne(l, "Fortnite", t.AddMinutes(40)));
            Banc.Verifie("aucun lancement noté : rien", false, FermeLanceur.Concerne(null, "Counter-Strike 2", t));
            Banc.Verifie("nom vide : rien", false, FermeLanceur.Concerne(new FermeLanceur.Lancement { Jeu = "", Quand = t }, "", t));
            Banc.Verifie("lancement trop ancien : oublié", false, FermeLanceur.Concerne(l, "Counter-Strike 2", t.AddHours(13)));
            Banc.Verifie("horloge reculée : rien", false, FermeLanceur.Concerne(l, "Counter-Strike 2", t.AddMinutes(-5)));
            l.SteamTournait = true;
            Banc.Verifie("Steam déjà ouvert avant le lancement : jamais fermé", false, FermeLanceur.Concerne(l, "Counter-Strike 2", t.AddMinutes(40)));
            Banc.Verifie("délai de 2 min", true, FermeLanceur.Delai == TimeSpan.FromMinutes(2));
        }
    }
}
