// Les projets WPF n'importent pas System.IO implicitement (conflit avec System.Windows.Shapes.Path).
global using System.IO;

// Le projet de tests (selftest) accède aux détecteurs internes.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("selftest")]
