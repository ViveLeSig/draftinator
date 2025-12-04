using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using OverlayApp.Models;

namespace OverlayApp.Services
{
    public class RiotApiService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _region; // ex: "euw1", "na1"
        private readonly string _regionalRoute; // ex: "europe", "americas"

        public RiotApiService(string apiKey, string region = "euw1", string regionalRoute = "europe")
        {
            _apiKey = apiKey;
            _region = region;
            _regionalRoute = regionalRoute;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("X-Riot-Token", _apiKey);
        }

        // Account-V1: Récupérer compte par Riot ID (GameName#TagLine)
        public async Task<AccountDto?> GetAccountByRiotIdAsync(string gameName, string tagLine)
        {
            try
            {
                var url = $"https://{_regionalRoute}.api.riotgames.com/riot/account/v1/accounts/by-riot-id/{gameName}/{tagLine}";
                Console.WriteLine($"API Call: {url}");
                var response = await _httpClient.GetAsync(url);
                
                Console.WriteLine($"Status: {response.StatusCode}");
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Erreur API: {errorContent}");
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Response: {json}");
                return JsonSerializer.Deserialize<AccountDto>(json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur GetAccountByRiotId: {ex.Message}");
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
        public async Task<PlayerStats?> GetPlayerStatsAsync(string gameName, string tagLine)
        {
            // 1. Récupérer l'account
            var account = await GetAccountByRiotIdAsync(gameName, tagLine);
            if (account == null)
                return null;

            // 2. Récupérer le summoner
            var summoner = await GetSummonerByPuuidAsync(account.Puuid);
            if (summoner == null)
                return null;

            // 3. Récupérer les top champions
            var topChampions = await GetTopChampionMasteriesAsync(account.Puuid, 5);

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
    }
}
