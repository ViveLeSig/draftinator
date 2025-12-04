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
        private ChampionIconService _championIconService;

        public PlayerStatsPanel(ChampionIconService championIconService)
        {
            _championIconService = championIconService;
            InitializeComponents();
        }

        private void InitializeComponents()
        {
            this.BackColor = Color.FromArgb(200, 20, 30, 40); // Semi-transparent dark
            this.BorderStyle = BorderStyle.FixedSingle;
            this.Size = new Size(300, 150); // Augmenté pour afficher 5 champions
            this.Padding = new Padding(8);

            // Nom du joueur
            playerNameLabel = new Label
            {
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(8, 8),
                Text = "Chargement..."
            };

            // Niveau
            levelLabel = new Label
            {
                Font = new Font("Segoe UI", 8),
                ForeColor = Color.LightGray,
                AutoSize = true,
                Location = new Point(8, 30),
                Text = "Niveau: --"
            };

            // Maîtrise
            masteryLabel = new Label
            {
                Font = new Font("Segoe UI", 8),
                ForeColor = Color.LightGray,
                AutoSize = true,
                Location = new Point(8, 45),
                Text = "Champions maîtrisés: --"
            };

            // Panneau pour les icônes de champions
            championIconsPanel = new FlowLayoutPanel
            {
                Location = new Point(8, 60),
                Size = new Size(284, 80),  // Augmenté pour afficher 5 champions (52px * 5 + espaces)
                AutoScroll = false,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.Transparent
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

            // Afficher le rôle entre parenthèses si présent
            var roleText = !string.IsNullOrEmpty(stats.Role) ? $" ({stats.Role})" : "";
            playerNameLabel.Text = $"{stats.Account.GameName}#{stats.Account.TagLine}{roleText}";
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
            var masteryColor = GetMasteryColor(champion.ChampionLevel);
            
            var panel = new Panel
            {
                Size = new Size(52, 70),  // Augmenté pour avoir de la place pour les points
                BackColor = Color.FromArgb(40, 40, 50),
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(2)
            };

            // Icône du champion - plus petite et en haut
            var championIcon = new PictureBox
            {
                Size = new Size(50, 50),
                Location = new Point(1, 1),
                SizeMode = PictureBoxSizeMode.StretchImage,
                BackColor = Color.Black
            };
            
            var icon = _championIconService.GetChampionIcon(champion.ChampionId);
            if (icon != null)
            {
                championIcon.Image = icon;
            }
            else
            {
                // Fallback: afficher le nom du champion
                var nameLabel = new Label
                {
                    Text = _championIconService.GetChampionName(champion.ChampionId),
                    Font = new Font("Segoe UI", 7, FontStyle.Bold),
                    ForeColor = Color.White,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Dock = DockStyle.Fill,
                    BackColor = Color.Black
                };
                championIcon.Controls.Add(nameLabel);
            }

            // Badge séparé en Panel coloré positionné par-dessus l'icône
            var badgePanel = new Panel
            {
                Size = new Size(18, 18),
                Location = new Point(32, 0),
                BackColor = masteryColor
            };
            
            var badgeLabel = new Label
            {
                Text = champion.ChampionLevel.ToString(),
                Font = new Font("Segoe UI", 8, FontStyle.Bold),
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent
            };
            
            badgePanel.Controls.Add(badgeLabel);
            
            // Ajouter le badge AU-DESSUS de l'icône (en tant qu'enfant du PictureBox)
            championIcon.Controls.Add(badgePanel);

            // Points de maîtrise sous l'icône
            var pointsLabel = new Label
            {
                Text = $"{champion.ChampionPoints / 1000}K",
                Font = new Font("Segoe UI", 8, FontStyle.Bold),
                ForeColor = Color.Gold,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, 52),
                Size = new Size(52, 16),
                BackColor = Color.Transparent
            };

            panel.Controls.Add(championIcon);
            panel.Controls.Add(pointsLabel);
            
            return panel;
        }

        private Color GetMasteryColor(int level)
        {
            // Couleurs vives sans transparence alpha pour meilleure visibilité
            return level switch
            {
                7 => Color.FromArgb(255, 138, 43, 226),   // Violet foncé (BlueViolet) - M7
                6 => Color.FromArgb(255, 220, 20, 60),    // Rouge vif (Crimson) - M6
                5 => Color.FromArgb(255, 30, 144, 255),   // Bleu vif - M5
                4 => Color.FromArgb(255, 70, 130, 180),   // Bleu acier - M4
                3 => Color.FromArgb(255, 50, 205, 50),    // Vert lime - M3
                2 => Color.FromArgb(255, 255, 140, 0),    // Orange foncé - M2
                1 => Color.FromArgb(255, 169, 169, 169),  // Gris clair - M1
                _ => Color.FromArgb(255, 105, 105, 105)   // Gris foncé - M0
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
