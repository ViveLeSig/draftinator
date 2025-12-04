using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace OverlayApp.Models
{
    public class SummonerDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("accountId")]
        public string AccountId { get; set; } = string.Empty;

        [JsonPropertyName("puuid")]
        public string Puuid { get; set; } = string.Empty;

        [JsonPropertyName("profileIconId")]
        public int ProfileIconId { get; set; }

        [JsonPropertyName("revisionDate")]
        public long RevisionDate { get; set; }

        [JsonPropertyName("summonerLevel")]
        public long SummonerLevel { get; set; }
    }

    public class AccountDto
    {
        [JsonPropertyName("puuid")]
        public string Puuid { get; set; } = string.Empty;

        [JsonPropertyName("gameName")]
        public string GameName { get; set; } = string.Empty;

        [JsonPropertyName("tagLine")]
        public string TagLine { get; set; } = string.Empty;
    }

    public class ChampionMasteryDto
    {
        [JsonPropertyName("puuid")]
        public string Puuid { get; set; } = string.Empty;

        [JsonPropertyName("championId")]
        public long ChampionId { get; set; }

        [JsonPropertyName("championLevel")]
        public int ChampionLevel { get; set; }

        [JsonPropertyName("championPoints")]
        public int ChampionPoints { get; set; }

        [JsonPropertyName("lastPlayTime")]
        public long LastPlayTime { get; set; }

        [JsonPropertyName("championPointsSinceLastLevel")]
        public long ChampionPointsSinceLastLevel { get; set; }

        [JsonPropertyName("championPointsUntilNextLevel")]
        public long ChampionPointsUntilNextLevel { get; set; }

        [JsonPropertyName("markRequiredForNextLevel")]
        public int MarkRequiredForNextLevel { get; set; }

        [JsonPropertyName("tokensEarned")]
        public int TokensEarned { get; set; }

        [JsonPropertyName("milestoneGrades")]
        public List<string> MilestoneGrades { get; set; } = new();
    }

    public class MatchDto
    {
        [JsonPropertyName("metadata")]
        public MetadataDto Metadata { get; set; } = new();

        [JsonPropertyName("info")]
        public InfoDto Info { get; set; } = new();
    }

    public class MetadataDto
    {
        [JsonPropertyName("matchId")]
        public string MatchId { get; set; } = string.Empty;

        [JsonPropertyName("participants")]
        public List<string> Participants { get; set; } = new();
    }

    public class InfoDto
    {
        [JsonPropertyName("gameCreation")]
        public long GameCreation { get; set; }

        [JsonPropertyName("gameDuration")]
        public long GameDuration { get; set; }

        [JsonPropertyName("gameMode")]
        public string GameMode { get; set; } = string.Empty;

        [JsonPropertyName("participants")]
        public List<ParticipantDto> Participants { get; set; } = new();
    }

    public class ParticipantDto
    {
        [JsonPropertyName("puuid")]
        public string Puuid { get; set; } = string.Empty;

        [JsonPropertyName("summonerName")]
        public string SummonerName { get; set; } = string.Empty;

        [JsonPropertyName("championName")]
        public string ChampionName { get; set; } = string.Empty;

        [JsonPropertyName("championId")]
        public int ChampionId { get; set; }

        [JsonPropertyName("kills")]
        public int Kills { get; set; }

        [JsonPropertyName("deaths")]
        public int Deaths { get; set; }

        [JsonPropertyName("assists")]
        public int Assists { get; set; }

        [JsonPropertyName("win")]
        public bool Win { get; set; }

        [JsonPropertyName("lane")]
        public string Lane { get; set; } = string.Empty;

        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;
    }
}
