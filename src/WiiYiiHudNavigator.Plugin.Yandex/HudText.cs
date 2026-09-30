using System.Text;

namespace WiiYiiHudNavigator.Plugin.Yandex;

internal static class HudText
{
    // The installed host converts street text to GB2312; HUD firmware fonts vary.
    // ASCII is byte-compatible and never turns Cyrillic/punctuation into CJK glyphs.
    internal static string Encode(string? text, bool latin = true)
    {
        const string letters = "абвгдеёжзийклмнопрстуфхцчшщъыьэюя";
        string[] replacements = ["a","b","v","g","d","e","yo","zh","z","i","y","k","l","m","n","o","p","r","s","t","u","f","kh","ts","ch","sh","shch","","y","","e","yu","ya"];
        var result = new StringBuilder();
        foreach (char c in text ?? "")
        {
            string part;
            int index = letters.IndexOf(char.ToLowerInvariant(c));
            if (latin && index >= 0)
            {
                part = replacements[index];
                if (char.IsUpper(c) && part.Length > 0) part = char.ToUpperInvariant(part[0]) + part[1..];
            }
            else if (c is >= ' ' and <= '~' || !latin && index >= 0) part = c.ToString();
            else part = c switch
            {
                '−' or '–' or '—' => "-", '«' or '»' or '“' or '”' => "\"",
                '’' or '‘' => "'", _ => " "
            };
            foreach (char item in part)
            {
                if (item == ' ' && (result.Length == 0 || result[result.Length - 1] == ' ')) continue;
                if (result.Length >= 64) break;
                result.Append(item);
            }
        }
        var encoded = result.ToString().Trim();
        return encoded.Length == 0 ? "-" : encoded;
    }
}
