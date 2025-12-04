using System;
using System.Drawing;
using System.Windows.Forms;
using OverlayApp.Services;
using OverlayApp.Models;

namespace OverlayApp.UI
{
    public class PlayerStatsPanel : Panel
    {
        private Label playerNameLabel;
        private Label levelLabel;
        private Label masteryLabel;
        private FlowLayoutPanel championIconsPanel;

        public PlayerStatsPanel()
        {
            InitializeComponents();
        }

        private void InitializeComponents()
        {
            this.BackColor = Color.FromArgb(200, 20, 30, 40); // Semi-transparent dark
            this.BorderStyle = BorderStyle.FixedSingle;
            this.Size = new Size(300, 150);
            this.Padding = new Padding(10);

            // Nom du joueur
            playerNameLabel = new Label
            {
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(10, 10),
                Text = "Chargement..."
            };

            // Niveau
            levelLabel = new Label
            {
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.LightGray,
                AutoSize = true,
                Location = new Point(10, 35),
                Text = "Niveau: --"
            };

            // Maîtrise
            masteryLabel = new Label
            {
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.LightGray,
                AutoSize = true,
                Location = new Point(10, 55),
                Text = "Champions maîtrisés: --"
            };

            // Panneau pour les icônes de champions
            championIconsPanel = new FlowLayoutPanel
            {
                Location = new Point(10, 80),
                Size = new Size(280, 60),
                AutoScroll = false,
                FlowDirection = FlowDirection.LeftToRight
            };

            this.Controls.AddRange(new Control[] {
                playerNameLabel,
                levelLabel,
                masteryLabel,
                championIconsPanel
            });
        }

        public void UpdatePlayerStats(PlayerStats stats)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => UpdatePlayerStats(stats)));
                return;
            }

            playerNameLabel.Text = $"{stats.Account.GameName}#{stats.Account.TagLine}";
            levelLabel.Text = $"Niveau: {stats.Summoner.SummonerLevel}";
            masteryLabel.Text = ""; // Texte retiré pour gagner de la place

            // Afficher les top champions
            championIconsPanel.Controls.Clear();
            foreach (var champion in stats.TopChampions)
            {
                var championPanel = CreateChampionMasteryItem(champion);
                championIconsPanel.Controls.Add(championPanel);
            }
        }

        private Panel CreateChampionMasteryItem(ChampionMasteryDto champion)
        {
            var panel = new Panel
            {
                Size = new Size(50, 60),
                BackColor = Color.FromArgb(100, 50, 50, 70)
            };

            // ID du champion (temporaire, sera remplacé par une icône)
            var idLabel = new Label
            {
                Text = champion.ChampionId.ToString(),
                Font = new Font("Segoe UI", 8, FontStyle.Bold),
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top,
                Height = 30
            };

            // Points de maîtrise
            var pointsLabel = new Label
            {
                Text = $"{champion.ChampionPoints / 1000}K",
                Font = new Font("Segoe UI", 7),
                ForeColor = Color.Gold,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Bottom,
                Height = 20
            };

            // Niveau de maîtrise (badge bien visible)
            var levelBadge = new Label
            {
                Text = champion.ChampionLevel.ToString(),
                Font = new Font("Segoe UI", 8, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = GetMasteryColor(champion.ChampionLevel),
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(20, 20),
                Location = new Point(28, 0),
                BorderStyle = BorderStyle.FixedSingle
            };

            panel.Controls.AddRange(new Control[] { idLabel, pointsLabel, levelBadge });
            return panel;
        }

        private Color GetMasteryColor(int level)
        {
            return level switch
            {
                7 => Color.FromArgb(138, 43, 226),   // Violet foncé (BlueViolet)
                6 => Color.FromArgb(220, 20, 60),     // Rouge vif (Crimson)
                5 => Color.FromArgb(0, 119, 182),     // Bleu foncé
                4 => Color.FromArgb(70, 130, 180),    // Bleu acier
                _ => Color.FromArgb(105, 105, 105)    // Gris foncé
            };
        }

        public void ShowError(string message)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => ShowError(message)));
                return;
            }

            playerNameLabel.Text = "Erreur";
            levelLabel.Text = message;
            masteryLabel.Text = "";
            championIconsPanel.Controls.Clear();
        }

        public void ShowLoading(string playerName)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => ShowLoading(playerName)));
                return;
            }

            playerNameLabel.Text = playerName;
            levelLabel.Text = "Chargement des statistiques...";
            masteryLabel.Text = "";
            championIconsPanel.Controls.Clear();
        }
    }
}
