using System.IO;
using System.Reflection;

namespace GestionCompte.App;

/// <summary>
/// Étapes du démarrage notées dans %LOCALAPPDATA%\GestionCompte\demarrage.log (le lancement précédent dans
/// demarrage-precedent.log) : si la fenêtre ne s'ouvre pas, le journal dit où le démarrage s'est arrêté. Un fichier
/// vide ou absent alors que le processus tourne signifie que l'application a été retenue avant même de démarrer.
/// </summary>
internal static class JournalDemarrage
{
    private static readonly object Verrou = new();
    private static string? _chemin;
    private static string _derniere = "";
    private static bool _termine;

    /// <summary>Emplacement du journal (vide s'il n'a pas pu être créé).</summary>
    public static string Chemin => _chemin ?? "";

    /// <summary>Commence un nouveau journal et surveille le démarrage (étape notée si la fenêtre tarde).</summary>
    public static void Commencer()
    {
        try
        {
            var dossier = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GestionCompte");
            Directory.CreateDirectory(dossier);
            _chemin = Path.Combine(dossier, "demarrage.log");
            if (File.Exists(_chemin))
                File.Copy(_chemin, Path.Combine(dossier, "demarrage-precedent.log"), overwrite: true);
            File.WriteAllText(_chemin, "");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _chemin = null;
        }

        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        Noter($"Lancement de la version {version} depuis {Environment.ProcessPath} (Windows {Environment.OSVersion.Version})");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Noter($"Erreur fatale : {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) => Noter($"Erreur en tâche de fond : {e.Exception}");

        // Fenêtre toujours pas affichée : la dernière étape est rappelée (elle est celle qui bloque).
        new Thread(() =>
        {
            var attendu = 0;
            foreach (var secondes in new[] { 15, 60 })
            {
                Thread.Sleep(TimeSpan.FromSeconds(secondes - attendu));
                attendu = secondes;
                lock (Verrou)
                    if (_termine)
                        return;
                Noter($"Fenêtre toujours pas affichée après {secondes} s ; dernière étape : {_derniere}");
            }
        }) { IsBackground = true, Name = "Surveillance du démarrage" }.Start();
    }

    /// <summary>Note une étape (heure à la milliseconde).</summary>
    public static void Noter(string etape)
    {
        lock (Verrou)
        {
            _derniere = etape;
            if (_chemin is null)
                return;
            try
            {
                File.AppendAllText(_chemin, $"{DateTime.Now:HH:mm:ss.fff}  {etape}{Environment.NewLine}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Journal facultatif : le démarrage continue.
            }
        }
    }

    /// <summary>Fenêtre affichée : fin de la surveillance.</summary>
    public static void Terminer(string etape)
    {
        Noter(etape);
        lock (Verrou)
            _termine = true;
    }
}
