using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace GestionCompte.Presentation;

/// <summary>Morceau de texte d'un paragraphe du manuel.</summary>
/// <param name="Touche">Raccourci clavier (« Ctrl + J »), affiché comme une touche.</param>
public sealed record SegmentManuel(string Texte, bool Gras = false, bool Touche = false);

public abstract record BlocManuel;

/// <param name="Niveau">3 pour un sous-titre (les titres de chapitre sont ceux des <see cref="ChapitreManuel"/>).</param>
public sealed record TitreManuel(int Niveau, string Texte) : BlocManuel;

public sealed record ParagrapheManuel(IReadOnlyList<SegmentManuel> Segments) : BlocManuel;

public sealed record ListeManuel(bool Numerotee, IReadOnlyList<IReadOnlyList<SegmentManuel>> Elements) : BlocManuel;

/// <param name="Largeurs">Largeur de chaque colonne en % (null : le reste).</param>
public sealed record TableauManuel(IReadOnlyList<double?> Largeurs, IReadOnlyList<LigneTableauManuel> Lignes) : BlocManuel;

public sealed record LigneTableauManuel(bool Entete, IReadOnlyList<IReadOnlyList<SegmentManuel>> Cellules);

/// <param name="Largeur">Part de la largeur de la page, de 0 à 1.</param>
public sealed record ImageManuel(string Fichier, string? Legende, double Largeur) : BlocManuel;

/// <summary>Encadré (« À savoir »), orange quand <paramref name="Attention"/>.</summary>
public sealed record EncartManuel(bool Attention, IReadOnlyList<BlocManuel> Blocs) : BlocManuel;

public sealed record ChapitreManuel(string Titre, IReadOnlyList<BlocManuel> Blocs)
{
    /// <summary>Tout le texte du chapitre, pour la recherche.</summary>
    public string Texte { get; } = Titre + "\n" + string.Join("\n", Blocs.Select(TexteDe));

    private static string TexteDe(BlocManuel bloc) => bloc switch
    {
        TitreManuel t => t.Texte,
        ParagrapheManuel p => Joindre(p.Segments),
        ListeManuel l => string.Join("\n", l.Elements.Select(Joindre)),
        TableauManuel t => string.Join("\n", t.Lignes.SelectMany(l => l.Cellules).Select(Joindre)),
        ImageManuel i => i.Legende ?? "",
        EncartManuel e => string.Join("\n", e.Blocs.Select(TexteDe)),
        _ => "",
    };

    private static string Joindre(IReadOnlyList<SegmentManuel> segments) => string.Concat(segments.Select(s => s.Texte));
}

/// <summary>
/// Manuel d'utilisation intégré à l'application (rubrique « Aide ») : le même texte que le manuel PDF
/// (docs/manuel/manuel.html), découpé en chapitres, avec ses captures d'écran.
/// </summary>
public static class Manuel
{
    private const string PrefixeRessource = "GestionCompte.Manuel.";

    /// <summary>Chapitres du manuel embarqué dans l'application.</summary>
    public static IReadOnlyList<ChapitreManuel> Charger()
    {
        using var flux = typeof(Manuel).Assembly.GetManifestResourceStream(PrefixeRessource + "manuel.html");
        if (flux is null)
            return Array.Empty<ChapitreManuel>();
        using var lecteur = new StreamReader(flux, Encoding.UTF8);
        return Lire(lecteur.ReadToEnd());
    }

    /// <summary>Capture d'écran du manuel (« images/1-mois-ocean.png »), ou null si elle n'est pas embarquée.</summary>
    public static Stream? Image(string fichier) =>
        typeof(Manuel).Assembly.GetManifestResourceStream(PrefixeRessource + "images." + Path.GetFileName(fichier));

    /// <summary>Version indiquée sur la couverture du manuel (« 2.7.0 »), ou null.</summary>
    public static string? Version(string html) =>
        Regex.Match(html, @"version\s+(\d+\.\d+(?:\.\d+)?)") is { Success: true } m ? m.Groups[1].Value : null;

