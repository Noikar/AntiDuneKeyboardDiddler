using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("AntiDuneKeyboardDiddler")]
[assembly: AssemblyDescription("Keeps a game from hijacking your keyboard layout.")]
[assembly: AssemblyProduct("AntiDuneKeyboardDiddler")]
[assembly: AssemblyCopyright("MIT licensed")]
[assembly: ComVisible(false)]

// Bump on release, and tag the repository to match. The update check compares this against
// the tag_name of the latest GitHub release, so a build stamped higher than the newest tag
// simply reports itself as up to date.
[assembly: AssemblyVersion("1.0.2.0")]
[assembly: AssemblyFileVersion("1.0.2.0")]
[assembly: AssemblyInformationalVersion("1.0.2")]
