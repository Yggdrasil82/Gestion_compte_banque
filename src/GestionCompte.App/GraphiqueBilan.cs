using System.Collections;
using System.Windows;
using System.Windows.Media;
using GestionCompte.Core;
using GestionCompte.Presentation;

namespace GestionCompte.App;

/// <summary>
/// Barres du bilan, mois par mois : revenus à gauche, dépenses à droite avec l'épargne empilée au-dessus.
/// Les couleurs sont fournies par l'apparence (DynamicResource).
/// </summary>
public sealed class GraphiqueBilan : FrameworkElement
{
    public static readonly DependencyProperty MoisProperty = Propriete<IEnumerable>(nameof(Mois));
    public static readonly DependencyProperty CouleurRevenusProperty = Propriete<Brush>(nameof(CouleurRevenus));
    public static readonly DependencyProperty CouleurDepensesProperty = Propriete<Brush>(nameof(CouleurDepenses));
    public static readonly DependencyProperty CouleurEpargneProperty = Propriete<Brush>(nameof(CouleurEpargne));
    public static readonly DependencyProperty CouleurGrilleProperty = Propriete<Brush>(nameof(CouleurGrille));
    public static readonly DependencyProperty CouleurTexteProperty = Propriete<Brush>(nameof(CouleurTexte));

    public IEnumerable? Mois { get => (IEnumerable?)GetValue(MoisProperty); set => SetValue(MoisProperty, value); }
    public Brush? CouleurRevenus { get => (Brush?)GetValue(CouleurRevenusProperty); set => SetValue(CouleurRevenusProperty, value); }
    public Brush? CouleurDepenses { get => (Brush?)GetValue(CouleurDepensesProperty); set => SetValue(CouleurDepensesProperty, value); }
    public Brush? CouleurEpargne { get => (Brush?)GetValue(CouleurEpargneProperty); set => SetValue(CouleurEpargneProperty, value); }
    public Brush? CouleurGrille { get => (Brush?)GetValue(CouleurGrilleProperty); set => SetValue(CouleurGrilleProperty, value); }
    public Brush? CouleurTexte { get => (Brush?)GetValue(CouleurTexteProperty); set => SetValue(CouleurTexteProperty, value); }

    private static DependencyProperty Propriete<T>(string nom) =>
        DependencyProperty.Register(nom, typeof(T), typeof(GraphiqueBilan),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double MargeGauche = 70;
    private const double MargeDroite = 8;
    private const double MargeHaut = 10;
    private const double MargeBas = 28;

    protected override void OnRender(DrawingContext dc)
    {
        var mois = Mois?.OfType<MoisBilanViewModel>().ToList() ?? new List<MoisBilanViewModel>();
        var largeur = ActualWidth - MargeGauche - MargeDroite;
        var hauteur = ActualHeight - MargeHaut - MargeBas;
        if (mois.Count == 0 || largeur <= 0 || hauteur <= 0)
            return;

        var revenus = CouleurRevenus ?? Brushes.SeaGreen;
        var depenses = CouleurDepenses ?? Brushes.DarkOrange;
        var epargne = CouleurEpargne ?? Brushes.MediumPurple;
        var grille = new Pen(CouleurGrille ?? Brushes.LightGray, 1);
        var texte = CouleurTexte ?? Brushes.Gray;
        var dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        var max = (double)mois.Max(m => Math.Max(m.Revenus, Math.Max(0, m.Depenses) + Math.Max(0, m.Epargne)));
        var pas = PasArrondi(max / 4);
        max = Math.Max(pas, Math.Ceiling(max / pas) * pas);
        double Y(double valeur) => MargeHaut + hauteur - valeur / max * hauteur;

        for (var v = 0.0; v <= max + pas / 2; v += pas)
        {
            var y = Y(v);
            dc.DrawLine(grille, new Point(MargeGauche, y), new Point(MargeGauche + largeur, y));
            var etiquette = Texte($"{v.ToString("N0", Montants.Francais)} €", texte, dip);
            dc.DrawText(etiquette, new Point(MargeGauche - 8 - etiquette.Width, y - etiquette.Height / 2));
        }

        var colonne = largeur / mois.Count;
        var barre = Math.Min(18, colonne * 0.34);
        var saut = colonne < 40 ? 2 : 1;
        for (var i = 0; i < mois.Count; i++)
        {
            var m = mois[i];
            var x = MargeGauche + i * colonne + colonne / 2;
            Barre(dc, revenus, x - barre - 1, 0, (double)m.Revenus, barre, Y);
            var bas = (double)Math.Max(0, m.Depenses);
            Barre(dc, depenses, x + 1, 0, bas, barre, Y);
            Barre(dc, epargne, x + 1, bas, bas + (double)Math.Max(0, m.Epargne), barre, Y);
            if (i % saut == 0)
            {
                var etiquette = Texte(m.LibelleCourt, texte, dip);
                dc.DrawText(etiquette, new Point(x - etiquette.Width / 2, MargeHaut + hauteur + 8));
            }
        }
    }

    private static void Barre(DrawingContext dc, Brush couleur, double x, double de, double a, double largeur, Func<double, double> y)
    {
        if (a <= de)
            return;
        dc.DrawRoundedRectangle(couleur, null, new Rect(x, y(a), largeur, y(de) - y(a)), 2, 2);
    }

    private static FormattedText Texte(string texte, Brush couleur, double dip) =>
        new(texte, Montants.Francais, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, couleur, dip);

    /// <summary>Pas de graduation « rond » : 1, 2 ou 5 × une puissance de 10.</summary>
    private static double PasArrondi(double brut)
    {
        if (brut <= 0)
            return 100;
        var puissance = Math.Pow(10, Math.Floor(Math.Log10(brut)));
        var normalise = brut / puissance;
        return (normalise <= 1 ? 1 : normalise <= 2 ? 2 : normalise <= 5 ? 5 : 10) * puissance;
    }
}