    /// <summary>Découpe le manuel HTML en chapitres (un par titre h2), sans la couverture ni le sommaire.</summary>
    public static IReadOnlyList<ChapitreManuel> Lire(string html)
    {
        var racine = Analyser(html);
        var corps = Trouver(racine, "body") ?? racine;

        var chapitres = new List<ChapitreManuel>();
        string? titre = null;
        var blocs = new List<BlocManuel>();

        void Terminer()
        {
            if (titre is not null)
                chapitres.Add(new ChapitreManuel(titre, blocs.ToList()));
            blocs.Clear();
        }

        foreach (var noeud in Aplatir(corps))
        {
            if (noeud.Nom == "h2")
            {
                Terminer();
                var texte = TexteBrut(noeud);
                titre = texte == "Sommaire" ? null : texte;
                continue;
            }
            if (titre is null)
                continue; // couverture et sommaire
            if (Bloc(noeud) is { } bloc)
                blocs.Add(bloc);
        }
        Terminer();
        return chapitres;
    }

    /// <summary>Contenu des sections, l'un après l'autre (les sections ne sont que des sauts de page du PDF).</summary>
    private static IEnumerable<Noeud> Aplatir(Noeud parent)
    {
        foreach (var enfant in parent.Enfants)
        {
            if (enfant.Nom == "section")
            {
                if (enfant.Classe.Contains("couverture"))
                    continue;
                foreach (var n in Aplatir(enfant))
                    yield return n;
            }
            else if (enfant.Nom is not null)
                yield return enfant;
        }
    }

    private static BlocManuel? Bloc(Noeud noeud)
    {
        switch (noeud.Nom)
        {
            case "h3":
                return new TitreManuel(3, TexteBrut(noeud));
            case "p":
                return Segments(noeud) is { Count: > 0 } segments ? new ParagrapheManuel(segments) : null;
            case "ul":
            case "ol":
                return new ListeManuel(noeud.Nom == "ol",
                    noeud.Enfants.Where(e => e.Nom == "li").Select(e => (IReadOnlyList<SegmentManuel>)Segments(e)).ToList());
            case "table":
                var lignes = Descendants(noeud, "tr").Select(tr => new LigneTableauManuel(
                    tr.Enfants.Any(c => c.Nom == "th"),
                    tr.Enfants.Where(c => c.Nom is "td" or "th").Select(c => (IReadOnlyList<SegmentManuel>)Segments(c)).ToList())).ToList();
                var premiere = Descendants(noeud, "tr").FirstOrDefault();
                var largeurs = premiere?.Enfants.Where(c => c.Nom is "td" or "th").Select(c => Pourcentage(c.Attribut("style"))).ToList()
                               ?? new List<double?>();
                return new TableauManuel(largeurs, lignes);
            case "figure":
                var image = Descendants(noeud, "img").FirstOrDefault();
                if (image?.Attribut("src") is not { } src)
                    return null;
                var legende = Descendants(noeud, "figcaption").FirstOrDefault() is { } fc ? TexteBrut(fc) : null;
                var largeur = Pourcentage(image.Attribut("style")) / 100
                              ?? (noeud.Classe.Contains("demi") ? 0.78 : noeud.Classe.Contains("grand") ? 0.86 : 1.0);
                return new ImageManuel(src, legende, largeur);
            case "div" when noeud.Classe.Contains("encart"):
                return new EncartManuel(noeud.Classe.Contains("attention"), noeud.Enfants.Select(Bloc).OfType<BlocManuel>().ToList());
            default:
                return null;
        }
    }

