namespace Deckle.Projects;

// How many files of the project have uncommitted changes: the entries of
// `git status --porcelain=v1 -z --untracked-files=all -- .`.
//
// `-- .` scopes the count to the project's folder when the project is a
// subfolder of a repository (without it git reports the whole repository).
// Untracked files count as uncommitted work: a new file that was never added
// is exactly what a person forgets. `-z` ends every entry with a NUL and
// leaves names unquoted, so a path with spaces or non-ASCII characters needs no
// unescaping.
public static class UncommittedFiles
{
    // The entries in the output. Each is "XY path"; a rename or a copy (X or Y
    // is R or C) is followed by one more NUL-ended field, the path it came
    // from, which is not an entry of its own.
    public static int Count(string porcelainZ)
    {
        int count = 0;
        string[] fields = porcelainZ.Split('\0');
        for (int i = 0; i < fields.Length; i++)
        {
            string field = fields[i];
            // The text ends with a NUL, which leaves an empty last field; a
            // real entry is at least "XY p".
            if (field.Length < 4) continue;
            count++;
            if (field[0] is 'R' or 'C' || field[1] is 'R' or 'C') i++;
        }
        return count;
    }
}
