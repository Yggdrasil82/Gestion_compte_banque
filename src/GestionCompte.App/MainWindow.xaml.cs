using System.Windows;

namespace GestionCompte.App;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void Quitter_Click(object sender, RoutedEventArgs e) => Close();
}
