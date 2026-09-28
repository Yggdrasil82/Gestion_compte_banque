using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GestionCompte.Presentation;

public enum Ambiance
{
    Ocean,
    Pastel,
    Nuit,
}

/// <summary>Préférences d'affichage, enregistrées à part des données du compte.</summary>
public sealed class PreferencesAffichage
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Ambiance Ambiance { get; set; } = Ambiance.Ocean;

    public bool ModeSombre { get; set; }

    /// <summary>Lit les préférences ; valeurs par défaut si le fichier est absent ou illisible.</summary>
    public static PreferencesAffichage Charger(string? chemin)
    {
        if (chemin is null || !File.Exists(chemin))
            return new PreferencesAffichage();

        try
        {
            return JsonSerializer.Deserialize<PreferencesAffichage>(File.ReadAllText(chemin)) ?? new PreferencesAffichage();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return new PreferencesAffichage();
        }
    }

    public void Enregistrer(string chemin)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(chemin))!);
        File.WriteAllText(chemin, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}

/// <summary>Choix de l'ambiance de couleurs et du mode sombre.</summary>
public sealed class ApparenceViewModel : ObservableObject
{
    private readonly string? _chemin;
    private readonly PreferencesAffichage _preferences;

    /// <param name="chemin">Fichier des préférences ; null = rien n'est enregistré (captures, tests).</param>
    public ApparenceViewModel(string? chemin)
    {
        _chemin = chemin;
        _preferences = PreferencesAffichage.Charger(chemin);
    }

    /// <summary>Levé quand l'apparence à afficher change.</summary>
    public event EventHandler? Changee;

    public static IReadOnlyList<ChoixAmbiance> Ambiances { get; } = new[]
    {
        new ChoixAmbiance(Ambiance.Ocean, "Océan"),
        new ChoixAmbiance(Ambiance.Pastel, "Pastel"),
        new ChoixAmbiance(Ambiance.Nuit, "Nuit"),
    };

    public Ambiance Ambiance
    {
        get => _preferences.Ambiance;
        set
        {
            if (!SetProperty(_preferences.Ambiance, value, _preferences, (p, v) => p.Ambiance = v))
                return;
            OnPropertyChanged(nameof(ModeSombreModifiable));
            OnPropertyChanged(nameof(Sombre));
            Appliquer();
        }
    }

    /// <summary>Mode sombre demandé (sans effet pour Nuit, toujours sombre).</summary>
    public bool ModeSombre
    {
        get => _preferences.ModeSombre;
        set
        {
            if (!SetProperty(_preferences.ModeSombre, value, _preferences, (p, v) => p.ModeSombre = v))
                return;
            OnPropertyChanged(nameof(Sombre));
            Appliquer();
        }
    }

    /// <summary>L'ambiance Nuit est toujours sombre : l'option n'a alors pas d'effet.</summary>
    public bool ModeSombreModifiable => Ambiance != Ambiance.Nuit;

    /// <summary>Apparence réellement affichée en sombre.</summary>
    public bool Sombre => Ambiance == Ambiance.Nuit || ModeSombre;

    private void Appliquer()
    {
        if (_chemin is not null)
        {
            try
            {
                _preferences.Enregistrer(_chemin);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Une préférence d'affichage non enregistrée n'est pas grave : elle reste active jusqu'à la fermeture.
            }
        }

        Changee?.Invoke(this, EventArgs.Empty);
    }
}

public sealed record ChoixAmbiance(Ambiance Valeur, string Nom)
{
    public override string ToString() => Nom;
}
