using System.Diagnostics;
using System.Windows;
using GestionCompte.Presentation;

namespace GestionCompte.App;

/// <summary>Saisie de la clé API gratuite d'une IA (Gemini ou Mistral).</summary>
public partial class FenetreCleIA : Window
{
    private readonly string _adresse;
    private readonly bool _cleExistante;

    public FenetreCleIA(string nom, string adresseCle, bool cleExistante, string modele, string modeleParDefaut)
    {
        InitializeComponent();
        _adresse = adresseCle;
        _cleExistante = cleExistante;
        Title = $"Clé {nom}";
        Explication.Text = $"Créez gratuitement une clé API {nom} avec votre compte (bouton ci-dessous), puis collez-la ici.";
        BoutonLien.Content = $"Créer une clé {nom}";
        if (cleExistante)
            LibelleCle.Text = "Clé API (laisser vide pour garder celle déjà enregistrée)";
        LibelleModele.Text = $"Modèle (facultatif, par défaut {modeleParDefaut})";
        Modele.Text = modele;
        Loaded += (_, _) => Cle.Focus();
    }

    public SaisieIA? Saisie { get; private set; }

    private void OuvrirLien(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(_adresse) { UseShellExecute = true });

    private void Valider(object sender, RoutedEventArgs e)
    {
        if (!_cleExistante && Cle.Password.Trim().Length == 0)
        {
            Cle.Focus();
            return;
        }
        Saisie = new SaisieIA(Cle.Password.Trim().Length > 0 ? Cle.Password.Trim() : null, Modele.Text.Trim());
        DialogResult = true;
    }
}
