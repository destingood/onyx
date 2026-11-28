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

            Banc.Titre("Mise à jour — fichier de la release à télécharger");
            Banc.Egal("installateur complet", "3", MajLogique.RangInstallateur("ONYX-Setup-16.14.0.0.exe").ToString());
            Banc.Egal("installateur léger : simple programme", "1", MajLogique.RangInstallateur("ONYX-Installation.exe").ToString());
            Banc.Egal("autre installateur", "2", MajLogique.RangInstallateur("Setup.EXE").ToString());
            Banc.Egal("archive : pas un programme", "0", MajLogique.RangInstallateur("ONYX-16.14.zip").ToString());
            Banc.Egal("manifeste : pas un programme", "0", MajLogique.RangInstallateur("onyx-derniere.ini").ToString());
            Banc.Egal("nom vide", "00", MajLogique.RangInstallateur(null).ToString() + MajLogique.RangInstallateur(""));
            Banc.Verifie("le complet passe devant le léger, quel que soit l'ordre", true,
                MajLogique.RangInstallateur("ONYX-Setup-16.14.0.0.exe") > MajLogique.RangInstallateur("ONYX-Installation.exe"));
            string upd = CatalogueTests.Source("Updater.cs");
            Banc.Verifie("Updater choisit le fichier par son rang", true, upd.Contains("MajLogique.RangInstallateur(name)"));
            Banc.Verifie("Updater télécharge dans le dossier que le ménage balaie", true,
                upd.Contains("MajLogique.DossierMaj") && CatalogueTests.Source("OldVersions.cs").Contains("MajLogique.DossierMaj"));
            Banc.Verifie("Updater contrôle la place avant de télécharger", true,
                upd.IndexOf("MajLogique.EspaceSuffisant", StringComparison.Ordinal) > 0
                && upd.IndexOf("MajLogique.EspaceSuffisant", StringComparison.Ordinal) < upd.IndexOf("part = dest + \".part\"", StringComparison.Ordinal));
            Banc.Verifie("installateur tenu sans partage en écriture jusqu'au lancement", true,
                upd.Contains("FileAccess.Read, FileShare.Read") && upd.Contains("avantLancement()"));

            Banc.Titre("Mise à jour — ignorer une version, mise à jour importante");
            Banc.Verifie("même version, écritures différentes", true,
                MajLogique.MemeVersion("16.14", "v16.14.0.0") && MajLogique.MemeVersion("ONYX 16.05", "16.5") && MajLogique.MemeVersion(" 16.14\r\n", "16.14"));
            Banc.Verifie("versions différentes", false, MajLogique.MemeVersion("16.14", "16.15") || MajLogique.MemeVersion("16.14", "17.14"));
            Banc.Verifie("version illisible : jamais égale", false,
                MajLogique.MemeVersion(null, null) || MajLogique.MemeVersion("", "") || MajLogique.MemeVersion("abc", "abc"));
            Banc.Verifie("version ignorée : celle-là seulement", true,
                MajLogique.EstIgnoree("16.14", "16.14\r\n") && !MajLogique.EstIgnoree("16.15", "16.14") && !MajLogique.EstIgnoree("16.14", null));
            Banc.Verifie("marque de mise à jour importante", true,
                MajLogique.EstObligatoire("## Correctif\n- Faille corrigée\n<!-- onyx:obligatoire -->") && MajLogique.EstObligatoire("<!--ONYX:Obligatoire-->"));
            Banc.Verifie("notes ordinaires : pas importante", false,
                MajLogique.EstObligatoire("## Nouveautés\n- Mise à jour obligatoire des pilotes") || MajLogique.EstObligatoire(null) || MajLogique.EstObligatoire(""));
            string lisibles = MajLogique.NotesLisibles("## Correctif\n- Faille corrigée\n<!-- onyx:obligatoire -->\n<!-- sur\ndeux lignes -->\n- Suite", 2000);
            Banc.Verifie("les commentaires des notes ne s'affichent pas", true,
                !lisibles.Contains("onyx") && !lisibles.Contains("<!--") && !lisibles.Contains("deux lignes") && lisibles.Contains("Faille corrigée") && lisibles.Contains("Suite"));
            string fen = CatalogueTests.Source("MajForm.cs");
            Banc.Verifie("fenêtre : « Ignorer » absent d'une mise à jour importante", true,
                fen.Contains("_tertiaire.Visible = !rel.Obligatoire;") && fen.Contains("rel.Obligatoire) return;"));
            Banc.Verifie("version ignorée : plus de rappel", true, CatalogueTests.Source("UpdateFlag.cs").Contains("if (EstIgnoree(v)) return null;"));

            Banc.Titre("Mise à jour — espace disque");
            long mo = 1048576;
            Banc.Egal("place à avoir pour 58 Mo", "374", (MajLogique.EspaceRequis(58 * mo) / mo).ToString());
            Banc.Egal("taille inconnue : la marge seule", "200", (MajLogique.EspaceRequis(0) / mo).ToString() + (MajLogique.EspaceRequis(-5) == MajLogique.EspaceRequis(0) ? "" : "!"));
            Banc.Verifie("assez de place", true, MajLogique.EspaceSuffisant(374 * mo, 58 * mo) && MajLogique.EspaceSuffisant(5000 * mo, 58 * mo));
            Banc.Verifie("pas assez de place", false, MajLogique.EspaceSuffisant(374 * mo - 1, 58 * mo) || MajLogique.EspaceSuffisant(0, 58 * mo));
            Banc.Verifie("espace libre inconnu : on ne bloque pas", true, MajLogique.EspaceSuffisant(-1, 58 * mo));
            Banc.Egal("message de disque plein",
                "Pas assez de place sur le disque : il faut 374 Mo libres, il en reste 120. Rien n'a été téléchargé.",
                MajLogique.TexteEspace(120 * mo, 58 * mo));

            Banc.Titre("Mise à jour — installation automatique");
            Banc.Verifie("option cochée, GitHub, empreinte publiée", true, MajLogique.AutoPossible(true, true, "ab12"));
            Banc.Verifie("option coupée, autre hôte ou empreinte absente : jamais", false,
                MajLogique.AutoPossible(false, true, "ab12") || MajLogique.AutoPossible(true, false, "ab12")
                || MajLogique.AutoPossible(true, true, null) || MajLogique.AutoPossible(true, true, ""));
            Banc.Verifie("ONYX réduit, pas de partie, pas de fenêtre de mise à jour", true, MajLogique.MomentPropice(false, false, false));
            Banc.Verifie("fenêtre ouverte, partie en cours ou fenêtre de mise à jour : on attend", false,
                MajLogique.MomentPropice(true, false, false) || MajLogique.MomentPropice(false, true, false) || MajLogique.MomentPropice(false, false, true));
            Banc.Verifie("installation automatique : aucune fenêtre, relance demandée", true,
                MajLogique.ArgsDiscrets.Contains("/VERYSILENT") && MajLogique.ArgsDiscrets.Contains("/SUPPRESSMSGBOXES")
                && MajLogique.ArgsDiscrets.Contains("/CLOSEAPPLICATIONS") && MajLogique.ArgsDiscrets.Contains("/RELANCE=1"));
            var t0 = new DateTime(2026, 11, 28, 12, 0, 0);
            string marque = MajLogique.MarqueRetour("16.14", t0);
            Banc.Egal("marque de retour", "16.14|2026-11-28 12:00", marque);
            Banc.Egal("retour après installation : réduit", "16.14", MajLogique.RetourDiscret(marque, t0.AddMinutes(2), "16.14") ?? "null");
            Banc.Egal("version qui tourne différente : installation non faite", "null", MajLogique.RetourDiscret(marque, t0.AddMinutes(2), "16.13") ?? "null");
            Banc.Egal("marque trop vieille", "null", MajLogique.RetourDiscret(marque, t0.AddMinutes(16), "16.14") ?? "null");
            Banc.Egal("marque du futur (horloge reculée)", "null", MajLogique.RetourDiscret(marque, t0.AddMinutes(-5), "16.14") ?? "null");
            Banc.Egal("marque absente ou illisible", "nullnullnull",
                (MajLogique.RetourDiscret(null, t0, "16.14") ?? "null") + (MajLogique.RetourDiscret("16.14", t0, "16.14") ?? "null")
                + (MajLogique.RetourDiscret("16.14|hier", t0, "16.14") ?? "null"));
            string tdb = CatalogueTests.Source("DashboardForm.cs");
            Banc.Verifie("tableau de bord : installation seulement au moment propice", true,
                tdb.Contains("MajLogique.MomentPropice(Visible, partie, majOuverte)") && tdb.Contains("MajLogique.ArgsDiscrets"));
            Banc.Verifie("tableau de bord : rien pendant une partie, pas même le téléchargement", true,
                tdb.IndexOf("if (PartieEnCours()) return;", StringComparison.Ordinal) > 0
                && tdb.IndexOf("if (PartieEnCours()) return;", StringComparison.Ordinal) < tdb.IndexOf("MajAuto.Telecharge(out err)", StringComparison.Ordinal));
            Banc.Verifie("installation automatique coupée par défaut", true,
                upd.Contains("File.Exists(AutoPath) && File.ReadAllText(AutoPath).Trim() == \"1\""));
        }
    }
}
