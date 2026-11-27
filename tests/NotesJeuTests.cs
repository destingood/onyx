using System.Collections.Generic;

namespace BTOptimizer.Tests
{
    /// <summary>Note et liens par jeu : adresses acceptées ou refusées, codage de la note, lecture et écriture.</summary>
    internal static class NotesJeuTests
    {
        private static string Url(string saisi)
        {
            string e;
            return NotesJeu.UrlPropre(saisi, out e);
        }

        private static string Raison(string saisi)
        {
            string e;
            NotesJeu.UrlPropre(saisi, out e);
            return e;
        }

        public static void Tout()
        {
            Banc.Titre("Notes et liens du jeu — adresses");
            Banc.Egal("une adresse https est gardée telle quelle", "https://fortnite.gg/map", Url("https://fortnite.gg/map"));
            Banc.Egal("une adresse http est acceptée", "http://exemple.fr/guide", Url("http://exemple.fr/guide"));
            Banc.Egal("sans protocole, https est ajouté", "https://wiki.gg/x", Url("  wiki.gg/x "));
            Banc.Egal("un port après le nom du site n'est pas pris pour un protocole", "https://site.fr:8080/x", Url("site.fr:8080/x"));
            Banc.Egal("une adresse avec paramètres est gardée", "https://exemple.fr/a?b=1&c=2#d", Url("https://exemple.fr/a?b=1&c=2#d"));
            Banc.Verifie("une adresse vide est refusée", true, Url("   ") == null);
            Banc.Egal("la raison du refus d'une adresse vide", "L'adresse est vide.", Raison(""));
            Banc.Verifie("un chemin de programme est refusé", true, Url("C:\\Jeux\\jeu.exe") == null);
            Banc.Verifie("un lien file:// est refusé", true, Url("file:///C:/Windows/notepad.exe") == null);
            Banc.Verifie("un protocole d'application est refusé", true, Url("steam://run/730") == null);
            Banc.Verifie("un protocole sans barres est refusé", true, Url("mailto:quelquun@exemple.fr") == null);
            Banc.Verifie("javascript: est refusé", true, Url("javascript:alert(1)") == null);
            Banc.Verifie("un protocole ftp est refusé", true, Url("ftp://exemple.fr/x") == null);
            Banc.Verifie("une adresse avec une espace est refusée", true, Url("https://exemple.fr/a b") == null);
            Banc.Verifie("une adresse avec un guillemet est refusée", true, Url("https://exemple.fr/\"x") == null);
            Banc.Verifie("un nom sans point n'est pas un site", true, Url("localhost") == null);
            Banc.Verifie("une adresse avec identifiant est refusée", true, Url("https://nom:secret@exemple.fr/") == null);
            Banc.Verifie("une adresse trop longue est refusée", true, Url("https://exemple.fr/" + new string('a', NotesJeu.MaxUrl)) == null);

            Banc.Titre("Notes et liens du jeu — titres et note");
            Banc.Egal("le titre proposé est le nom du site sans www", "fortnite.gg", NotesJeu.TitreParDefaut("https://www.fortnite.gg/map"));
            Banc.Egal("pas de titre proposé pour une adresse illisible", "", NotesJeu.TitreParDefaut("pas une adresse"));
            Banc.Egal("un titre perd ses tabulations et retours", "Carte de la saison", NotesJeu.TitrePropre(" Carte\tde\nla saison "));
            Banc.Egal("un titre est coupé à la longueur maximale", NotesJeu.MaxTitre.ToString(), NotesJeu.TitrePropre(new string('t', 200)).Length.ToString());
            Banc.Egal("la note unifie ses fins de ligne", "ligne 1\nligne 2\nligne 3", NotesJeu.NotePropre("ligne 1\r\nligne 2\rligne 3\r\n"));
            Banc.Egal("la note perd ses tabulations", "a b", NotesJeu.NotePropre("a\tb"));
            Banc.Egal("la note est bornée", NotesJeu.MaxNote.ToString(), NotesJeu.NotePropre(new string('n', 5000)).Length.ToString());
            Banc.Egal("la note tient sur une ligne de fichier", "DLSS Qualité\\n144 FPS", NotesJeu.Code("DLSS Qualité\n144 FPS"));
            Banc.Egal("une barre oblique inverse est doublée", "C:\\\\Jeux", NotesJeu.Code("C:\\Jeux"));
            string piege = "dossier C:\\nouveau\nfin \\";
            Banc.Egal("le codage se relit à l'identique, même avec « \\n » écrit en toutes lettres", piege, NotesJeu.Decode(NotesJeu.Code(piege)));

            Banc.Titre("Notes et liens du jeu — enregistrement");
            var l = new List<NotesJeu.Fiche>();
            var liens = new List<NotesJeu.Lien>
            {
                new NotesJeu.Lien { Url = "fortnite.gg/map", Titre = "Carte" },
                new NotesJeu.Lien { Url = "https://FORTNITE.gg/map", Titre = "Doublon" },
                new NotesJeu.Lien { Url = "steam://run/1", Titre = "Refusé" },
                new NotesJeu.Lien { Url = "https://www.exemple.fr/guide", Titre = "" }
            };
            Banc.Verifie("la note et les liens sont posés", true, NotesJeu.Pose(l, "Fortnite", 4, "Sensibilité 8 %\nDLSS Qualité", liens));
            NotesJeu.Fiche f = NotesJeu.Trouve(l, "Fortnite");
            Banc.Verifie("la fiche du jeu se retrouve", true, f != null);
            Banc.Egal("les liens invalides ou en double sont écartés", "2", f.Liens.Count.ToString());
            Banc.Egal("le lien sans protocole est complété", "https://fortnite.gg/map", f.Liens[0].Url);
            Banc.Egal("un lien sans titre prend le nom du site", "exemple.fr", f.Liens[1].Titre);
            Banc.Egal("la note compte pour un, chaque lien pour un", "3", f.Nombre.ToString());
            Banc.Verifie("le jeu se retrouve quelle que soit la casse du nom", true, NotesJeu.Trouve(l, "FORTNITE") != null);
            Banc.Verifie("un autre jeu n'a rien", true, NotesJeu.Trouve(l, "Cyberpunk 2077") == null);

            string texte = NotesJeu.Ecrire(l);
            Banc.Egal("une ligne par jeu", "1", (texte.Split('\n').Length - 1).ToString());
            List<NotesJeu.Fiche> relu = NotesJeu.Lire(texte);
            Banc.Egal("la relecture rend le même fichier", texte, NotesJeu.Ecrire(relu));
            Banc.Egal("la note relue garde son retour à la ligne", "Sensibilité 8 %\nDLSS Qualité", relu[0].Note);
            Banc.Egal("le titre relu est le bon", "Carte", relu[0].Liens[0].Titre);
            Banc.Egal("l'appréciation relue est la bonne", "4", relu[0].Etoiles.ToString());
            Banc.Egal("l'appréciation s'écrit sur 5", "4/5", NotesJeu.TexteEtoiles(4));
            Banc.Egal("un jeu pas noté n'affiche rien", "", NotesJeu.TexteEtoiles(0));
            Banc.Egal("une appréciation hors bornes vaut « pas noté »", "0", (NotesJeu.EtoilesPropres(9) + NotesJeu.EtoilesPropres(-2)).ToString());
            var seule = new List<NotesJeu.Fiche>();
            Banc.Verifie("une appréciation seule suffit à garder la fiche", true, NotesJeu.Pose(seule, "Hades II", 5, "", null) && seule.Count == 1);
            Banc.Egal("l'appréciation seule se relit", "5", NotesJeu.Lire(NotesJeu.Ecrire(seule))[0].Etoiles.ToString());
            Banc.Egal("sans recommandation, la ligne reste celle des versions précédentes", BibliothequeJeux.Cle("Hades II") + "\t5\t\n", NotesJeu.Ecrire(seule));
            var reco = new List<NotesJeu.Fiche>();
            Banc.Verifie("« je recommande » seul suffit à garder la fiche", true, NotesJeu.Pose(reco, "Hades II", 0, "", null, true) && reco.Count == 1);
            NotesJeu.Pose(reco, "Hades II", 4, "Top", null, true);
            Banc.Egal("la recommandation s'écrit après les étoiles", BibliothequeJeux.Cle("Hades II") + "\t4,r\tTop\n", NotesJeu.Ecrire(reco));
            NotesJeu.Fiche relue = NotesJeu.Lire(NotesJeu.Ecrire(reco))[0];
            Banc.Verifie("étoiles et recommandation se relisent", true, relue.Etoiles == 4 && relue.Recommande);
            Banc.Verifie("une marque inconnue après la virgule ne recommande pas", false, NotesJeu.Lire("jeu\t3,x\tn\n")[0].Recommande);

            Banc.Verifie("remplacer garde une seule fiche par jeu", true, NotesJeu.Pose(l, "fortnite", 0, "Nouvelle note", null) && l.Count == 1);
            Banc.Egal("la note est remplacée", "Nouvelle note", NotesJeu.Trouve(l, "Fortnite").Note);
            Banc.Egal("les liens non repassés sont retirés", "0", NotesJeu.Trouve(l, "Fortnite").Liens.Count.ToString());
            Banc.Verifie("tout vider retire la fiche", true, NotesJeu.Pose(l, "Fortnite", 0, "  ", new List<NotesJeu.Lien>()) && l.Count == 0);
            Banc.Verifie("un nom de jeu vide est refusé", false, NotesJeu.Pose(l, "", 3, "note", null));

            var trop = new List<NotesJeu.Lien>();
            for (int i = 0; i < 30; i++) trop.Add(new NotesJeu.Lien { Url = "https://exemple.fr/p" + i, Titre = "Page " + i });
            NotesJeu.Pose(l, "Jeu aux mille liens", 0, "", trop);
            Banc.Egal("le nombre de liens est borné", NotesJeu.MaxLiens.ToString(), l[0].Liens.Count.ToString());

            Banc.Titre("Notes et liens du jeu — fichier modifié à la main");
            List<NotesJeu.Fiche> main = NotesJeu.Lire("jeua\t9\tnote A\thttps://exemple.fr/x Guide\tC:\\virus.exe Piège\r\njeua\t0\tdoublon\n\t0\tsans clé\njeub\t0\t\n\njeuc\t\t\thttps://exemple.fr/seul\njeud\tligne trop courte\n");
            Banc.Egal("les lignes vides, sans clé, en double ou sans contenu sont ignorées", "2", main.Count.ToString());
            Banc.Egal("un lien devenu invalide est ignoré, les autres restent", "1", main[0].Liens.Count.ToString());
            Banc.Egal("un lien sans titre dans le fichier prend le nom du site", "exemple.fr", main[1].Liens[0].Titre);
            Banc.Egal("une appréciation hors bornes dans le fichier est ignorée", "0", main[0].Etoiles.ToString());
            Banc.Egal("un fichier vide donne une liste vide", "0", NotesJeu.Lire("").Count.ToString());
            Banc.Egal("une liste nulle s'écrit en fichier vide", "", NotesJeu.Ecrire(null));
        }
    }
}
