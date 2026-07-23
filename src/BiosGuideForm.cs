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
            Text = "DesTinGOOD — BIOS & manips manuelles";
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
            opti.Click += (s, e) => { try { new MainForm().Show(); } catch { } };
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
        private void RebootToFirmware()
        {
            if (MessageBox.Show(this,
                    "Redémarrer MAINTENANT dans le BIOS/UEFI ?\r\n\r\nEnregistre ton travail : le PC va redémarrer "
                    + "immédiatement et ouvrir les réglages du firmware.",
                    "Redémarrer dans le BIOS", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                return;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("shutdown.exe", "/r /fw /t 2")
                { UseShellExecute = false, CreateNoWindow = true });
                if (_log != null) _log("Redémarrage dans le BIOS demandé.", 0);
            }
            catch (Exception ex) { MessageBox.Show(this, "Impossible : " + ex.Message, "DesTinGOOD", MessageBoxButtons.OK, MessageBoxIcon.Error); }
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
            Add(bios, "Mode XMP + fTPM (si micro-freezes AMD)",
                "Sur certaines cartes AMD, le fTPM cause des micro-saccades : mets-le sur « Discrete » ou désactive-le si tu n'en as pas besoin (Bitlocker off d'abord).");

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
