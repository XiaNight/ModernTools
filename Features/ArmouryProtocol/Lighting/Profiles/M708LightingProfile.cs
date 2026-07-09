namespace ArmouryProtocol.Lighting.Profiles;

/// <summary>
/// Lighting profile for the M708 keyboard (currently under test).
///
/// The <see cref="MatrixLightKeyTable"/> mirrors the M708 firmware light-key
/// table, and <c>M708LayoutToMatrix</c> maps the KLE labels from "M708.txt" onto
/// those matrix keys. Both are M708 specific; other models get their own profile.
/// </summary>
public sealed class M708LightingProfile : KeyboardLightingProfile
{
    public override string ModelName => "M708";

    public override string LayoutFileName => "M708.txt";

    public override string[,] MatrixLightKeyTable => M708MatrixLightKeyTable;

    protected override IReadOnlyDictionary<string, string> LayoutToMatrix => M708LayoutToMatrix;

    // Matrix is stored with x on the first axis and y on the second.
    // An empty string marks a cell with no addressable key.
    private static readonly string[,] M708MatrixLightKeyTable =
    {
        {"L_BAR1",  "L_BAR2",   "L_BAR3",       "L_BAR4",       "L_BAR5",       "L_BAR6"},
        {"L_BAR7",  "L_BAR8",   "L_BAR9",       "",             "",             ""},
        {"ESC",     "TILDE",    "TAB",          "CAP",          "L_SHIFT",      "L_CTRL"},
        {"F1",      "1",        "Q",            "A",            "CODE45(EU)",   "L_WIN"},
        {"F2",      "2",        "W",            "S",            "Z",            "L_ALT"},
        {"F3",      "3",        "E",            "D",            "X",            "SPACE1"},
        {"F4",      "4",        "R",            "F",            "C",            "SPACE2"},
        {"F5",      "5",        "T",            "G",            "V",            "SPACE3"},
        {"F6",      "6",        "Y",            "H",            "B",            "SPACE4"},
        {"F7",      "7",        "U",            "J",            "N",            "SPACE5"},
        {"F8",      "8",        "I",            "K",            "M",            ""},
        {"F9",      "9",        "O",            "L",            "COMMA",        "R_ALT"},
        {"F10",     "0",        "P",            "SEMICOLON",    "DOT",          "FN"},
        {"F11",     "MINUS",    "L_BRACKETS",   "APOSTROPHE",   "SLASH",        "R_CTRL"},
        {"F12",     "EQUAL",    "R_BRACKETS",   "CODE42(EU)",   "R_SHIFT",      "L_ARROW"},
        {"",        "BACKSPACE","BACKSLASH",    "ENTER",        "UP_ARROW",     "DN_ARROW"},
        {"",        "INSERT",   "DEL",          "PGUP",         "PGDN",         "R_ARROW"},
        {"R_BAR1",  "R_BAR2",   "R_BAR3",       "R_BAR4",       "R_BAR5",       "R_BAR6"},
        {"R_BAR7",  "R_BAR8",   "R_BAR9",       "",             "",             ""}
    };

    private static readonly Dictionary<string, string> M708LayoutToMatrix = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Esc"] = "ESC",
        ["~\\n`"] = "TILDE",
        ["Tab"] = "TAB",
        ["Caps Lock"] = "CAP",
        ["L-Shift"] = "L_SHIFT",
        ["L-Ctrl"] = "L_CTRL",
        ["F1"] = "F1",
        ["F2"] = "F2",
        ["F3"] = "F3",
        ["F4"] = "F4",
        ["F5"] = "F5",
        ["F6"] = "F6",
        ["F7"] = "F7",
        ["F8"] = "F8",
        ["F9"] = "F9",
        ["F10"] = "F10",
        ["F11"] = "F11",
        ["F12"] = "F12",

        ["!\\n1"] = "1",
        ["@\\n2"] = "2",
        ["#\\n3"] = "3",
        ["$\\n4"] = "4",
        ["%\\n5"] = "5",
        ["^\\n6"] = "6",
        ["&\\n7"] = "7",
        ["*\\n8"] = "8",
        ["(\\n9"] = "9",
        [")\\n0"] = "0",

        ["Q"] = "Q",
        ["W"] = "W",
        ["E"] = "E",
        ["R"] = "R",
        ["T"] = "T",
        ["Y"] = "Y",
        ["U"] = "U",
        ["I"] = "I",
        ["O"] = "O",
        ["P"] = "P",

        ["A"] = "A",
        ["S"] = "S",
        ["D"] = "D",
        ["F"] = "F",
        ["G"] = "G",
        ["H"] = "H",
        ["J"] = "J",
        ["K"] = "K",
        ["L"] = "L",

        ["Z"] = "Z",
        ["X"] = "X",
        ["C"] = "C",
        ["V"] = "V",
        ["B"] = "B",
        ["N"] = "N",
        ["M"] = "M",

        ["_\\n-"] = "MINUS",
        ["+\\n="] = "EQUAL",
        ["{\\n["] = "L_BRACKETS",
        ["}\\n]"] = "R_BRACKETS",
        ["|\\n\\\\"] = "BACKSLASH",
        [":\\n;"] = "SEMICOLON",
        ["\\\"\\n'"] = "APOSTROPHE",
        ["<\\n,"] = "COMMA",
        [">\\n."] = "DOT",
        ["?\\n/"] = "SLASH",

        ["\\\\\\n|"] = "CODE45(EU)",
        ["#\\n~"] = "CODE42(EU)",

        ["bksp"] = "BACKSPACE",
        ["Enter"] = "ENTER",
        ["Insert"] = "INSERT",
        ["Delete"] = "DEL",
        ["PgUp"] = "PGUP",
        ["PgDn"] = "PGDN",

        ["L-Win"] = "L_WIN",
        ["L-Alt"] = "L_ALT",
        ["R-Alt"] = "R_ALT",
        ["Fn"] = "FN",
        ["R-Ctrl"] = "R_CTRL",
        ["R-Shift"] = "R_SHIFT",

        ["↑"] = "UP_ARROW",
        ["↓"] = "DN_ARROW",
        ["←"] = "L_ARROW",
        ["→"] = "R_ARROW",

        ["Space"] = "SPACE1",
        [""] = "SPACE1"
    };
}
