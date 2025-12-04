# Configuration Draftinator

## Obtenir une clé API Riot Games

1. Rendez-vous sur https://developer.riotgames.com/
2. Connectez-vous avec votre compte Riot Games
3. Allez dans le Dashboard
4. Générez une nouvelle clé API de développement (valable 24h)
5. Pour une clé production, faites une demande officielle

## Configuration de l'application

1. Lancez l'application: `dotnet run`
2. Cliquez sur le bouton "⚙️ Config"
3. Entrez votre clé API Riot
4. Cliquez sur "Sauvegarder"

## Installation de Tesseract OCR (requis pour la détection automatique)

### Windows
1. Téléchargez Tesseract depuis: https://github.com/UB-Mannheim/tesseract/wiki
2. Installez-le dans `C:\Program Files\Tesseract-OCR`
3. Téléchargez les données de langue anglaise
4. Placez le dossier `tessdata` dans le répertoire de l'application

## Régions disponibles

### Plateformes régionales (pour Summoner-V4, Champion-Mastery-V4, etc.)
- `br1` - Brésil
- `eun1` - Europe Nord & Est
- `euw1` - Europe Ouest
- `jp1` - Japon
- `kr` - Corée
- `la1` - Amérique Latine Nord
- `la2` - Amérique Latine Sud
- `na1` - Amérique du Nord
- `oc1` - Océanie
- `tr1` - Turquie
- `ru` - Russie
- `ph2` - Philippines
- `sg2` - Singapour
- `th2` - Thaïlande
- `tw2` - Taiwan
- `vn2` - Vietnam

### Routes régionales (pour Account-V1, Match-V5)
- `americas` - Amérique du Nord, Amérique du Sud, Amérique Latine, Brésil
- `asia` - Corée, Japon, Asie du Sud-Est
- `europe` - Europe (tous les serveurs)
- `sea` - Asie du Sud-Est

## Modifier la région par défaut

Dans `OverlayForm.cs`, ligne ~140:
```csharp
_riotApiService = new RiotApiService(apiKeyTextBox.Text, "euw1", "europe");
```

Changez `"euw1"` et `"europe"` selon votre région.

## Limitations

- **Rate Limiting**: Les clés de développement sont limitées à 20 requêtes/seconde et 100 requêtes/2 minutes
- **Durée**: Les clés de développement expirent après 24h
- **OCR**: La détection automatique des noms nécessite Tesseract installé et configuré
