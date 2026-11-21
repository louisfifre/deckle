using Deckle.FolderMap;

namespace Deckle.Projects;

// The names of the sample projects. Invented, except the product's own public
// name: the mockups show the maintainer's real project names, and none of them
// belongs in code, a fixture or a log.
public static class SampleProjectNames
{
    public const string Deckle = "deckle";
    public const string ScriptsKit = "scripts-kit";
    public const string HomeServer = "home-server";
    public const string UiToolkit = "ui-toolkit";
    public const string VoxelMods = "voxel-mods";
    public const string TerrainPreview = "terrain-preview";
    public const string CityMods = "city-mods";
    public const string PersonalSite = "personal-site";
    public const string TinyNotes = "tiny-notes";

    public const string Development = "Development";
    public const string Leisure = "Leisure";
}

public sealed record SampleProject(string Name, string Path, string? Category);

// What the sample world built: where, which projects, whether git was there to
// make the repositories. The walk reads projects by name from here.
public sealed class ProjectsSample
{
    internal ProjectsSample(string root, IReadOnlyList<SampleProject> projects, bool gitAvailable)
    {
        Root = root;
        Projects = projects;
        GitAvailable = gitAvailable;
    }

    public string Root { get; }

    public IReadOnlyList<SampleProject> Projects { get; }

    // False when git is not on the machine: the sample's projects are then
    // plain folders, and the walk records the degraded readouts rather than
    // failing the run.
    public bool GitAvailable { get; }

    public string PathOf(string name) => Projects.Single(p => p.Name == name).Path;

    // Registers the sample in a store the way a person would have: the two
    // categories in order, the projects in each, two without a category, and the
    // actions pinned on the two projects that carry a script menu. Run on a
    // fresh store (a self-check's own data root), never on the maintainer's.
    public void Populate(ProjectStore store)
    {
        ProjectCategory development = store.CreateCategory(SampleProjectNames.Development);
        ProjectCategory leisure = store.CreateCategory(SampleProjectNames.Leisure);

        foreach (SampleProject project in Projects)
        {
            string? category = project.Category switch
            {
                SampleProjectNames.Development => development.Id,
                SampleProjectNames.Leisure => leisure.Id,
                _ => null,
            };
            ProjectEntry added = store.AddProject(project.Path, category).Project;

            if (project.Name == SampleProjectNames.Deckle)
            {
                store.Pin(added.Id, new PinnedAction(new ActionRef("build-run.1"), "Build & run", "build-run.ps1"));
                store.Pin(added.Id, new PinnedAction(new ActionRef("self-check.0", "Debug"), "Self-check", "self-check.ps1"));
                store.Pin(added.Id, new PinnedAction(new ActionRef("deploy-prod.0"), "Update prod", "deploy-prod.ps1"));
                store.SetMain(added.Id, new ActionRef("build-run.1"));
            }
            else if (project.Name == SampleProjectNames.HomeServer)
            {
                store.Pin(added.Id, new PinnedAction(new ActionRef("server-power.0"), "Server power", "server-power.ps1"));
                store.SetMain(added.Id, new ActionRef("server-power.0"));
            }
        }
    }
}

// Builds synthetic projects under a folder the caller owns (the self-check's
// data root), never anything from the person's disk. Each is a real folder; the
// ones that need it are real Git repositories made with the git command line,
// with one commit, tags, and the uncommitted files a card counts. The mockups'
// shapes are all here: a project with a main action, one with a version the
// manifests contradict (the P2 shape: a tag and two manifests that disagree, and
// no script menu), one with no tag, one that is not a repository.
//
//   Development   deckle, scripts-kit, home-server, ui-toolkit
//   Leisure       voxel-mods, terrain-preview, city-mods
//   no category   personal-site, tiny-notes
//
// Dates are fixed, so two runs photograph the same thing.
public static class ProjectsSampleWorld
{
    public const string FolderName = "sample-projects";

    // The tokens the two projects with agent files read as (the mockups' P1 and
    // P2): the text is padded until the map's own estimate gives exactly these.
    public const int DeckleAgentTokens = 1880;
    public const int VoxelModsAgentTokens = 1460;

    public static ProjectsSample Create(string parent)
    {
        string root = Path.Combine(parent, FolderName);
        DeleteTree(root);
        Directory.CreateDirectory(root);

        List<SampleProject> projects = [];
        SampleProject Make(string name, string? category, Action<string> build)
        {
            string path = Path.Combine(root, name);
            Directory.CreateDirectory(path);
            build(path);
            var project = new SampleProject(name, path, category);
            projects.Add(project);
            return project;
        }

        Make(SampleProjectNames.Deckle, SampleProjectNames.Development, BuildDeckle);
        Make(SampleProjectNames.ScriptsKit, SampleProjectNames.Development, path =>
        {
            Write(path, "README.md", "# Scripts kit\n\nSample scripts.\n");
            SampleGit.InitializeAndCommit(path, "v0.5.0", "v0.6.0");
        });
        Make(SampleProjectNames.HomeServer, SampleProjectNames.Development, path =>
        {
            Write(path, "README.md", "# Home server\n\nNotes on a sample server.\n");
            Write(path, Path.Combine("scripts", "menu.ps1"), SampleManifests.MenuStub(
                ("server-power.0", "Server power", "server-power.ps1", ""),
                ("server-status.0", "Server status", "server-status.ps1", "")));
            SampleGit.InitializeAndCommit(path);
        });
        Make(SampleProjectNames.UiToolkit, SampleProjectNames.Development, path =>
        {
            Write(path, "README.md", "# UI toolkit\n\nA sample component library.\n");
            SampleGit.InitializeAndCommit(path, "v0.4.0");
        });

        Make(SampleProjectNames.VoxelMods, SampleProjectNames.Leisure, BuildVoxelMods);
        Make(SampleProjectNames.TerrainPreview, SampleProjectNames.Leisure, path =>
        {
            Write(path, "README.md", "# Terrain preview\n\nA sample viewer.\n");
            SampleGit.InitializeAndCommit(path, "v0.18.0", "v0.19.0");
        });
        Make(SampleProjectNames.CityMods, SampleProjectNames.Leisure, path =>
        {
            Write(path, "README.md", "# City mods\n\nSample mods.\n");
            SampleGit.InitializeAndCommit(path);
        });

        Make(SampleProjectNames.PersonalSite, null, path =>
        {
            // Not a repository: "Uncommitted" reads "Not a Git repository", and
            // the manifest still gives its version.
            Write(path, "index.html", "<!doctype html><title>Sample</title>\n");
            Write(path, "package.json", SampleManifests.PackageJson("personal-site", "1.0.0"));
        });
        Make(SampleProjectNames.TinyNotes, null, path =>
        {
            Write(path, "notes.md", "# Notes\n\nOne line.\n");
            SampleGit.InitializeAndCommit(path, "v0.1.0");
            if (!SampleGit.IsAvailable) return;
            Write(path, "notes.md", "# Notes\n\nTwo lines.\n");
            Write(path, "todo.md", "- sample\n");
        });

        return new ProjectsSample(root, projects, SampleGit.IsAvailable);
    }

