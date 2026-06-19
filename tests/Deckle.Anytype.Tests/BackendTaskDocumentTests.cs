using System.Xml;
using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

// Pins the Task Scheduler document for the headless backend. Its shape IS the
// frozen lifecycle contract (2026-06-19): a triggerless, non-elevated,
// on-demand, never-expiring task. These markers are asserted as literals on
// purpose — each one is the contract, not an incidental value that could re-tune.
[Trait("Category", "unit")]
public class BackendTaskDocumentTests
{
    private const string User = "MACHINE\\Louis";

    private static string Build(string exe = "C:\\Programs\\Deckle\\anytype.exe", string args = "serve") =>
        BackendTaskDocument.Build(new BackendProcessSpec(exe, args), User);

    [Fact]
    public void DocumentIsWellFormedXml()
    {
        // After escaping, the document must still parse — the load-bearing
        // guarantee that schtasks will accept it.
        var doc = new XmlDocument();
        doc.LoadXml(Build());
    }

    [Fact]
    public void HasNoTriggers()
    {
        // The whole autostart-honoring property rests on this: nothing starts the
        // backend on its own, only an explicit /Run.
        Assert.DoesNotContain("<Triggers", Build());
    }

    [Fact]
    public void RunsLeastPrivilegeInTheInteractiveSession()
    {
        string xml = Build();
        Assert.Contains("<RunLevel>LeastPrivilege</RunLevel>", xml);
        Assert.Contains("<LogonType>InteractiveToken</LogonType>", xml);
    }

    [Fact]
    public void AllowsOnDemandStartAndNeverExpires()
    {
        string xml = Build();
        Assert.Contains("<AllowStartOnDemand>true</AllowStartOnDemand>", xml);
        Assert.Contains("<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>", xml);
    }

    [Fact]
    public void CarriesTheQuotedCommandAndItsArguments()
    {
        string xml = Build(exe: "C:\\Programs\\Deckle\\anytype.exe", args: "serve --headless");
        Assert.Contains("<Command>\"C:\\Programs\\Deckle\\anytype.exe\"</Command>", xml);
        Assert.Contains("<Arguments>serve --headless</Arguments>", xml);
    }

    [Fact]
    public void OmitsTheArgumentsElementWhenThereAreNone()
    {
        // An empty <Arguments/> makes schtasks pass a stray empty argument, so the
        // element is dropped entirely when the spec has no arguments.
        string xml = Build(args: "");
        Assert.DoesNotContain("<Arguments>", xml);
    }

    [Fact]
    public void EscapesXmlMetacharactersInInjectedValues()
    {
        // An ampersand in a path would break the document if written raw; it must
        // come through escaped, and the document must still parse.
        string xml = Build(exe: "C:\\Tools & Bins\\anytype.exe", args: "serve");
        Assert.Contains("Tools &amp; Bins", xml);
        Assert.DoesNotContain("Tools & Bins", xml);

        var doc = new XmlDocument();
        doc.LoadXml(xml);
    }
}
