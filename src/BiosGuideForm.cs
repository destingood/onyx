using System;
using System.Drawing;
using System.Collections.Generic;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Guide « BIOS & manips manuelles » : les réglages CAPET qu'une app ne PEUT PAS écrire
    /// (SMT/Hyperthreading, XMP/EXPO — dans le firmware) + les manipulations de menus Windows.
    /// Honnête : ce sont des étapes à faire toi-même. Seule automatisation possible et offerte :
    /// redémarrer directement dans le BIOS (shutdown /r /fw).
    /// </summary>
    internal class BiosGuideForm : Form
    {
        private readonly Action<string, int> _log;
        private ListView _list;
        private List<Font> _ownedFonts;
        private Font _bold;

        private Font Own(Font f) { _ownedFonts.Add(f); return f; }

        public BiosGuideForm(Action<string, int> log)
        {
            _log = log;
            Build();
            Load += (s, e) => Reload();
        }

        private void Build()
        {
            Text = "ONYX — BIOS & manips manuelles";
            ClientSize = new Size(680, 560);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(580, 460);
            BackColor = Color.FromArgb(245, 246, 248);
            _ownedFonts = new List<Font>();
            Font = Own(new Font("Segoe UI", 9f));
            _bold = Own(new Font("Segoe UI Semibold", 9.5f));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  BIOS & manips manuelles", Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = Own(new Font("Segoe UI Semibold", 13f)), TextAlign = ContentAlignment.MiddleLeft
            });

            var intro = new Label
            {
                Dock = DockStyle.Top, Height = 40, Padding = new Padding(14, 7, 12, 2),
                ForeColor = Color.FromArgb(90, 95, 105),
                Text = "Les réglages CAPET qu'aucun logiciel ne peut appliquer (ils vivent dans le firmware ou sont des manips d'interface). À faire toi-même — bouton « Redémarrer dans le BIOS » pour y accéder direct."
            };

            _list = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, ShowGroups = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable, Font = Own(new Font("Segoe UI", 9.5f))
            };
            _list.Columns.Add("Réglage", 250);
            _list.Columns.Add("Quoi faire", 400);
            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 4, 12, 6) };
            host.Controls.Add(_list);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(12, 7, 12, 7) };
            var bios = MakeBtn("⏻ Redémarrer dans le BIOS…", 200, DockStyle.Left);
            bios.Click += (s, e) => RebootToFirmware();
            var opti = MakeBtn("Ouvrir l'optimiseur (manips auto)", 220, DockStyle.Left);
            // Cette fenêtre est ouverte en MODAL (OpenDialog → ShowDialog). Un Show() sans
            // propriétaire créait une fenêtre modeless par-dessus une boucle modale : elle
            // pouvait passer derrière sans qu'on puisse la rattraper, et son échec éventuel
            // était avalé. On l'ouvre modale et on la libère, comme partout ailleurs.
            opti.Click += (s, e) =>
            {
                try { using (var m = new MainForm()) m.ShowDialog(this); }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Impossible d'ouvrir l'optimiseur : " + ex.Message,
                        "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            var close = MakeBtn("Fermer", 90, DockStyle.Right);
            close.Click += (s, e) => Close();
            bottom.Controls.Add(new Label { Dock = DockStyle.Fill });
            bottom.Controls.Add(opti); bottom.Controls.Add(bios); bottom.Controls.Add(close);

            Controls.Add(banner);
            Controls.Add(intro);
            Controls.Add(host);
            Controls.Add(bottom);
            Controls.SetChildIndex(banner, 3);
            Controls.SetChildIndex(intro, 2);
            Controls.SetChildIndex(host, 0);
            Controls.SetChildIndex(bottom, 1);

            Theme.Apply(this);
        }

        private static Button MakeBtn(string text, int w, DockStyle dock)
        {
            var b = new Button { Text = text, Width = w, Dock = dock, FlatStyle = FlatStyle.Flat, BackColor = Color.White, Margin = new Padding(4, 0, 4, 0) };
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            return b;
        }

        // Redémarrage direct dans le firmware UEFI (l'utilisateur confirme — ça REDÉMARRE le PC).
        /// <summary>Vrai si Windows a démarré en UEFI. « /fw » n'existe que là : sur un firmware
        /// hérité, la commande échoue et le PC ne redémarre même pas.</summary>
        private static bool EstUefi()
        {
            try
            {
                string t = Environment.GetEnvironmentVariable("firmware_type");
                return !string.IsNullOrEmpty(t) && t.IndexOf("UEFI", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        private void RebootToFirmware()
        {
            // Prévenir AVANT de faire peur : inutile de proposer un redémarrage qui échouera.
            if (!EstUefi())
            {
                MessageBox.Show(this,
                    "Ce PC a démarré en mode BIOS hérité (pas UEFI).\r\n\r\n"
                    + "Windows ne sait pas y rediriger le démarrage vers le firmware : il faut appuyer sur la "
                    + "touche du constructeur au démarrage (souvent Suppr, F2 ou F12).",
                    "Redémarrer dans le BIOS", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (MessageBox.Show(this,
                    "Redémarrer MAINTENANT dans le BIOS/UEFI ?\r\n\r\nEnregistre ton travail : le PC va redémarrer "
                    + "immédiatement et ouvrir les réglages du firmware.",
                    "Redémarrer dans le BIOS", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                return;
            try
            {
                // ON LIT LE RÉSULTAT. L'ancien code lançait shutdown.exe sans fenêtre, sans jamais
                // regarder son code de retour, puis journalisait « Redémarrage dans le BIOS
                // demandé » quoi qu'il arrive. Or « /fw » est refusé par certains firmwares — et
                // dans ce cas shutdown ne redémarre PAS. L'utilisateur validait donc un
                // avertissement inquiétant, puis il ne se passait rien, sans la moindre
                // explication.
                //
                // En cas de succès, la machine redémarre 5 secondes plus tard : le message
                // d'échec ci-dessous ne peut apparaître que si l'échec est réel.
                NativeResult r = Sys.Run(Sys.Sys32("shutdown.exe"), "/r /fw /t 5");
                if (r.ExitCode == 0)
                {
                    if (_log != null) _log("Redémarrage dans le BIOS accepté : le PC redémarre dans 5 secondes.", 1);
                    return;
                }
                string detail = (r.Output ?? "").Trim();
                if (_log != null) _log("Redémarrage dans le BIOS REFUSÉ (code " + r.ExitCode + ") : " + detail, 3);
                MessageBox.Show(this,
                    "Windows a refusé de rediriger le démarrage vers le firmware (code " + r.ExitCode + ").\r\n\r\n"
                    + (detail.Length > 0 ? detail + "\r\n\r\n" : "")
                    + "Le PC n'a PAS redémarré. Redémarre-le toi-même en appuyant sur la touche du constructeur "
                    + "(souvent Suppr, F2 ou F12).",
                    "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex) { MessageBox.Show(this, "Impossible : " + ex.Message, "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void Add(ListViewGroup g, string name, string what)
        {
            var it = new ListViewItem("• " + name) { Group = g, UseItemStyleForSubItems = false, Font = _bold, ForeColor = Color.FromArgb(50, 55, 65) };
            it.SubItems.Add(what).ForeColor = Color.FromArgb(90, 95, 105);
            _list.Items.Add(it);
        }

        private void Reload()
        {
            _list.BeginUpdate();
            _list.Items.Clear();
            _list.Groups.Clear();

            var bios = new ListViewGroup("BIOS / UEFI — bouton « Redémarrer dans le BIOS » ci-dessous") { HeaderAlignment = HorizontalAlignment.Left };
            var manip = new ListViewGroup("Manips Windows (TUTO 1) — la plupart sont automatisées dans l'optimiseur") { HeaderAlignment = HorizontalAlignment.Left };
            _list.Groups.Add(bios);
            _list.Groups.Add(manip);

            Add(bios, "Profil mémoire XMP / EXPO",
                "Active le profil XMP (Intel) ou EXPO (AMD) : ta RAM tourne à sa VRAIE vitesse (souvent 2133 MHz sinon). Gros gain FPS. Vérifie avec CPU-Z (onglet Mémoire).");
            Add(bios, "SMT / Hyperthreading",
                "CAPET conseille de le LAISSER sur AUTO/activé : le désactiver manuellement cause plus de bugs que de gains sur les CPU récents.");
            Add(bios, "Resizable BAR / Smart Access Memory",
                "Active « Resizable BAR » (Intel) ou « SAM » (AMD) si ton GPU le supporte : quelques % de FPS en plus, gratuit.");
            Add(bios, "Above 4G Decoding",
                "Souvent requis pour activer le Resizable BAR — active-le d'abord si l'option ReBAR est grisée.");
            Add(bios, "fTPM (si micro-saccades AMD)",
                "Bug connu des cartes AMD, CORRIGÉ par une mise à jour BIOS (AGESA 1.2.0.7 et suivantes) : commence par "
                + "mettre à jour le BIOS, c'est la vraie réparation. Désactiver le fTPM n'est qu'un contournement — et il "
                + "rend Windows 11 non supporté. Si tu y touches quand même : DÉSACTIVE BitLocker AVANT, sinon ton disque "
                + "devient illisible sans la clé de récupération.");

            Add(manip, "Menu clic droit classique, barre des tâches, widgets, chat…",
                "Déjà AUTOMATISÉ : bouton « Ouvrir l'optimiseur » → catégorie 🚀 Rapidité (menu classique, widgets off, chat off, recherche…).");
            Add(manip, "Vérifier que Windows est activé",
                "Paramètres → Système → Activation. Un Windows non activé bride certaines options d'affichage.");
            Add(manip, "Épingler / réorganiser le menu Démarrer",
                "Purement manuel (glisser-déposer tes apps). CAPET le montre en vidéo ; rien à automatiser côté perf.");

            _list.EndUpdate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _ownedFonts != null)
            {
                foreach (Font f in _ownedFonts) { try { f.Dispose(); } catch { } }
                _ownedFonts.Clear();
            }
            base.Dispose(disposing);
        }
    }
}