    private static double? Pourcentage(string? style) =>
        style is not null && Regex.Match(style, @"width\s*:\s*([\d.]+)%") is { Success: true } m
            ? double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)
            : null;

    /// <summary>Texte d'un élément en segments (gras, touches), espaces regroupés.</summary>
    private static List<SegmentManuel> Segments(Noeud noeud)
    {
        var segments = new List<SegmentManuel>();

        void Parcourir(Noeud n, bool gras, bool touche)
        {
            if (n.Nom is null)
            {
                var texte = Regex.Replace(n.Texte, @"\s+", " ");
                if (texte.Length == 0)
                    return;
                if (segments.Count > 0 && segments[^1].Gras == gras && segments[^1].Touche == touche)
                    segments[^1] = segments[^1] with { Texte = segments[^1].Texte + texte };
                else
                    segments.Add(new SegmentManuel(texte, gras, touche));
                return;
            }
            if (n.Nom is "ul" or "ol" or "table" or "figure")
                return;
            foreach (var enfant in n.Enfants)
                Parcourir(enfant, gras || n.Nom is "b" or "strong", touche || n.Classe.Contains("touche"));
        }

        foreach (var enfant in noeud.Enfants)
            Parcourir(enfant, false, false);

        if (segments.Count > 0)
        {
            segments[0] = segments[0] with { Texte = segments[0].Texte.TrimStart() };
            segments[^1] = segments[^1] with { Texte = segments[^1].Texte.TrimEnd() };
        }
        return segments.Where(s => s.Texte.Length > 0).ToList();
    }

    private static string TexteBrut(Noeud noeud) => string.Concat(Segments(noeud).Select(s => s.Texte));

    private static Noeud? Trouver(Noeud parent, string nom) => Descendants(parent, nom).FirstOrDefault();

    private static IEnumerable<Noeud> Descendants(Noeud parent, string nom)
    {
        foreach (var enfant in parent.Enfants)
        {
            if (enfant.Nom == nom)
                yield return enfant;
            foreach (var n in Descendants(enfant, nom))
                yield return n;
        }
    }

    // ---- Lecture du HTML (le sous-ensemble utilisé par le manuel) ----

    private static readonly HashSet<string> ElementsVides = new() { "img", "meta", "br", "hr", "link", "input" };

    private sealed class Noeud
    {
        public string? Nom { get; init; }
        public string Texte { get; init; } = "";
        public Dictionary<string, string> Attributs { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<Noeud> Enfants { get; } = new();
        public Noeud? Parent { get; init; }

        public string? Attribut(string nom) => Attributs.GetValueOrDefault(nom);

        public IReadOnlyList<string> Classe =>
            (Attribut("class") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    private static Noeud Analyser(string html)
    {
        var racine = new Noeud { Nom = "#document" };
        var courant = racine;
        html = Regex.Replace(html, "<!--.*?-->", "", RegexOptions.Singleline);
        html = Regex.Replace(html, "<(style|script)\\b.*?</\\1>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        foreach (Match m in Regex.Matches(html, @"<(/?)([a-zA-Z][a-zA-Z0-9]*)([^>]*)>|([^<]+)|<"))
        {
            if (m.Groups[4].Success || m.Value == "<")
            {
                courant.Enfants.Add(new Noeud { Texte = WebUtility.HtmlDecode(m.Value), Parent = courant });
                continue;
            }

            var nom = m.Groups[2].Value.ToLowerInvariant();
            if (m.Groups[1].Value == "/")
            {
                // Remonte jusqu'à l'élément fermé (balises mal fermées tolérées).
                var n = courant;
                while (n is not null && n.Nom != nom)
                    n = n.Parent;
                if (n?.Parent is not null)
                    courant = n.Parent;
                continue;
            }

            var element = new Noeud { Nom = nom, Parent = courant };
            foreach (Match a in Regex.Matches(m.Groups[3].Value, "([a-zA-Z-]+)\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)')"))
                element.Attributs[a.Groups[1].Value] = WebUtility.HtmlDecode(a.Groups[2].Success ? a.Groups[2].Value : a.Groups[3].Value);
            courant.Enfants.Add(element);
            if (!ElementsVides.Contains(nom) && !m.Groups[3].Value.TrimEnd().EndsWith('/'))
                courant = element;
        }
        return racine;
    }
}
