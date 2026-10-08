namespace Deckle.Orchestration;

// What one appmanifest_<appid>.acf says about an installed Steam game.
// InstallDir is the manifest's own "installdir" token (a folder name);
// InstallDirectory is where that folder sits on disk:
// <library>\steamapps\common\<installdir>.
public sealed record SteamManifest(string AppId, string Name, string InstallDir, string InstallDirectory);
