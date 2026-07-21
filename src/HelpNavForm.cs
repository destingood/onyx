using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// « J'ai un problème… » : l'assistant qui part du SYMPTÔME et ouvre directement le bon
    /// outil parmi tous les panneaux de l'app. La porte d'entrée pour s'y retrouver sans être
    /// expert. Double-clic (ou bouton) = ouverture du panneau qui traite le problème.
    /// </summary>
    internal class HelpNavForm : Form
    {
        private readonly Action<string, int> _log;
        private ListView _list;
        private Button _btnOpen, _btnClose;

        private static readonly Color Accent = Color.FromArgb(0, 150, 90);

        private class Entry { public string Symptom, Tool; public Func<Form> Open; }
        private readonly List<Entry> _entries = new List<Entry>();

        public HelpNavForm(Action<string, int> log)
        {
            _log = log;
            Build();
            Fill();
            Theme.Apply(this);
        }

        private void Build()
        {
            Text = "DesTinGOOD — J'ai un problème…";
            ClientSize = new Size(700, 560);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Color.FromArgb(245, 246, 248);
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  J'ai un problème… — quel outil pour quoi ?",
                Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 12.5f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            var intro = new Label
            {
                Text = "Choisis ce qui te ressemble : double-clique une ligne (ou « Ouvrir l'outil ») pour aller "
                     + "directement au panneau qui traite ce problème. En cas de doute, commence par le bilan « Santé ».",
                Location = new Point(18, 58), Size = new Size(664, 40), ForeColor = Color.FromArgb(60, 64, 72)
            };
            Controls.Add(intro);

            _list = new ListView
            {
                Location = new Point(18, 104), Size = new Size(664, 388),
                View = View.Details, FullRowSelect = true, GridLines = false, MultiSelect = false
            };
            _list.Columns.Add("Ton problème", 430);
            _list.Columns.Add("Outil qui le traite", 234);
            _list.DoubleClick += (s, e) => OpenSelected();
            Controls.Add(_list);

            _btnOpen = MakeBtn("Ouvrir l'outil", 18, 504, 200, 40, true);
            _btnOpen.Click += (s, e) => OpenSelected();
            _btnClose = MakeBtn("Fermer", 592, 504, 90, 40, false);
            _btnClose.Click += (s, e) => Close();
            Controls.Add(_btnOpen); Controls.Add(_btnClose);
        }

        private static Button MakeBtn(string text, int x, int y, int w, int h, bool primary)
        {
            var b = new Button
            {
                Text = text, Location = new Point(x, y), Size = new Size(w, h), FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Color.FromArgb(40, 44, 52),
                Font = primary ? new Font("Segoe UI Semibold", 10f) : new Font("Segoe UI", 9f)
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            return b;
        }

        private void Add(string group, string symptom, string tool, Func<Form> open)
        {
            _entries.Add(new Entry { Symptom = symptom, Tool = tool, Open = open });
            var g = FindGroup(group);
            var it = new ListViewItem(symptom, g) { Tag = _entries.Count - 1 };
            it.SubItems.Add(tool);
            _list.Items.Add(it);
        }

        private ListViewGroup FindGroup(string header)
        {
            foreach (ListViewGroup g in _list.Groups) if (g.Header == header) return g;
            var ng = new ListViewGroup(header); _list.Groups.Add(ng); return ng;
        }

        private void Fill()
        {
            Action<string, int> L = _log;

            Add("Crashs & instabilité", "Bilan complet : je ne sais pas par où commencer", "Santé de mon PC", () => new HealthForm(L));
            Add("Crashs & instabilité", "Mes jeux crashent ou se ferment tout seuls", "Stabilité (14 j)", () => new StabilityForm(L));
            Add("Crashs & instabilité", "« Votre dispositif de rendu a été perdu » / freeze", "Boutiques / crashs", () => new ShopFixForm(L));
            Add("Crashs & instabilité", "Le PC ou le GPU chauffe / bride (throttling)", "Températures & throttling", () => new ThermalForm(L));
            Add("Crashs & instabilité", "Un ancien « optimiseur » a peut-être cassé des réglages", "Réglages néfastes", () => new CheckupForm(L));
            Add("Crashs & instabilité", "Crashs qui persistent malgré tout", "Réparer Windows (via menu ☰)", () => new HealthForm(L));

            Add("Performance & fluidité", "Ça rame / ça saccade en jeu", "Qui ralentit mon PC", () => new BloatForm(L));
            Add("Performance & fluidité", "Mes FPS sont bas", "FPS en direct", () => new FpsMonForm(L));
            Add("Performance & fluidité", "Je veux mesurer la puissance de mon PC", "Benchmark rapide", () => new BenchForm(L));
            Add("Performance & fluidité", "Chargements longs / mes jeux sont sur le bon disque ?", "Jeux & disques", () => new DiskForm(L));
            Add("Performance & fluidité", "Input lag / réactivité de la souris", "⏱ Latence en direct", () => new LiveMonForm(L));

            Add("Réseau & ping", "Ça lag en ligne (ping, gigue, décrochages)", "Qualité réseau", () => new NetworkForm(L));
            Add("Réseau & ping", "Je veux voir OÙ le lag apparaît sur le trajet", "Trajet réseau", () => new NetRouteForm(L));
            Add("Réseau & ping", "Téléchargements lents / Steam charge à l'infini", "⚙️ Réglages TCP/IP", () => new NetTuneForm(L));
            Add("Réseau & ping", "Réduire la latence de ma carte réseau", "Carte réseau", () => new NetAdapterForm(L));
            Add("Réseau & ping", "Changer/tester mon DNS", "DNS rapide", () => new DnsForm(L));

            Add("Jeux & lancement", "Un jeu refuse de démarrer (dll manquante)", "Bibliothèques de jeu", () => new LibsForm(L));
            Add("Jeux & lancement", "Boutique en jeu vide / qui tourne à l'infini", "Boutiques / crashs", () => new ShopFixForm(L));
            Add("Jeux & lancement", "Booster mon jeu principal (priorité CPU)", "Priorité par jeu", () => new GameProfileForm(L));
            Add("Jeux & lancement", "Moins de saccades (analyse antivirus des jeux)", "Exclusions antivirus", () => new DefenderForm(L));

            Add("Écran & périphériques", "Mon écran semble bloqué à 60 Hz", "Réglages d'écran", () => new DisplayForm(L));
            Add("Écran & périphériques", "Ma souris est-elle vraiment à 1000 Hz ?", "Fréquence de la souris", () => new MouseForm(L));

            Add("Entretien & sécurité", "PC lent à démarrer (trop de programmes au boot)", "Programmes au démarrage", () => new StartupForm(L));
            Add("Entretien & sécurité", "Libérer de l'espace disque", "Nettoyage disque", () => new CleanupForm(L));
            Add("Entretien & sécurité", "Faire une sauvegarde avant de bidouiller", "Points de restauration", () => new RestoreForm(L));
        }

        private void OpenSelected()
        {
            if (_list.SelectedItems.Count != 1) return;
            int idx = (int)_list.SelectedItems[0].Tag;
            if (idx < 0 || idx >= _entries.Count) return;
            try { using (Form f = _entries[idx].Open()) f.ShowDialog(this); }
            catch (Exception ex) { if (_log != null) _log("Ouverture : " + ex.Message, 2); }
        }
    }
}
