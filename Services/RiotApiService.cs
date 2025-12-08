using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using OverlayApp.Models;

namespace OverlayApp.Services
{
    // Classe pour désérialiser le nouveau format champion_roles.json
    public class ChampionRoleData
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
        
        [JsonPropertyName("roles")]
        public List<string> Roles { get; set; } = new();
    }
    
    public class RiotApiService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _region; // ex: "euw1", "na1"
        private readonly string _regionalRoute; // ex: "europe", "americas"
        private Dictionary<long, List<string>> _championRoles = new();

        public RiotApiService(string apiKey, string region = "euw1", string regionalRoute = "europe")
        {
            _apiKey = apiKey;
            _region = region;
            _regionalRoute = regionalRoute;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("X-Riot-Token", _apiKey);
            LoadChampionRoles();
        }

        /// <summary>
        /// Charge les rôles des champions depuis champion_roles.json
        /// </summary>
        private void LoadChampionRoles()
        {
            try
            {
                var jsonPath = "champion_roles.json";
                if (!File.Exists(jsonPath))
                {
                    OverlayApp.OverlayForm.LogStatic($"⚠️ Fichier {jsonPath} introuvable, filtrage par rôle désactivé");
                    return;
                }

                var json = File.ReadAllText(jsonPath);
                var rolesDict = JsonSerializer.Deserialize<Dictionary<string, ChampionRoleData>>(json);
                
                if (rolesDict != null)
                {
                    foreach (var kvp in rolesDict)
                    {
                        if (long.TryParse(kvp.Key, out var championId))
                        {
                            _championRoles[championId] = kvp.Value.Roles;
                        }
                    }
                    OverlayApp.OverlayForm.LogStatic($"✅ {_championRoles.Count} champions chargés avec leurs rôles");
                }
            }
            catch (Exception ex)
            {
                OverlayApp.OverlayForm.LogStatic($"❌ Erreur chargement champion_roles.json: {ex.Message}");
            }
        }

        /// <summary>
        /// Teste si la clé API est valide en faisant un appel simple
        /// </summary>
        public async Task<(bool isValid, string message)> ValidateApiKeyAsync()
        {
            try
            {
                // Tester avec un compte connu (Riot Games)
                var url = $"https://{_regionalRoute}.api.riotgames.com/riot/account/v1/accounts/by-riot-id/Riot/EUW";
                OverlayApp.OverlayForm.LogStatic($"Test validation API: {url}");
                var response = await _httpClient.GetAsync(url);
                
                OverlayApp.OverlayForm.LogStatic($"Status validation: {(int)response.StatusCode} {response.StatusCode}");
                
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized || 
                    response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    OverlayApp.OverlayForm.LogStatic($"Erreur API: {errorContent}");
                    return (false, $"❌ Clé API invalide ou expirée ({(int)response.StatusCode} {response.StatusCode})");
                }
                
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    return (false, "⚠️ Rate limit atteint (429) - la clé est valide mais trop sollicitée");
                }
                
                if (response.IsSuccessStatusCode)
                {
                    return (true, "✅ Clé API valide");
                }
                
                var unexpectedError = await response.Content.ReadAsStringAsync();
                OverlayApp.OverlayForm.LogStatic($"Erreur inattendue: {unexpectedError}");
                return (false, $"⚠️ Erreur inattendue: {(int)response.StatusCode} {response.StatusCode}");
            }
            catch (Exception ex)
            {
                OverlayApp.OverlayForm.LogStatic($"Exception validation: {ex}");
                return (false, $"❌ Erreur de connexion: {ex.Message}");
            }
        }

        // Account-V1: Récupérer compte par Riot ID (GameName#TagLine)
        public async Task<AccountDto?> GetAccountByRiotIdAsync(string gameName, string tagLine)
        {
            try
            {
                var url = $"https://{_regionalRoute}.api.riotgames.com/riot/account/v1/accounts/by-riot-id/{gameName}/{tagLine}";
                OverlayApp.OverlayForm.LogStatic($"API Call: {url}");
                var response = await _httpClient.GetAsync(url);
                
                OverlayApp.OverlayForm.LogStatic($"Status: {response.StatusCode}");
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    OverlayApp.OverlayForm.LogStatic($"Erreur API: {errorContent}");
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                OverlayApp.OverlayForm.LogStatic($"Response: {json}");
                return JsonSerializer.Deserialize<AccountDto>(json);
            }
            catch (Exception ex)
            {
                OverlayApp.OverlayForm.LogStatic($"Erreur GetAccountByRiotId: {ex.Message}");
                return null;
            }
        }

        // Summoner-V4: Récupérer summoner par PUUID
        public async Task<SummonerDto?> GetSummonerByPuuidAsync(string puuid)
        {
            try
            {
                var url = $"https://{_region}.api.riotgames.com/lol/summoner/v4/summoners/by-puuid/{puuid}";
                var response = await _httpClient.GetAsync(url);
                
                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<SummonerDto>(json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur GetSummonerByPuuid: {ex.Message}");
                return null;
            }
        }

        // Champion-Mastery-V4: Top champions par PUUID
        public async Task<List<ChampionMasteryDto>> GetTopChampionMasteriesAsync(string puuid, int count = 10)
        {
            try
            {
                var url = $"https://{_region}.api.riotgames.com/lol/champion-mastery/v4/champion-masteries/by-puuid/{puuid}/top?count={count}";
                var response = await _httpClient.GetAsync(url);
                
                if (!response.IsSuccessStatusCode)
                    return new List<ChampionMasteryDto>();

                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<List<ChampionMasteryDto>>(json) ?? new List<ChampionMasteryDto>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur GetTopChampionMasteries: {ex.Message}");
                return new List<ChampionMasteryDto>();
            }
        }

        // Champion-Mastery-V4: Top champions par PUUID et par rôle spécifique
        public async Task<List<ChampionMasteryDto>> GetTopChampionMasteriesByRoleAsync(string puuid, string role, int count = 5)
        {
            try
            {
                // Vérifier que le fichier de rôles est chargé
                if (_championRoles.Count == 0)
                {
                    OverlayApp.OverlayForm.LogStatic($"⚠️ Rôles des champions non chargés, utilisation des top champions globaux");
                    return await GetTopChampionMasteriesAsync(puuid, count);
                }

                OverlayApp.OverlayForm.LogStatic($"🎯 Filtrage des champions pour le rôle {role}");
                
                // 1. Récupérer plus de champions pour avoir un meilleur choix (30 au lieu de 5)
                var allMasteries = await GetTopChampionMasteriesAsync(puuid, 30);
                OverlayApp.OverlayForm.LogStatic($"📊 {allMasteries.Count} champions récupérés");
                
                // 2. Filtrer les champions qui peuvent jouer ce rôle
                var filteredMasteries = allMasteries
                    .Where(m => _championRoles.ContainsKey(m.ChampionId) && _championRoles[m.ChampionId].Contains(role))
                    .OrderByDescending(m => m.ChampionPoints)
                    .Take(count)
                    .ToList();
                
                OverlayApp.OverlayForm.LogStatic($"✅ {filteredMasteries.Count} champions filtrés pour {role}");
                foreach (var m in filteredMasteries)
                {
                    var roles = _championRoles.GetValueOrDefault(m.ChampionId, new List<string>());
                    OverlayApp.OverlayForm.LogStatic($"  - Champion {m.ChampionId}: {m.ChampionPoints} pts (rôles: {string.Join(", ", roles)})");
                }
                
                // 3. Si aucun champion trouvé pour ce rôle, retourner les top globaux
                if (filteredMasteries.Count == 0)
                {
                    OverlayApp.OverlayForm.LogStatic($"⚠️ Aucun champion trouvé pour {role}, utilisation des top champions globaux");
                    return await GetTopChampionMasteriesAsync(puuid, count);
                }
                
                return filteredMasteries;
            }
            catch (Exception ex)
            {
                OverlayApp.OverlayForm.LogStatic($"❌ Erreur GetTopChampionMasteriesByRole: {ex.Message}");
                return await GetTopChampionMasteriesAsync(puuid, count);
            }
        }



        // Match-V5: Historique des matchs
        public async Task<List<string>> GetMatchHistoryAsync(string puuid, int count = 20)
        {
            try
            {
                var url = $"https://{_regionalRoute}.api.riotgames.com/lol/match/v5/matches/by-puuid/{puuid}/ids?start=0&count={count}";
                var response = await _httpClient.GetAsync(url);
                
                if (!response.IsSuccessStatusCode)
                    return new List<string>();

                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur GetMatchHistory: {ex.Message}");
                return new List<string>();
            }
        }

        // Match-V5: Détails d'un match
        public async Task<MatchDto?> GetMatchDetailsAsync(string matchId)
        {
            try
            {
                var url = $"https://{_regionalRoute}.api.riotgames.com/lol/match/v5/matches/{matchId}";
                var response = await _httpClient.GetAsync(url);
                
                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<MatchDto>(json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur GetMatchDetails: {ex.Message}");
                return null;
            }
        }

        // Méthode helper: Récupérer toutes les infos d'un joueur
        public async Task<PlayerStats?> GetPlayerStatsAsync(string gameName, string tagLine, string? role = null)
        {
            // 1. Récupérer l'account
            var account = await GetAccountByRiotIdAsync(gameName, tagLine);
            if (account == null)
                return null;

            // 2. Récupérer le summoner
            var summoner = await GetSummonerByPuuidAsync(account.Puuid);
            if (summoner == null)
                return null;

            // 3. Récupérer les top champions (par rôle si spécifié)
            List<ChampionMasteryDto> topChampions;
            if (!string.IsNullOrEmpty(role))
            {
                OverlayApp.OverlayForm.LogStatic($"🎯 Récupération des champions pour le rôle: {role}");
                topChampions = await GetTopChampionMasteriesByRoleAsync(account.Puuid, role, 5);
            }
            else
            {
                OverlayApp.OverlayForm.LogStatic("📊 Récupération des top champions globaux");
                topChampions = await GetTopChampionMasteriesAsync(account.Puuid, 5);
            }

            // 4. Récupérer l'historique récent
            var matchHistory = await GetMatchHistoryAsync(account.Puuid, 10);

            return new PlayerStats
            {
                Account = account,
                Summoner = summoner,
                TopChampions = topChampions,
                RecentMatchIds = matchHistory
            };
        }
    }

    // Classe helper pour regrouper les stats
    public class PlayerStats
    {
        public AccountDto Account { get; set; } = new();
        public SummonerDto Summoner { get; set; } = new();
        public List<ChampionMasteryDto> TopChampions { get; set; } = new();
        public List<string> RecentMatchIds { get; set; } = new();
        public string Role { get; set; } = string.Empty; // TOP, JUNGLE, MID, BOTTOM, SUPPORT
    }
}
