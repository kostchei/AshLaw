namespace Ash.Rules;

internal static class CsvDocument
{
    public static IReadOnlyList<IReadOnlyList<string>> Parse(string text, string sourceName)
    {
        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var field = new System.Text.StringBuilder();
        var inQuotes = false;
        var fieldStarted = false;

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];

            if (inQuotes)
            {
                if (character == '"')
                {
                    if (index + 1 < text.Length && text[index + 1] == '"')
                    {
                        field.Append('"');
                        index++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(character);
                }

                continue;
            }

            switch (character)
            {
                case '"' when !fieldStarted && field.Length == 0:
                    inQuotes = true;
                    fieldStarted = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    fieldStarted = false;
                    break;
                case '\r':
                    if (index + 1 < text.Length && text[index + 1] == '\n')
                    {
                        index++;
                    }

                    CompleteRow(rows, row, field);
                    fieldStarted = false;
                    break;
                case '\n':
                    CompleteRow(rows, row, field);
                    fieldStarted = false;
                    break;
                default:
                    field.Append(character);
                    fieldStarted = true;
                    break;
            }
        }

        if (inQuotes)
        {
            throw new RulesDataException($"{sourceName}: unterminated quoted CSV field.");
        }

        if (field.Length > 0 || row.Count > 0 || fieldStarted)
        {
            CompleteRow(rows, row, field);
        }

        if (rows.Count == 0)
        {
            throw new RulesDataException($"{sourceName}: CSV file is empty.");
        }

        return rows;
    }

    private static void CompleteRow(
        ICollection<IReadOnlyList<string>> rows,
        ICollection<string> row,
        System.Text.StringBuilder field)
    {
        row.Add(field.ToString());
        field.Clear();
        rows.Add(row.ToArray());
        row.Clear();
    }
}

