# Draftinator - BBW Official draft helper

Application C# avec overlay transparent qui s'affiche au-dessus de toutes les fenêtres.

## Structure du projet

- `OverlayApp.csproj` - Fichier de projet .NET
- `Program.cs` - Point d'entrée de l'application
- `OverlayForm.cs` - Fenêtre overlay principale

## Fonctionnalités

- ✅ Fenêtre overlay transparente
- ✅ Reste au-dessus de toutes les applications (TopMost)
- ✅ Plein écran
- ✅ Fermeture avec la touche ESC
- ✅ Contenu personnalisable

## Comment exécuter

```powershell
dotnet build
dotnet run
```

## Personnalisation

Dans `OverlayForm.cs`, vous pouvez modifier :

- **Transparence** : Ajustez `this.Opacity` (0.0 à 1.0)
- **Couleur de fond** : Modifiez `this.BackColor` et `this.TransparencyKey`
- **Contenu** : Ajoutez vos propres contrôles dans `AddDemoContent()`
- **Clics à travers** : Décommentez les lignes Windows API pour permettre les clics à travers l'overlay

## Options avancées

Pour permettre aux clics de passer à travers l'overlay, décommentez les lignes suivantes dans `OverlayForm.cs` :

```csharp
int initialStyle = GetWindowLong(this.Handle, -20);
SetWindowLong(this.Handle, -20, initialStyle | 0x80000 | 0x20);
```

Et décommentez aussi les imports DllImport au bas du fichier.
