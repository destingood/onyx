using System.Drawing;

namespace BTOptimizer.Tests
{
    internal static class DealArtworkTests
    {
        internal static void Tout()
        {
            Banc.Titre("Netteté des images de Bons plans");
            var frame = new Rectangle(8, 8, 440, 130);
            var small = DealArtwork.Fit(new Size(120, 80), frame);
            Banc.Verifie("une vignette reste à sa résolution native", true, small.Size == new Size(120, 80) && frame.Contains(small));
            var portrait = DealArtwork.Fit(new Size(600, 900), frame);
            Banc.Verifie("un portrait reste entier sans étirement", true, portrait.Height == 130 && portrait.Width == 87 && frame.Contains(portrait));
            var wide = DealArtwork.Fit(new Size(1920, 1080), frame);
            Banc.Verifie("un bandeau HD conserve son rapport largeur-hauteur", true, wide.Height == 130 && wide.Width == 231 && frame.Contains(wide));
            var tiny = DealArtwork.Fit(new Size(1, 1), frame);
            Banc.Verifie("même un pixel n'est pas agrandi", true, tiny.Width == 1 && tiny.Height == 1);
            Banc.Verifie("source absente : aucun dessin", true, DealArtwork.Fit(Size.Empty, frame).IsEmpty);
            // Fond flou : il recouvre tout le cadre quand la jaquette nette laisse des bandes vides.
            Banc.Verifie("un portrait laisse des bandes à combler", true, DealArtwork.LeavesBars(portrait, frame));
            Banc.Verifie("une vignette plus petite que le cadre aussi", true, DealArtwork.LeavesBars(small, frame));
            var plein = new Rectangle(8, 8, 320, 180);
            Banc.Verifie("un bandeau 16:9 remplit son cadre : pas de fond", false, DealArtwork.LeavesBars(DealArtwork.Fit(new Size(1280, 720), plein), plein));
            var fondPortrait = DealArtwork.Fill(new Size(24, 36), frame);
            Banc.Verifie("le fond d'un portrait recouvre tout le cadre", true, fondPortrait.Contains(frame) && fondPortrait.Width == 440 && fondPortrait.Height == 660);
            var fondLarge = DealArtwork.Fill(new Size(36, 8), frame);
            Banc.Verifie("le fond d'un bandeau très large recouvre tout le cadre", true, fondLarge.Contains(frame) && fondLarge.Height == 130 && fondLarge.Width == 585);
            Banc.Verifie("fond sans source : aucun dessin", true, DealArtwork.Fill(Size.Empty, frame).IsEmpty);
            Banc.Verifie("miniature du fond : portrait borné à 32 px", true, DealArtwork.BackdropSize(new Size(600, 900)) == new Size(21, 32));
            Banc.Verifie("miniature du fond : bandeau borné à 32 px", true, DealArtwork.BackdropSize(new Size(1280, 720)) == new Size(32, 18));
            Banc.Verifie("miniature du fond : jamais vide", true, DealArtwork.BackdropSize(new Size(4000, 10)) == new Size(32, 1));
            Banc.Verifie("cache HD : la largeur utile est conservée", true, DealArtwork.CacheSize(new Size(1280, 720)) == new Size(1280, 720));
            Banc.Verifie("cache HD : les grandes images restent bornées", true, DealArtwork.CacheSize(new Size(3840, 2160)) == new Size(1280, 720));
            Banc.Verifie("cache HD : un portrait garde ses proportions", true, DealArtwork.CacheSize(new Size(2000, 3000)) == new Size(853, 1280));
        }
    }
}