    // The project with a main action, two pinned actions, a remote, three
    // uncommitted files and the estimate of P1.
    private static void BuildDeckle(string path)
    {
        Write(path, "README.md", "# Deckle sample\n\nA sample project.\n");
        Write(path, "AGENTS.md", "# Agents\n\nBuild with scripts/build-run.ps1, then check the result.\n");
        Write(path, "CLAUDE.md", "@AGENTS.md\n");
        Write(path, Path.Combine(".claude", "skills", "ship-release", "SKILL.md"),
            SampleManifests.SkillFile("ship-release", "Bumps the version and publishes a release."));
        Write(path, Path.Combine(".claude", "skills", "write-notes", "SKILL.md"),
            SampleManifests.SkillFile("write-notes", "Writes the notes of the day in the journal."));
        Write(path, Path.Combine("scripts", "menu.ps1"), SampleManifests.MenuStub(
            ("build-run.1", "Build & run", "build-run.ps1", ""),
            ("self-check.0", "Self-check", "self-check.ps1", "Debug"),
            ("self-check.0", "Self-check", "self-check.ps1", "Release"),
            ("deploy-prod.0", "Update prod", "deploy-prod.ps1", "")));
        PadAgentContext(path, DeckleAgentTokens);

        SampleGit.InitializeAndCommit(path, "v0.130.0", "v0.131.0");
        if (!SampleGit.IsAvailable) return;
        SampleGit.Run(path, "remote", "add", "origin", "https://github.com/sample/deckle.git");

        // One modified and two untracked: "3 uncommitted".
        Write(path, "README.md", "# Deckle sample\n\nA sample project, edited.\n");
        Write(path, "scratch.txt", "x\n");
        Write(path, "draft.md", "x\n");
    }

    // The P2 shape: a tag without a "v" that two manifests disagree with, nothing
    // uncommitted, no script menu.
    private static void BuildVoxelMods(string path)
    {
        Write(path, "README.md", "# Voxel mods\n\nSample mods.\n");
        Write(path, "AGENTS.md", "# Agents\n\nEach part has its own version.\n");
        Write(path, "CLAUDE.md", "@AGENTS.md\n");
        Write(path, Path.Combine("launcher", "app", "Cargo.toml"), SampleManifests.CargoManifest("launcher", "0.1.0"));
        Write(path, Path.Combine("mods", "voice", "gradle.properties"), SampleManifests.GradleProperties("voice", "0.1.0"));
        PadAgentContext(path, VoxelModsAgentTokens);

        SampleGit.InitializeAndCommit(path, "0.31.0", "0.32.0");
    }

    // Grows AGENTS.md until the map's own estimate of what an agent reads at the
    // start of a session gives exactly the target: the same count the page shows,
    // measured by the code the page uses. Each pass adds or removes the characters
    // the missing tokens stand for (four a token) and measures again, so whatever
    // the map counts twice or not at all, the loop settles on the target.
    private static void PadAgentContext(string root, int targetTokens)
    {
        string path = Path.Combine(root, "AGENTS.md");
        File.AppendAllText(path, "\n");
        for (int pass = 0; pass < 6; pass++)
        {
            int difference = targetTokens - MapInventory.Build(root).EstimatedStartupTokens;
            if (difference == 0) return;

            int characters = Math.Abs(difference) * TokenEstimate.CharactersPerToken;
            if (difference > 0)
            {
                File.AppendAllText(path, Filler(characters));
            }
            else
            {
                string text = File.ReadAllText(path);
                File.WriteAllText(path, text[..Math.Max(0, text.Length - characters)]);
            }
        }
    }

    // Text of exactly this many characters, a line break every 80.
    private static string Filler(int length)
    {
        const string sentence = "Keep the context short and the rules few. ";
        var text = new System.Text.StringBuilder(length);
        for (int i = 0; i < length; i++)
            text.Append((i + 1) % 80 == 0 ? '\n' : sentence[i % sentence.Length]);
        return text.ToString();
    }
    private static void Write(string root, string relative, string content)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.ReplaceLineEndings("\n"));
    }

    // A git repository's objects are read-only files: clear the attribute before
    // deleting, as a plain recursive delete would stop on them.
    private static void DeleteTree(string root)
    {
        if (!Directory.Exists(root)) return;
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            try { File.SetAttributes(file, FileAttributes.Normal); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        Directory.Delete(root, recursive: true);
    }
}
