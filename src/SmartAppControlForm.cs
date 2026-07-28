using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Panneau « Smart App Control » : explique POURQUOI Windows refuse de lancer une application
    /// (non signée / sans réputation), montre l'état réel, et permet de le changer en connaissance
    /// de cause — sauvegarde .reg avant, confirmation explicite, et l'avertissement que Microsoft
    /// ne permet pas de le rallumer après coup.
    /// </summary>
    internal class SmartAppControlForm : Form
    {
        private readonly Action<string, int> _log;
        private Label _state, _explain, _warn;
        private Button _btnEval, _btnOff, _btnOn;

        public SmartAppControlForm(Action<string, int> log)
        {
            _log = log ?? delegate { };
            Text = "ONYX — Smart App Control";
            ClientSize = new Size(620, 520);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Color.White;
            Font = new Font("Segoe UI", 9.5f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            BuildUi();
            Theme.Apply(this);
            Refresh_();
        }

        private void BuildUi()
        {
            var banner = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = Color.FromArgb(12, 14, 13) };
            banner.Controls.Add(new Label
            {
                Text = "  Smart App Control (filtre de réputation Windows)", Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 13f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            var cap = new Label
            {
                Text = "État actuel", Location = new Point(20, 80), Size = new Size(200, 20),
                ForeColor = Theme.InkDimColor
            };
            Controls.Add(cap);

            _state = new Label
            {
                Text = "…", Location = new Point(20, 100), Size = new Size(580, 30),
                Font = new Font("Segoe UI Semibold", 15f, FontStyle.Bold)
            };
            Controls.Add(_state);

            _explain = new Label
            {
                Location = new Point(20, 136), Size = new Size(580, 56), ForeColor = Theme.InkColor
            };
            Controls.Add(_explain);

            var why = new Label
            {
                Location = new Point(20, 200), Size = new Size(580, 76), ForeColor = Theme.InkDimColor,
                Text = "Une application que tu viens de compiler est bloquée ?\n"
                     + "C'est le comportement normal de Smart App Control : il ne connaît ni sa signature ni sa "
                     + "réputation. La vraie solution côté développeur est de SIGNER l'exécutable (Sign.bat) et de "
                     + "le diffuser en un seul fichier ; couper le filtre ne règle le souci que sur TON PC."
            };
            Controls.Add(why);

            _warn = new Label
            {
                Location = new Point(20, 284), Size = new Size(580, 58),
                ForeColor = Color.FromArgb(200, 120, 40),
                Text = "À savoir avant de toucher à quoi que ce soit : d'après Microsoft, une fois Smart App Control "
                     + "désactivé, il ne peut plus être réactivé sans réinitialiser ou réinstaller Windows. "
                     + "Tout changement ne prend effet qu'après un redémarrage."
            };
            Controls.Add(_warn);

            int by = 360;
            _btnEval = Action_("Passer en mode Évaluation", 20, by,
                "Mode Évaluation : Windows observe sans rien bloquer.\n\n"
                + "C'est l'état le plus souple qui garde le filtre en place. Le changement prend effet au redémarrage.\n\n"
                + "Continuer ?", SmartAppControl.State.Evaluation);

            _btnOff = Action_("Désactiver", 250, by,
                "DÉSACTIVER Smart App Control ?\n\n"
                + "• Windows ne filtrera plus les applications non signées ou sans réputation.\n"
                + "• D'après Microsoft, ce choix est DÉFINITIF : il ne se rallume qu'en réinstallant Windows.\n"
                + "• Ton antivirus continue de fonctionner normalement.\n"
                + "• Effet au prochain redémarrage. Une sauvegarde .reg est écrite sur le Bureau.\n\n"
                + "Confirmer la désactivation ?", SmartAppControl.State.Off);

            _btnOn = Action_("Tenter de réactiver", 400, by,
                "Réactiver Smart App Control ?\n\n"
                + "Si le filtre a déjà été coupé sur ce PC, Windows ignorera la demande (réinstallation requise) : "
                + "l'app te dira honnêtement si l'état n'a pas changé.\n\n"
                + "Continuer ?", SmartAppControl.State.On);

            var winSec = new Button
            {
                Text = "Ouvrir Sécurité Windows (voie officielle)", Location = new Point(20, 440), Size = new Size(300, 32),
                FlatStyle = FlatStyle.Flat, BackColor = Theme.AccentColor, ForeColor = Color.FromArgb(16, 13, 9)
            };
            winSec.FlatAppearance.BorderSize = 0;
            winSec.Click += (s, e) =>
            {
                try { Process.Start(new ProcessStartInfo("windowsdefender://appbrowser") { UseShellExecute = true }); }
                catch { try { Process.Start(new ProcessStartInfo("ms-settings:windowsdefender") { UseShellExecute = true }); } catch { } }
            };
            Controls.Add(winSec);

            var refreshBtn = new Button
            {
                Text = "Actualiser", Location = new Point(330, 440), Size = new Size(120, 32), FlatStyle = FlatStyle.Flat
            };
            refreshBtn.Click += (s, e) => Refresh_();
            Controls.Add(refreshBtn);

            var close = new Button
            {
                Text = "Fermer", Location = new Point(500, 440), Size = new Size(100, 32), FlatStyle = FlatStyle.Flat
            };
            close.Click += (s, e) => Close();
            Controls.Add(close);
        }

        /// <summary>Bouton d'action : confirmation explicite, puis écriture (jamais l'inverse).</summary>
        private Button Action_(string label, int x, int y, string confirm, SmartAppControl.State target)
        {
            var b = new Button
            {
                Text = label, Location = new Point(x, y), Size = new Size(target == SmartAppControl.State.Evaluation ? 210 : 130, 34),
                FlatStyle = FlatStyle.Flat
            };
            b.Click += (s, e) =>
            {
                if (MessageBox.Show(this, confirm, "Smart App Control",
                        MessageBoxButtons.OKCancel, MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2) != DialogResult.OK) return;

                bool ok = SmartAppControl.Apply(target, _log);
                Refresh_();
                if (ok)
                    MessageBox.Show(this,
                        "C'est écrit. Le changement sera effectif après un REDÉMARRAGE du PC.\n\n"
                        + "Une sauvegarde .reg de l'état d'origine a été déposée sur ton Bureau.",
                        "Smart App Control", MessageBoxButtons.OK, MessageBoxIcon.Information);
                else
                    MessageBox.Show(this,
                        "L'état n'a pas changé.\n\n"
                        + "Soit Windows a refusé (c'est le cas quand on tente de RALLUMER un filtre déjà coupé : "
                        + "Microsoft impose une réinstallation), soit l'application n'a pas les droits administrateur.",
                        "Smart App Control", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
            Controls.Add(b);
            return b;
        }

        private void Refresh_()
        {
            SmartAppControl.State s = SmartAppControl.Read();
            _state.Text = SmartAppControl.Label(s);
            _state.ForeColor = s == SmartAppControl.State.On ? Theme.OkColor
                             : s == SmartAppControl.State.Evaluation ? Color.FromArgb(220, 140, 50)
                             : s == SmartAppControl.State.Off ? Color.FromArgb(210, 80, 70)
                             : Theme.InkDimColor;
            _explain.Text = SmartAppControl.Explain(s);

            // Tout RETOUR EN ARRIÈRE (évaluation comme réactivation) est refusé par Windows une fois
            // le filtre coupé : on grise les deux au lieu de promettre un bouton qui ne fera rien.
            bool usable = SmartAppControl.Available;
            bool canGoBack = SmartAppControl.CanReEnable;
            _btnEval.Enabled = usable && canGoBack && s != SmartAppControl.State.Evaluation;
            _btnOff.Enabled = usable && s != SmartAppControl.State.Off;
            _btnOn.Enabled = usable && canGoBack && s != SmartAppControl.State.On;

            if (!usable)
                _warn.Text = "Smart App Control n'est pas disponible sur ce PC : rien à régler ici. "
                           + "(Il demande Windows 11 22H2 ou plus récent, activé à l'installation.)";
            else if (!canGoBack)
                _warn.Text = "Smart App Control est déjà désactivé sur ce PC. Les deux retours en arrière sont grisés "
                           + "volontairement : d'après Microsoft, il ne se rallume qu'en réinitialisant ou en "
                           + "réinstallant Windows — un bouton qui n'aurait servi à rien.";
        }
    }
}
