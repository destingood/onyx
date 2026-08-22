using System;
using System.IO;
using System.Text.RegularExpressions;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// LE GARDE-FOU QUI AURAIT ÉVITÉ D'INVALIDER TOUT LE PARC.
    ///
    /// La clé publique embarquée dans <c>src/License.cs</c> et la clé privée du vendeur
    /// (<c>seller/private.xml</c>) sont les deux moitiés d'UNE paire. Si elles cessent de
    /// correspondre, l'application ne peut plus vérifier AUCUNE clé : ni celles déjà vendues, ni
    /// celles que le générateur produira ensuite. Le produit se retrouve invendable, et la panne
    /// est invisible depuis le code — tout compile, tout se lance, et seul un client s'en aperçoit.
    ///
    /// Ce n'est pas une hypothèse. Le commit ab25e7c (« Centre de stockage », v15.34) a remplacé la
    /// constante par le modulus d'une paire dont la moitié privée n'existe nulle part dans le
    /// dépôt. Son message ne parle pas de licences : la rotation est passée avec le reste. Les sept
    /// licences de seller/licences-emises.csv sont devenues invalides d'un coup, et le générateur
    /// n'aurait pas pu en émettre une seule qui marche.
    ///
    /// Le test ne fait AUCUNE cryptographie et n'ouvre aucune classe de l'application : il compare
    /// deux chaînes de texte dans deux fichiers du dépôt. C'est justement ce qui le rend sûr —
    /// aucun effet de bord, aucune clé manipulée, et il tombe à la seconde où les deux moitiés
    /// divergent.
    ///
    /// SI seller/private.xml EST ABSENT (poste sans les secrets de vente, intégration continue), le
    /// test ne peut pas conclure et le DIT, au lieu de passer au vert et de faire croire à une
    /// vérification qui n'a pas eu lieu.
    /// </summary>
    internal static class LicenceCleTests
    {
        public static void Tout()
        {
            Banc.Titre("Les deux moitiés de la clé de licence");

            string racine = Racine();
            if (racine == null)
            {
                Banc.Verifie("dépôt introuvable depuis le banc — vérification impossible, et c'est dit",
                    true, true);
                return;
            }

            string publique = Modulus(Path.Combine(racine, "src", "License.cs"));
            Banc.Verifie("la clé publique est bien présente dans src/License.cs", true,
                publique != null && publique.Length > 300);

            string prive = Path.Combine(racine, "seller", "private.xml");
            if (!File.Exists(prive))
            {
                // Pas de secret de vente sur ce poste : on ne peut pas comparer. On refuse de
                // faire passer ça pour une vérification réussie.
                Console.WriteLine("  [--]  seller/private.xml absent : correspondance NON vérifiée sur ce poste");
                return;
            }

            string secret = Modulus(prive);
            Banc.Verifie("la clé privée du vendeur est lisible", true,
                secret != null && secret.Length > 300);

            // LE test. Deux moitiés d'une même paire ont le MÊME modulus.
            Banc.Egal("la clé publique embarquée correspond à la clé privée du vendeur", secret, publique);
        }

        /// <summary>Le modulus RSA d'un fichier, quel que soit son format — la constante C# comme
        /// le XML de la clé privée le portent entre les mêmes balises.</summary>
        private static string Modulus(string fichier)
        {
            try
            {
                if (!File.Exists(fichier)) return null;
                Match m = Regex.Match(File.ReadAllText(fichier), "<Modulus>([^<]+)</Modulus>");
                return m.Success ? m.Groups[1].Value.Trim() : null;
            }
            catch { return null; }
        }

        /// <summary>Remonte depuis le dossier d'exécution jusqu'à la racine du dépôt. Le banc
        /// tourne dans tests/bin/… : le nombre de niveaux dépend de la configuration, donc on
        /// cherche un repère plutôt que de compter.</summary>
        private static string Racine()
        {
            try
            {
                var d = new DirectoryInfo(AppContext.BaseDirectory);
                for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                    if (File.Exists(Path.Combine(d.FullName, "BTOptimizer.csproj"))) return d.FullName;
            }
            catch { }
            return null;
        }
    }
}
