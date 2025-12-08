# Draftinator - BBW Official Draft Helper

Application C# d'analyse de draft League of Legends avec overlay transparent qui affiche les statistiques des joueurs en temps réel.

## Fonctionnalités principales

### 🎯 Détection automatique des joueurs
- **Mode OCR** : Détection automatique des pseudos et rôles via OCR (Tesseract)
- **Mode Test** : Configuration manuelle via `test_players.json` pour les tests
- **Auto-calibration** : Détection automatique de la fenêtre League of Legends
- **Déduction intelligente** : Attribution automatique des rôles manquants

### 📊 Statistiques des joueurs
- **Niveau de maîtrise** : Affichage du niveau invocateur
- **Champions principaux** : Top 5 champions filtrés par rôle détecté
- **Points de maîtrise** : Affichage des points avec icônes des champions
- **Tokens de maîtrise** : Visualisation des tokens M6/M7
- **Niveaux de champion** : Niveau de maîtrise par champion (0-7)

### 🎨 Interface
- Overlay transparent au-dessus de toutes les fenêtres
- Panels redimensionnables automatiquement (5 joueurs)
- Icônes des champions issues de Data Dragon
- Code couleur selon le niveau de maîtrise :
  - 🔵 Niveau 5 : Bleu (Dodger Blue)
  - 🔴 Niveau 6 : Rouge (Crimson)
  - 🟣 Niveau 7 : Violet (Blue Violet)
  - ⚪ Autres : Blanc

## Structure du projet

### Fichiers principaux
- `Program.cs` - Point d'entrée de l'application
- `OverlayForm.cs` - Fenêtre overlay principale et logique UI

### Services
- `RiotApiService.cs` - Appels API Riot (Account-v1, Summoner-v4, Champion Mastery-v4)
- `PlayerResolver.cs` - Résolution gameName → gameName#tagLine avec cache
- `DraftOcrService.cs` - Détection OCR des pseudos et rôles + déduction des rôles manquants
- `AutoDetectionService.cs` - Auto-détection des zones de joueurs
- `ScreenCaptureService.cs` - Capture d'écran avec gestion multi-moniteurs
- `ChampionIconService.cs` - Chargement et cache des icônes de champions

### UI
- `PlayerStatsPanel.cs` - Panel individuel pour chaque joueur
- `CalibrationForm.cs` - Interface de sélection manuelle des zones OCR

### Modèles
- `SummonerDto.cs` - Modèles de données API Riot

### Configuration
- `riot_api_key.txt` - Clé API Riot (non versionnée)
- `known_players.json` - Cache des joueurs résolus avec rôles préférés
- `ocr_regions.json` - Zones OCR calibrées
- `test_players.json` - Joueurs pour le mode test
- `champion_roles.json` - Mapping champions → rôles jouables (170 champions)

### Données
- `15.23.1/` - Data Dragon (champion.json + icônes)

## Installation

### Prérequis
- .NET 8.0 SDK
- Tesseract OCR (inclus via NuGet)
- Clé API Riot Development

### Configuration

1. **Clé API Riot**
   ```
   Créez riot_api_key.txt à la racine avec votre clé API
   ```

2. **Mode Test** (optionnel)
   ```json
   // test_players.json
   [
     {
       "playerName": "Pseudo",
       "tagLine": "TAG",
       "role": "TOP"
     }
   ]
   ```

3. **Build et exécution**
   ```powershell
   dotnet build
   dotnet run
   ```

## Utilisation

### Démarrage
1. Lancez l'application
2. L'overlay s'affiche en mode transparent
3. Deux modes disponibles :
   - **Mode OCR** : Détection automatique (par défaut)
   - **Mode Test** : Utilise test_players.json

### Mode OCR
1. Assurez-vous que League of Legends est ouvert sur l'écran de draft
2. Cliquez sur **"Analyser Draft"**
3. Première utilisation : auto-calibration automatique de la zone gauche
4. Les 5 joueurs sont détectés et leurs stats affichées

### Calibration manuelle
- Si l'auto-détection échoue, utilisez **"Calibrer OCR"**
- Sélectionnez pour chaque joueur :
  1. Zone du pseudo
  2. Zone du rôle (au-dessus)

### Raccourcis clavier
- **ESC** : Fermer l'application
- **Bouton "Basculer"** : Alterner entre mode OCR et Test

## Architecture technique

### Filtrage par rôle
Les champions affichés sont filtrés selon le rôle détecté grâce au fichier `champion_roles.json` :
- Récupération des 30 meilleurs champions en points de maîtrise
- Filtrage selon les rôles jouables du champion
- Affichage des 5 meilleurs pour le rôle

### Déduction des rôles manquants
Si un joueur n'a pas de rôle visible (ex: partie avec bots) :
1. Détection des rôles déjà assignés (TOP, JUNGLE, MID, BOTTOM, SUPPORT)
2. Calcul des rôles manquants
3. Attribution automatique aux joueurs sans rôle

### Résolution des pseudos
Le système résout les pseudos incomplets (sans tagLine) :
1. Recherche dans le cache (`known_players.json`)
2. Priorisation selon le rôle préféré enregistré
3. Évitement des doublons (plusieurs comptes même pseudo)
4. Tentative avec tagLines courants (EUW, FR1, etc.)
5. Mise en cache des résultats

### Code refactorisé
- **Méthode commune** `DisplayPlayerStats()` utilisée par les deux modes
- **Pas de duplication** entre mode Test et OCR
- **LoadTestPlayers()** réutilisable
- Les améliorations bénéficient automatiquement aux deux modes

## Fichiers exclus (.gitignore)
- `riot_api_key.txt` - Clé API sensible
- `*.key`, `*.secret` - Autres données sensibles
- `debug_*.png/jpg/bmp` - Images de debug
- `detection_*.png/jpg/bmp` - Images de détection
- `bin/`, `obj/` - Artifacts de build

## API Riot utilisées
- **Account-v1** : Récupération PUUID depuis gameName#tagLine
- **Summoner-v4** : Informations invocateur (niveau, icône)
- **Champion Mastery-v4** : Points et niveaux de maîtrise par champion

## Dépendances NuGet
- `Tesseract` - OCR
- `System.Drawing.Common` - Manipulation d'images
- `System.Text.Json` - Sérialisation JSON

## Limitations connues
- Rate limiting API Riot : délai de 500ms entre chaque joueur
- OCR nécessite une bonne qualité d'image et contraste
- Nécessite que League of Legends soit visible à l'écran


## Contributions
Projet interne BBW - Sponge

## License
Propriété de BBW - Tous droits réservés
