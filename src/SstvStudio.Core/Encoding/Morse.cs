// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using SstvStudio.Core.Audio;

namespace SstvStudio.Core.Encoding;

public static class Morse
{
    private static readonly Dictionary<char, string> Table = new()
    {
        ['A'] = ".-", ['B'] = "-...", ['C'] = "-.-.", ['D'] = "-..", ['E'] = ".", ['F'] = "..-.",
        ['G'] = "--.", ['H'] = "....", ['I'] = "..", ['J'] = ".---", ['K'] = "-.-", ['L'] = ".-..",
        ['M'] = "--", ['N'] = "-.", ['O'] = "---", ['P'] = ".--.", ['Q'] = "--.-", ['R'] = ".-.",
        ['S'] = "...", ['T'] = "-", ['U'] = "..-", ['V'] = "...-", ['W'] = ".--", ['X'] = "-..-",
        ['Y'] = "-.--", ['Z'] = "--..",
        ['0'] = "-----", ['1'] = ".----", ['2'] = "..---", ['3'] = "...--", ['4'] = "....-",
        ['5'] = ".....", ['6'] = "-....", ['7'] = "--...", ['8'] = "---..", ['9'] = "----.",
        ['/'] = "-..-.", ['?'] = "..--..", ['.'] = ".-.-.-", [','] = "--..--", ['='] = "-...-",
    };

    /// <summary>PARIS timing: dot = 1200/wpm ms, dash = 3 dots, letter gap = 3, word gap = 7.</summary>
    public static IEnumerable<Tone> Encode(string text, int wpm, double toneHz)
    {
        var dot = 1200.0 / Math.Clamp(wpm, 5, 60);
        var words = text.ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        for (var w = 0; w < words.Length; w++)
        {
            if (w > 0) yield return Tone.Silence(dot * 7);
            var letters = words[w].Where(Table.ContainsKey).ToArray();
            for (var l = 0; l < letters.Length; l++)
            {
                if (l > 0) yield return Tone.Silence(dot * 3);
                var code = Table[letters[l]];
                for (var e = 0; e < code.Length; e++)
                {
                    if (e > 0) yield return Tone.Silence(dot);
                    yield return new Tone(toneHz, code[e] == '.' ? dot : dot * 3);
                }
            }
        }
    }
}
