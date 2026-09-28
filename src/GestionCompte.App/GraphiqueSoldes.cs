using System.Collections;
using System.Windows;
using System.Windows.Media;
using GestionCompte.Core;
using GestionCompte.Presentation;

namespace GestionCompte.App;

/// <summary>
/// Courbe du solde de fin de mois : trait plein pour les mois réels, pointillés pour les mois prévus,
/// points rouges sous zéro. Les couleurs sont fournies par l'apparence (DynamicResource).
/// </summary>
public sealed class GraphiqueSoldes : FrameworkElement
{
    public static readonly DependencyProperty LignesProperty = Propriete<IEnumerable>(nameof(Lignes));
    public static readonly DependencyProperty CouleurProperty = Propriete<Brush>(nameof(Couleur));
    public static readonly DependencyProperty CouleurNegativeProperty = Propriete<Brush>(nameof(CouleurNegative));
    public static readonly DependencyProperty CouleurGrilleProperty = Propriete<Brush>(nameof(CouleurGrille));
    public static readonly DependencyProperty CouleurTexteProperty = Propriete<Brush>(nameof(CouleurTexte));
    public static readonly DependencyProperty CouleurFondProperty = Propriete<Brush>(nameof(CouleurFond));

    public IEnumerable? Lignes { get => (IEnumerable?)GetValue(LignesProperty); set => SetValue(LignesProperty, value); }
    public Brush? Couleur { get => (Brush?)GetValue(CouleurProperty); set => SetValue(CouleurProperty, value); }
    public Brush? CouleurNegative { get => (Brush?)GetValue(CouleurNegativeProperty); set => SetValue(CouleurNegativeProperty, value); }
    public Brush? CouleurGrille { get => (Brush?)GetValue(CouleurGrilleProperty); set => SetValue(CouleurGrilleProperty, value); }
    public Brush? CouleurTexte { get => (Brush?)GetValue(CouleurTexteProperty); set => SetValue(CouleurTexteProperty, value); }
    public Brush? CouleurFond { get => (Brush?)GetValue(CouleurFondProperty); set => SetValue(CouleurFondProperty, value); }

    private static DependencyProperty Propriete<T>(string nom) =>
        DependencyProperty.Register(nom, typeof(T), typeof(GraphiqueSoldes),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double MargeGauche = 78;
    private const double MargeDroite = 14;
    private const double MargeHaut = 12;
    private const double MargeBas = 30;

    protected override void OnRender(DrawingContext dc)
    {
        var points = Lignes?.OfType<LignePrevisionViewModel>().ToList() ?? new List<LignePrevisionViewModel>();
        var largeur = ActualWidth - MargeGauche - MargeDroite;
        var hauteur = ActualHeight - MargeHaut - MargeBas;
        if (points.Count == 0 || largeur <= 0 || hauteur <= 0)
            return;

        var couleur = Couleur ?? Brushes.SteelBlue;
        var negative = CouleurNegative ?? Brushes.Firebrick;
        var grille = new Pen(CouleurGrille ?? Brushes.LightGray, 1);
        var texte = CouleurTexte ?? Brushes.Gray;
        var dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // Échelle verticale arrondie, qui inclut toujours zéro.
        var min = Math.Min(0, (double)points.Min(p => p.SoldeFin));
        var max = Math.Max(0, (double)points.Max(p => p.SoldeFin));
        var pas = PasArrondi((max - min) / 4);
        min = Math.Floor(min / pas) * pas;
        max = Math.Ceiling(max / pas) * pas;
        if (max <= min)
            max = min + pas;

        double Y(double valeur) => MargeHaut + (max - valeur) / (max - min) * hauteur;
        double X(int i) => MargeGauche + (points.Count == 1 ? largeur / 2 : i * largeur / (points.Count - 1));

        for (var v = min; v <= max + pas / 2; v += pas)
        {
            var y = Y(v);
            dc.DrawLine(Math.Abs(v) < pas / 1000 ? new Pen(texte, 1.4) : grille, new Point(MargeGauche, y), new Point(MargeGauche + largeur, y));
            var etiquette = Texte($"{v.ToString("N0", Montants.Francais)} €", texte, dip);
            dc.DrawText(etiquette, new Point(MargeGauche - 8 - etiquette.Width, y - etiquette.Height / 2));
        }

        // Étiquettes des mois (une sur deux si la place manque).
        var saut = largeur / points.Count < 46 ? 2 : 1;
        for (var i = 0; i < points.Count; i += saut)
        {
            var etiquette = Texte(points[i].LibelleCourt, texte, dip);
            dc.DrawText(etiquette, new Point(X(i) - etiquette.Width / 2, MargeHaut + hauteur + 8));
        }

        // Séparation entre le réel et le prévu.
        var dernierReel = points.FindLastIndex(p => p.Reel);
        if (dernierReel >= 0 && dernierReel < points.Count - 1)
        {
            var x = (X(dernierReel) + X(dernierReel + 1)) / 2;
            dc.DrawLine(new Pen(texte, 1) { DashStyle = new DashStyle(new double[] { 2, 3 }, 0) },
                new Point(x, MargeHaut), new Point(x, MargeHaut + hauteur));
            var aujourdhui = Texte("prévu →", texte, dip);
            dc.DrawText(aujourdhui, new Point(x + 5, MargeHaut));
        }

        // Surface légère sous la courbe, puis la courbe.
        var surface = new StreamGeometry();
        using (var g = surface.Open())
        {
            g.BeginFigure(new Point(X(0), Y(0)), true, true);
            for (var i = 0; i < points.Count; i++)
                g.LineTo(new Point(X(i), Y((double)points[i].SoldeFin)), false, true);
            g.LineTo(new Point(X(points.Count - 1), Y(0)), false, true);
        }
        surface.Freeze();
        var remplissage = couleur.CloneCurrentValue();
        remplissage.Opacity = 0.12;
        dc.DrawGeometry(remplissage, null, surface);

        var pleine = new Pen(couleur, 3) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var pointillee = new Pen(couleur, 3) { DashStyle = new DashStyle(new double[] { 1.5, 1.5 }, 0), DashCap = PenLineCap.Round };
        for (var i = 1; i < points.Count; i++)
        {
            dc.DrawLine(points[i].Reel ? pleine : pointillee,
                new Point(X(i - 1), Y((double)points[i - 1].SoldeFin)), new Point(X(i), Y((double)points[i].SoldeFin)));
        }

        var fond = CouleurFond ?? Brushes.White;
        for (var i = 0; i < points.Count; i++)
        {
            var point = new Point(X(i), Y((double)points[i].SoldeFin));
            var pinceau = points[i].Negatif ? negative : couleur;
            dc.DrawEllipse(points[i].Reel ? pinceau : fond, new Pen(pinceau, 2.2), point, 4.5, 4.5);
        }
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
