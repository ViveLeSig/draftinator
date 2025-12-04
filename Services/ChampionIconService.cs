using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.Json;

namespace OverlayApp.Services
{
    public class ChampionIconService
    {
        private readonly Dictionary<long, string> _championIdToName;
        private readonly string _iconBasePath;
        private readonly Dictionary<long, Image> _iconCache;

        public ChampionIconService()
        {
            _championIdToName = new Dictionary<long, string>();
            _iconCache = new Dictionary<long, Image>();
            _iconBasePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "15.23.1", "img", "champion");
            
            LoadChampionMapping();
        }

        private void LoadChampionMapping()
        {
            try
            {
                var jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "15.23.1", "data", "fr_FR", "champion.json");
                
                if (!File.Exists(jsonPath))
                {
                    Console.WriteLine($"Champion.json not found at: {jsonPath}");
                    return;
                }

                var jsonContent = File.ReadAllText(jsonPath);
                using var document = JsonDocument.Parse(jsonContent);
                
                var dataElement = document.RootElement.GetProperty("data");
                
                foreach (var championProperty in dataElement.EnumerateObject())
                {
                    var champion = championProperty.Value;
                    var key = long.Parse(champion.GetProperty("key").GetString()!);
                    var id = champion.GetProperty("id").GetString()!;
                    
                    _championIdToName[key] = id;
                }
                
                Console.WriteLine($"Loaded {_championIdToName.Count} champions");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading champion mapping: {ex.Message}");
            }
        }

        public Image? GetChampionIcon(long championId)
        {
            // Check cache first
            if (_iconCache.TryGetValue(championId, out var cachedIcon))
            {
                return cachedIcon;
            }

            // Get champion name from ID
            if (!_championIdToName.TryGetValue(championId, out var championName))
            {
                Console.WriteLine($"Unknown champion ID: {championId}");
                return null;
            }

            // Load icon
            var iconPath = Path.Combine(_iconBasePath, $"{championName}.png");
            
            if (!File.Exists(iconPath))
            {
                Console.WriteLine($"Icon not found: {iconPath}");
                return null;
            }

            try
            {
                // Load and cache the icon
                var icon = Image.FromFile(iconPath);
                _iconCache[championId] = icon;
                return icon;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading icon for {championName}: {ex.Message}");
                return null;
            }
        }

        public string GetChampionName(long championId)
        {
            return _championIdToName.TryGetValue(championId, out var name) ? name : championId.ToString();
        }
    }
}
