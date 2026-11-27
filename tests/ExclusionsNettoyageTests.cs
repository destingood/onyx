using System.Collections.Generic;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Vérifie <see cref="ExclusionsNettoyage"/> — chemins acceptés, ce qu'une exclusion couvre, et
    /// la protection d'un dossier qui contient une exclusion.
    /// </summary>
    internal static class ExclusionsNettoyageTests
    {
        public static void Tout()
        {
            Banc.Titre("Exclusions du nettoyage");

            Banc.Egal("chemin simple", @"D:\Montages", ExclusionsNettoyage.Normalise(@"D:\Montages"));
            Banc.Egal("guillemets, espaces, barres de fin et barres obliques nettoyés", @"D:\Mes clips\2026", ExclusionsNettoyage.Normalise("  \"d:/Mes clips/2026\\\\\"  ".Replace("\\\\\"", "\\\"")));
            Banc.Egal("racine d'un disque gardée telle quelle", @"E:\", ExclusionsNettoyage.Normalise(@"e:\"));
            Banc.Egal("partage réseau", @"\\nas\videos\jeux", ExclusionsNettoyage.Normalise(@"\\nas\videos\jeux\"));
            foreach (string faux in new[] { null, "", "Montages", @"..\Montages", @"D:Montages", @"D:\a\..\b", @"D:\a\.\b", @"D:\a*b", @"D:\a?b", "D:\\a\tb", @"D:\a:b", @"\\nas", @"\\\nas\x", @"D:\a\\b", @"1:\x" })
                if (ExclusionsNettoyage.Normalise(faux) != null) Banc.Verifie("chemin refusé attendu : " + (faux ?? "null"), true, false);
            Banc.Verifie("chemins relatifs, à jokers, à « .. » ou incomplets : refusés", true, ExclusionsNettoyage.Normalise("Montages") == null);

            var l = new List<string> { @"D:\Montages", @"C:\Users\Lucas\AppData\Local\Temp\garde" };
            Banc.Verifie("le dossier exclu lui-même", true, ExclusionsNettoyage.Couvre(l, @"D:\Montages"));
            Banc.Verifie("un fichier dedans, casse ignorée", true, ExclusionsNettoyage.Couvre(l, @"d:\montages\Rush\clip.mp4"));
            Banc.Verifie("un dossier voisin au nom plus long : pas couvert", false, ExclusionsNettoyage.Couvre(l, @"D:\Montages2\clip.mp4"));
            Banc.Verifie("le dossier parent : pas couvert", false, ExclusionsNettoyage.Couvre(l, @"D:\"));
            Banc.Verifie("identifiant qui n'est pas un chemin : pas couvert", false, ExclusionsNettoyage.Couvre(l, "hiberfil"));
            Banc.Verifie("liste vide ou chemin vide : pas couvert", false, ExclusionsNettoyage.Couvre(null, @"D:\x") || ExclusionsNettoyage.Couvre(l, ""));

            Banc.Verifie("un cache qui CONTIENT un dossier exclu est protégé", true, ExclusionsNettoyage.Touche(l, @"C:\Users\Lucas\AppData\Local\Temp"));
            Banc.Verifie("…y compris écrit avec une barre de fin", true, ExclusionsNettoyage.Touche(l, @"C:\Users\Lucas\AppData\Local\Temp\"));
            Banc.Verifie("un cache sans rapport n'est pas protégé", false, ExclusionsNettoyage.Touche(l, @"C:\Windows\Temp"));
            Banc.Verifie("un fichier dans un dossier exclu est protégé", true, ExclusionsNettoyage.Touche(l, @"D:\Montages\a.log"));
            Banc.Verifie("identifiant qui n'est pas un chemin : pas protégé", false, ExclusionsNettoyage.Touche(l, "winsxs") || ExclusionsNettoyage.Touche(l, "recyclebin"));
            Banc.Verifie("racine du disque exclue : tout le disque est couvert", true, ExclusionsNettoyage.Couvre(new[] { @"E:\" }, @"E:\Jeux\x.log"));

            var e = new List<string>();
            Banc.Verifie("ajout accepté", true, ExclusionsNettoyage.Ajoute(e, @"D:\Montages\2026") == null && e.Count == 1);
            Banc.Verifie("doublon refusé avec la raison", true, ExclusionsNettoyage.Ajoute(e, @"d:\montages\2026\") != null && e.Count == 1);
            Banc.Verifie("sous-dossier d'une exclusion : refusé (déjà couvert)", true, ExclusionsNettoyage.Ajoute(e, @"D:\Montages\2026\rush") != null && e.Count == 1);
            Banc.Verifie("chemin incorrect refusé", true, ExclusionsNettoyage.Ajoute(e, "Montages") != null && e.Count == 1);
            Banc.Verifie("dossier parent ajouté : il remplace celui qu'il englobe", true,
                ExclusionsNettoyage.Ajoute(e, @"D:\Montages") == null && e.Count == 1 && e[0] == @"D:\Montages");
            for (int i = e.Count; i < ExclusionsNettoyage.Max; i++) ExclusionsNettoyage.Ajoute(e, @"F:\d" + i);
            Banc.Verifie("liste pleine : refus, rien n'est ajouté", true,
                e.Count == ExclusionsNettoyage.Max && ExclusionsNettoyage.Ajoute(e, @"G:\encore") != null && e.Count == ExclusionsNettoyage.Max);
        }
    }
}
