using System.Text.Json;
using Deckle.Core;

namespace Deckle.Autocorrect.Probe;

internal static class CaretContextProbeCommand
{
    public static int Run(ProbeArguments arguments)
    {
        Console.WriteLine(
            $"Focus the target text field. Read-only capture starts in {arguments.DelaySeconds} seconds.");
        for (int remaining = arguments.DelaySeconds; remaining > 0; remaining--)
        {
            Console.WriteLine($"  {remaining}...");
            Thread.Sleep(TimeSpan.FromSeconds(1));
        }

        ProbeCapture capture = CaptureOnMta(arguments.MaxCharacters);
        if (!capture.FirstSucceeded)
        {
            Console.Error.WriteLine($"Capture refused: {capture.FirstDiagnostic}");
            return 1;
        }

        FocusedCaretText first = capture.First;
        Console.WriteLine($"Target: {first.TargetIdentity}");
        Console.WriteLine(
            $"Provider: {first.Pattern}; moved={first.MovedCharacters}; "
            + $"document_start={first.ReachedDocumentStart}");

        if (!capture.SecondSucceeded)
        {
            Console.Error.WriteLine($"Verification refused: {capture.SecondDiagnostic}");
            return 1;
        }

        FocusedCaretText second = capture.Second;
        bool sameTarget = string.Equals(
            first.TargetIdentity,
            second.TargetIdentity,
            StringComparison.Ordinal);
        bool sameText = string.Equals(
            first.TextBeforeCaret,
            second.TextBeforeCaret,
            StringComparison.Ordinal);
        Console.WriteLine($"Verification: same_target={sameTarget}; same_text={sameText}");

        Console.WriteLine($"Raw suffix: {JsonSerializer.Serialize(first.TextBeforeCaret)}");

        CaretSentenceContextResult context = CaretSentenceContext.Extract(
            first.TextBeforeCaret,
            first.ReachedDocumentStart);
        Console.WriteLine(
            $"Sentence context: available={context.Available}; "
            + $"boundary={context.Boundary}; reason={context.Reason}");
        if (context.Available)
            Console.WriteLine($"Candidate: {JsonSerializer.Serialize(context.Text)}");

        return sameTarget && sameText ? 0 : 1;
    }

    private static ProbeCapture CaptureOnMta(int maxCharacters)
    {
        ProbeCapture capture = default;
        var thread = new Thread(() =>
        {
            bool firstSucceeded = UIAutomation.TryReadFocusedTextBeforeCaret(
                maxCharacters,
                out FocusedCaretText first,
                out string firstDiagnostic);

            Thread.Sleep(TimeSpan.FromMilliseconds(100));

            bool secondSucceeded = UIAutomation.TryReadFocusedTextBeforeCaret(
                maxCharacters,
                out FocusedCaretText second,
                out string secondDiagnostic);

            capture = new ProbeCapture(
                firstSucceeded,
                first,
                firstDiagnostic,
                secondSucceeded,
                second,
                secondDiagnostic);
        });
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        thread.Join();
        return capture;
    }

    private readonly record struct ProbeCapture(
        bool FirstSucceeded,
        FocusedCaretText First,
        string FirstDiagnostic,
        bool SecondSucceeded,
        FocusedCaretText Second,
        string SecondDiagnostic);
}
