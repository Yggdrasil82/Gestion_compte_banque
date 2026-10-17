using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GestionCompte.Presentation;

/// <summary>Une IA réglée, cochée ou non pour un module.</summary>
public sealed partial class CaseIA : ObservableObject
{
    private readonly Action<bool> _changee;

    public CaseIA(string nom, bool cochee, Action<bool> changee)
    {
        Nom = nom;
        _cochee = cochee;
        _changee = changee;
    }

    public string Nom { get; }

    [ObservableProperty] private bool _cochee;

    partial void OnCocheeChanged(bool value) => _changee(value);
}

/// <summary>Cases « IA utilisées » en haut d'un module (Achats, Assistant, Lettres, Crédits), mémorisées sur ce PC.</summary>
public sealed class ChoixIAModule : ObservableObject
{
    private readonly ServicesIA _ia;

    public ChoixIAModule(ServicesIA ia, string cle, string libelle)
    {
        _ia = ia;
        Cle = cle;
        Libelle = libelle;
        Actualiser();
        _ia.ReglagesChanges += (_, _) => Actualiser();
    }

    /// <summary>Clé du réglage (ex. « achats »).</summary>
    public string Cle { get; }

    /// <summary>Nom du module dans les messages (ex. « les achats »).</summary>
    public string Libelle { get; }

    /// <summary>IA réglées et cochées pour ce module.</summary>
    public IReadOnlyList<Data.Achats.IAssistantIA> Assistants() => _ia.Assistants(Cle);

    /// <summary>Message quand aucune IA n'est utilisable dans ce module.</summary>
    public string MessageAucune => _ia.MessageAucune(Libelle);

    public ObservableCollection<CaseIA> Cases { get; } = new();

    /// <summary>Les cases ne s'affichent que s'il y a un choix à faire (au moins deux IA réglées).</summary>
    public bool Visible => Cases.Count > 1;

    private void Actualiser()
    {
        Cases.Clear();
        foreach (var nom in _ia.Assistants().Select(a => a.Nom))
            Cases.Add(new CaseIA(nom, _ia.Utilisee(Cle, nom), cochee => _ia.Utiliser(Cle, nom, cochee)));
        OnPropertyChanged(nameof(Visible));
    }
}
