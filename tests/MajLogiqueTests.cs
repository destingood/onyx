using System;
using System.IO;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Mise à jour : fréquence de vérification, empreinte SHA256 de GitHub, notes lisibles,
    /// notification une seule fois par version, texte de progression.
    /// </summary>
    internal static class MajLogiqueTests
    {
        public static void Tout()
        {
            Banc.Titre("Mise à jour : logique");

            var t = new DateTime(2026, 11, 24, 12, 0, 0);
            Banc.Verifie("aucun tampon : due", true, MajLogique.EstDue(null, t) && MajLogique.EstDue("", t));
            Banc.Verifie("tampon illisible : due", true, MajLogique.EstDue("n'importe quoi", t));
            Banc.Verifie("vérifiée il y a 2 h : pas due", false, MajLogique.EstDue(MajLogique.Horodatage(t.AddHours(-2)), t));
            Banc.Verifie("vérifiée il y a 6 h : due", true, MajLogique.EstDue(MajLogique.Horodatage(t.AddHours(-6)), t));
            Banc.Verifie("horloge reculée (tampon dans le futur) : due", true, MajLogique.EstDue(MajLogique.Horodatage(t.AddHours(3)), t));
            Banc.Verifie("ancien format du jour, 12 h plus tard : due", true, MajLogique.EstDue("20261124", t));
            Banc.Verifie("ancien format du jour, 3 h du matin : pas due", false, MajLogique.EstDue("20261124", new DateTime(2026, 11, 24, 3, 0, 0)));

            const string H = "79c28430fb0fd3f3a3469d22f30d836fec3ee4bbec2a3abbc1258de4222af6a9";
            Banc.Verifie("digest GitHub lu", true, MajLogique.Sha256DuDigest("sha256:" + H) == H);
            Banc.Verifie("empreinte brute en majuscules acceptée", true, MajLogique.Sha256DuDigest(H.ToUpperInvariant()) == H);
            Banc.Verifie("digest absent, autre algorithme ou tronqué : null", true,
                MajLogique.Sha256DuDigest(null) == null && MajLogique.Sha256DuDigest("md5:abc") == null
                && MajLogique.Sha256DuDigest("sha256:" + H.Substring(1)) == null && MajLogique.Sha256DuDigest("sha256:" + H.Replace('a', 'z')) == null);

            string f = Path.Combine(Path.GetTempPath(), "onyx-test-empreinte.bin");
            File.WriteAllText(f, "abc");
            const string Abc = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
            Banc.Verifie("empreinte conforme", true, MajLogique.EmpreinteOk(f, Abc) && MajLogique.EmpreinteOk(f, Abc.ToUpperInvariant()));
            Banc.Verifie("empreinte différente refusée", false, MajLogique.EmpreinteOk(f, H));
            Banc.Verifie("sans empreinte annoncée : rien à contrôler", true, MajLogique.EmpreinteOk(f, null));
            Banc.Verifie("fichier absent refusé", false, MajLogique.EmpreinteOk(f + ".absent", Abc));
            try { File.Delete(f); } catch { }

            string md = "## Rapidité et fluidité\n\n- **Page Optimisations instantanée** : ouverture 2,1 s.\n"
                      + "* Voir [le journal](https://github.com/x) et `code`.\n\n\n\n### Installation\nTélécharge **ONYX-Setup.exe**.\n\nValidation : 1 282 tests.";
            string n = MajLogique.NotesLisibles(md, 2000);
            Banc.Verifie("titre sans dièses", true, n.StartsWith("Rapidité et fluidité\n"));
            Banc.Verifie("puces et gras convertis", true, n.Contains("• Page Optimisations instantanée : ouverture 2,1 s."));
            Banc.Verifie("lien et code réduits au texte", true, n.Contains("• Voir le journal et code."));
            Banc.Verifie("section Installation retirée", true, !n.Contains("Installation") && !n.Contains("Validation") && !n.Contains("Setup"));
            Banc.Verifie("pas de lignes vides en série", true, !n.Contains("\n\n\n") && !n.EndsWith("\n"));
            string court = MajLogique.NotesLisibles(md, 30);
            Banc.Verifie("coupé à la ligne, avec points de suspension", true, court.Length <= 31 && court.EndsWith("…") && court.StartsWith("Rapidité et fluidité"));
            Banc.Verifie("notes absentes", true, MajLogique.NotesLisibles(null, 100) == "" && MajLogique.NotesLisibles("\r\n", 100) == "");

            Banc.Verifie("nouvelle version : notifier", true, MajLogique.ANotifier("15.98", null) && MajLogique.ANotifier("15.98", "15.97"));
            Banc.Verifie("même version déjà notifiée : se taire", false, MajLogique.ANotifier("15.98", " 15.98\r\n"));
            Banc.Verifie("aucune version : se taire", false, MajLogique.ANotifier(null, null) || MajLogique.ANotifier(" ", null));

            var vs = new[] { new Version(16, 11), new Version(16, 12), new Version(16, 13), null, new Version(16, 12) };
            var pre = new[] { false, true, false, false, false };
            var bro = new[] { false, false, true, false, false };
            Banc.Egal("bêta coupé : la finale la plus récente, jamais un brouillon", "4", MajLogique.ChoisitRelease(vs, pre, bro, false).ToString());
            Banc.Egal("bêta : à version égale, la finale passe devant", "4", MajLogique.ChoisitRelease(vs, pre, bro, true).ToString());
            Banc.Egal("bêta : une préversion plus récente est proposée", "1",
                MajLogique.ChoisitRelease(new[] { new Version(16, 11), new Version(16, 12) }, new[] { false, true }, null, true).ToString());
            Banc.Egal("rien de publiable", "-1", MajLogique.ChoisitRelease(new[] { new Version(16, 12) }, new[] { true }, null, false).ToString());
            Banc.Egal("liste vide", "-1", MajLogique.ChoisitRelease(null, null, null, true).ToString());

            Banc.Verifie("progression lisible", true, MajLogique.Progression(12900000, 57942239) == "12,3 / 55,3 Mo — 22 %");
            Banc.Verifie("progression sans taille connue", true, MajLogique.Progression(5 * 1048576, 0) == "5,0 Mo");
            Banc.Verifie("installation silencieuse avec relance demandée", true,
                MajLogique.ArgsSilencieux.Contains("/SILENT") && MajLogique.ArgsSilencieux.Contains("/SUPPRESSMSGBOXES")
                && MajLogique.ArgsSilencieux.Contains("/CLOSEAPPLICATIONS") && MajLogique.ArgsSilencieux.Contains("/RELANCE=1"));
        }
    }
}
