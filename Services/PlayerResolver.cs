using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace OverlayApp.Services
{
    /// <summary>
    /// Service pour résoudre les pseudos (gameName uniquement) en comptes Riot complets (gameName#tagLine)
    /// </summary>
    public class PlayerResolver
    {
        private readonly RiotApiService _riotApi;
        private List<PlayerEntry> _knownPlayers; // Liste de joueurs connus avec rôle préféré optionnel
        private readonly string _configFile = "known_players.json";
        
        // TagLines courants à essayer en priorité pour la région EUW
        private readonly string[] _commonTagLines = new[]
        {
            "EUW", "FR1", "EUNE", "EUW1", "FR", "EU", 
            "NA1", "OCE", "KR", "JP", "BR1", "LAN", "LAS", "TR1", "RU"
        };

        public PlayerResolver(RiotApiService riotApi)
        {
            _riotApi = riotApi;
            _knownPlayers = LoadKnownPlayers();
        }

        /// <summary>
        /// Ajoute un joueur connu à la base de données
        /// </summary>
        public void AddKnownPlayer(string gameName, string tagLine, string role = "")
        {
            // Vérifier si cette combinaison existe déjà
            var existing = _knownPlayers.FirstOrDefault(p => 
                p.gameName.Equals(gameName, StringComparison.OrdinalIgnoreCase) && 
                p.tagLine.Equals(tagLine, StringComparison.OrdinalIgnoreCase));
            
            if (existing != null)
            {
                // Mettre à jour le rôle si fourni et pas déjà défini
                if (!string.IsNullOrEmpty(role) && string.IsNullOrEmpty(existing.preferredRole))
                {
                    existing.preferredRole = role;
                    SaveKnownPlayers();
                }
            }
            else
            {
                _knownPlayers.Add(new PlayerEntry
                {
                    gameName = gameName,
                    tagLine = tagLine,
                    preferredRole = role
                });
                SaveKnownPlayers();
            }
        }

        /// <summary>
        /// Charge la liste des joueurs connus depuis le fichier
        /// </summary>
        private List<PlayerEntry> LoadKnownPlayers()
        {
            try
            {
                if (File.Exists(_configFile))
                {
                    var json = File.ReadAllText(_configFile);
                    
                    // Essayer de charger le nouveau format avec preferredRole
                    try
                    {
                        var players = JsonSerializer.Deserialize<List<PlayerEntry>>(json);
                        if (players != null)
                        {
                            Console.WriteLine($"Chargé {players.Count} joueurs connus");
                            return players;
                        }
                    }
                    catch
                    {
                        // Si échec, essayer l'ancien format (dictionnaire)
                        var oldFormat = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                        if (oldFormat != null)
                        {
                            var result = oldFormat.Select(kv => new PlayerEntry
                            {
                                gameName = kv.Key,
                                tagLine = kv.Value,
                                preferredRole = "" // Pas de rôle dans l'ancien format
                            }).ToList();
                            Console.WriteLine($"Chargé {result.Count} joueurs connus (ancien format) - migration automatique");
                            
                            // Sauvegarder au nouveau format
                            _knownPlayers = result;
                            SaveKnownPlayers();
                            
                            return result;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur chargement joueurs connus: {ex.Message}");
            }

            return new List<PlayerEntry>();
        }

        /// <summary>
        /// Sauvegarde la liste des joueurs connus dans le fichier
        /// </summary>
        private void SaveKnownPlayers()
        {
            try
            {
                var json = JsonSerializer.Serialize(_knownPlayers, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                File.WriteAllText(_configFile, json);
                Console.WriteLine($"Sauvegardé {_knownPlayers.Count} joueurs connus");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur sauvegarde joueurs connus: {ex.Message}");
            }
        }

        private class PlayerEntry
        {
            public string gameName { get; set; } = "";
            public string tagLine { get; set; } = "";
            public string preferredRole { get; set; } = ""; // Role préféré (TOP, JUNGLE, MID, BOTTOM, SUPPORT)
        }

        /// <summary>
        /// Résout plusieurs joueurs en évitant les doublons (pour gérer les pseudos identiques)
        /// </summary>
        public async Task<List<(string gameName, string tagLine, string role)>> ResolvePlayersAsync(
            List<(string gameName, string role)> players)
        {
            var results = new List<(string gameName, string tagLine, string role)>();
            var usedAccounts = new HashSet<string>(); // Pour tracker les comptes déjà utilisés

            foreach (var (gameName, role) in players)
            {
                var resolved = await ResolvePlayerWithoutDuplicatesAsync(gameName, role, usedAccounts);
                if (resolved.HasValue)
                {
                    var accountKey = $"{resolved.Value.gameName}#{resolved.Value.tagLine}".ToLower();
                    usedAccounts.Add(accountKey);
                    results.Add((resolved.Value.gameName, resolved.Value.tagLine, role));
                    Console.WriteLine($"✓ Résolu: {gameName} -> {resolved.Value.gameName}#{resolved.Value.tagLine} ({role})");
                }
                else
                {
                    Console.WriteLine($"✗ Impossible de résoudre {gameName} pour le rôle {role}");
                }
            }

            return results;
        }

        /// <summary>
        /// Résout un gameName en excluant les comptes déjà utilisés et en priorisant le rôle
        /// </summary>
        private async Task<(string gameName, string tagLine)?> ResolvePlayerWithoutDuplicatesAsync(
            string gameName,
            string role,
            HashSet<string> usedAccounts)
        {
            if (string.IsNullOrWhiteSpace(gameName))
                return null;

            gameName = gameName.Trim();

            Console.WriteLine($"  🔍 Recherche de '{gameName}' (rôle: {role}) parmi {_knownPlayers.Count} joueurs connus");
            foreach (var kp in _knownPlayers)
            {
                Console.WriteLine($"     - {kp.gameName}#{kp.tagLine} (role: {kp.preferredRole})");
            }

            // 1. Vérifier les joueurs connus avec le même rôle en priorité
            var knownMatchesWithRole = _knownPlayers
                .Where(p => p.gameName.Equals(gameName, StringComparison.OrdinalIgnoreCase))
                .Where(p => !string.IsNullOrEmpty(p.preferredRole) && 
                           p.preferredRole.Equals(role, StringComparison.OrdinalIgnoreCase))
                .ToList();
            
            Console.WriteLine($"     → {knownMatchesWithRole.Count} match(es) avec rôle");

            foreach (var player in knownMatchesWithRole)
            {
                var accountKey = $"{player.gameName}#{player.tagLine}".ToLower();
                
                if (usedAccounts.Contains(accountKey))
                {
                    Console.WriteLine($"  ⊘ {player.gameName}#{player.tagLine} (role match) déjà utilisé, skip...");
                    continue;
                }

                var account = await _riotApi.GetAccountByRiotIdAsync(player.gameName, player.tagLine);
                if (account != null)
                {
                    Console.WriteLine($"  ✓ Joueur connu (role match): {player.gameName}#{player.tagLine} pour {role}");
                    return (player.gameName, player.tagLine);
                }
            }

            // 2. Si pas de match avec le rôle, essayer tous les joueurs connus avec ce pseudo
            var knownMatches = _knownPlayers
                .Where(p => p.gameName.Equals(gameName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            
            Console.WriteLine($"     → {knownMatches.Count} match(es) total sans filtre de rôle");

            foreach (var player in knownMatches)
            {
                var accountKey = $"{player.gameName}#{player.tagLine}".ToLower();
                
                if (usedAccounts.Contains(accountKey))
                {
                    Console.WriteLine($"  ⊘ {player.gameName}#{player.tagLine} déjà utilisé, skip...");
                    continue;
                }

                var account = await _riotApi.GetAccountByRiotIdAsync(player.gameName, player.tagLine);
                if (account != null)
                {
                    Console.WriteLine($"  ✓ Joueur connu disponible: {player.gameName}#{player.tagLine}");
                    return (player.gameName, player.tagLine);
                }
            }

            // 2. Essayer les tagLines courants (en excluant ceux déjà utilisés)
            Console.WriteLine($"  Recherche de {gameName} avec les tagLines courants...");
            foreach (var tagLine in _commonTagLines)
            {
                var accountKey = $"{gameName}#{tagLine}".ToLower();
                
                if (usedAccounts.Contains(accountKey))
                {
                    continue; // Ce compte est déjà utilisé
                }

                var account = await _riotApi.GetAccountByRiotIdAsync(gameName, tagLine);
                if (account != null)
                {
                    Console.WriteLine($"  ✓ Trouvé: {gameName}#{tagLine}");
                    AddKnownPlayer(gameName, tagLine, role);
                    return (gameName, tagLine);
                }
                
                await Task.Delay(100);
            }

            Console.WriteLine($"  ✗ Aucun compte disponible pour: {gameName}");
            return null;
        }
    }
}
