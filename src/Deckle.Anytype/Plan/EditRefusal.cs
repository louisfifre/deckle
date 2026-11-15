using System.Net;
using System.Net.Http;

namespace Deckle.Anytype;

// An edit of something the space already held, which the server refuses to the
// bot's key. On a space a person created, the bot joins as an editor: it may
// create properties, types and objects, but Anytype answers 403 when it renames
// a native property the owner's space shipped with (PATCH /properties/{due_date}
// « failed to update property », while POST /properties passes; measured on
// « PM trial 2026-11-15 », 2026-11-15). The same refusal already met collections,
// queries and views of native types the bot did not create.
//
// Such a refusal blocks none of the other writes, so the run says it and goes
// on; the next preview plans the edit again, so a check keeps naming it until
// the owner makes it in the app. A creation refused, or any other failure (a
// 5xx, a dropped connection), still stops the run: the space would otherwise
// miss what later steps build on, and a backend that is down never reads as a
// list of refusals.
internal static class EditRefusal
{
    public static bool Is(Exception ex) =>
        ex is HttpRequestException { StatusCode: HttpStatusCode.Forbidden };

    public static string Line(string what) =>
        $"refusé (403) : {what} — à faire dans l'app par le propriétaire de l'espace";

    // Runs one edit; a refusal is logged instead of thrown. True when it was written.
    public static async Task<bool> TryAsync(Func<Task> edit, string what, List<string> log)
    {
        try
        {
            await edit().ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (Is(ex))
        {
            log.Add(Line(what));
            return false;
        }
    }
}
