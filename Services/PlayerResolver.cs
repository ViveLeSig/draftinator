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
        private Dictionary<string, string> _knownPlayers; // gameName -> tagLine
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
        /// Résout un gameName en un compte Riot complet en essayant différents tagLines
        /// </summary>
        public async Task<(string gameName, string tagLine)?> ResolvePlayerAsync(string gameName)
        {
            if (string.IsNullOrWhiteSpace(gameName))
                return null;

            // Nettoyer le gameName
            gameName = gameName.Trim();

            // 1. Vérifier si on connaît déjà ce joueur
            if (_knownPlayers.TryGetValue(gameName.ToLower(), out var knownTagLine))
            {
                Console.WriteLine($"Joueur connu trouvé: {gameName}#{knownTagLine}");
                return (gameName, knownTagLine);
            }

            // 2. Essayer les tagLines courants
            Console.WriteLine($"Recherche de {gameName} avec les tagLines courants...");
            foreach (var tagLine in _commonTagLines)
            {
                var account = await _riotApi.GetAccountByRiotIdAsync(gameName, tagLine);
                if (account != null)
                {
                    Console.WriteLine($"✓ Trouvé: {gameName}#{tagLine}");
                    
                    // Sauvegarder pour la prochaine fois
                    AddKnownPlayer(gameName, tagLine);
                    
                    return (gameName, tagLine);
                }
                
                // Petit délai pour éviter le rate limiting
                await Task.Delay(100);
            }

            Console.WriteLine($"✗ Impossible de résoudre: {gameName}");
            return null;
        }

        /// <summary>
        /// Ajoute un joueur connu à la base de données
        /// </summary>
        public void AddKnownPlayer(string gameName, string tagLine)
        {
            _knownPlayers[gameName.ToLower()] = tagLine;
            SaveKnownPlayers();
        }

        /// <summary>
        /// Charge la liste des joueurs connus depuis le fichier
        /// </summary>
        private Dictionary<string, string> LoadKnownPlayers()
        {
            try
            {
                if (File.Exists(_configFile))
                {
                    var json = File.ReadAllText(_configFile);
                    var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    Console.WriteLine($"Chargé {loaded?.Count ?? 0} joueurs connus");
                    return loaded ?? new Dictionary<string, string>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur chargement joueurs connus: {ex.Message}");
            }

            return new Dictionary<string, string>();
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

        /// <summary>
        /// Résout plusieurs joueurs en parallèle
        /// </summary>
        public async Task<List<(string gameName, string tagLine, string role)>> ResolvePlayersAsync(
            List<(string gameName, string role)> players)
        {
            var results = new List<(string gameName, string tagLine, string role)>();

            foreach (var (gameName, role) in players)
            {
                var resolved = await ResolvePlayerAsync(gameName);
                if (resolved.HasValue)
                {
                    results.Add((resolved.Value.gameName, resolved.Value.tagLine, role));
                }
                else
                {
                    Console.WriteLine($"Impossible de résoudre {gameName} pour le rôle {role}");
                }
            }

            return results;
        }
    }
}
