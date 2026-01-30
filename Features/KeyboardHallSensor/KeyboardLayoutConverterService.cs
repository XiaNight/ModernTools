using System.IO;
using System.Text.RegularExpressions;


namespace Base.Services
{
    public partial class LayoutConverter
    {
        public static List<KeyDef> Convert()
        {
            string path = Path.Combine(MainWindow.GetConfigFolder(), "keyboard_layout.txt");
            string kleRaw = File.ReadAllText(path);

            var matches = RowRegex().Matches(kleRaw);

            float Y = 0;

            var keyDefs = new List<KeyDef>();
            foreach (Match match in matches)
            {
                Y = ParseRow(keyDefs, match.Value, Y) + 1;
            }

            return keyDefs;
        }
        private static float ParseRow(List<KeyDef> list, string raw, float Y)
        {
            float X = 0;
            var matches = SlotRegex().Matches(raw);

            string modifier = null;

            foreach (Match match in matches)
            {
                var modpart = match.Groups[1].Value;
                var keypart = match.Groups[2].Value;

                if (!string.IsNullOrEmpty(modpart))
                {
                    modifier = modpart;
                    continue;
                }

                if (!string.IsNullOrEmpty(keypart))
                {
                    var label = keypart[1..^1];
                    var keyDef = new KeyDef
                    {
                        Label = label,
                        X = 0,
                        Y = 0,
                        W = 1,
                        H = 1
                    };

                    if (!string.IsNullOrEmpty(modifier))
                    {
                        var modValues = ModifierValueRegex().Matches(modifier);
                        foreach (Match modValue in modValues)
                        {
                            var modKey = modValue.Groups[1].Value;
                            var value = float.Parse(modValue.Groups[2].Value);

                            switch (modKey)
                            {
                                case "x":
                                    keyDef.X = value;
                                    break;
                                case "y":
                                    keyDef.Y = value;
                                    break;
                                case "w":
                                    keyDef.W = value;
                                    break;
                                case "h":
                                    keyDef.H = value;
                                    break;
                            }
                        }
                        modifier = null;
                    }

                    keyDef.X += X;
                    keyDef.Y += Y;

                    X = keyDef.X + keyDef.W;
                    Y = keyDef.Y;

                    list.Add(keyDef);
                }
                else
                {
                    Console.WriteLine($"Invalid key part: {keypart}");
                }
            }
            return Y;
        }


        [GeneratedRegex(@"(?:\[(.*?)\](?:,|\s))")]
        private static partial Regex RowRegex();

        [GeneratedRegex(@"(\{.*?\})|(\"".*?(?<=(^|[^\\])(\\\\)*)"")")]
        private static partial Regex SlotRegex();

        [GeneratedRegex(@"([xywha]):\s*(-?[0-9.]+)")]
        private static partial Regex ModifierValueRegex();

        public static readonly Dictionary<string, byte> keyLabelToCode = new()
        {
			// Function keys
			{"Esc", 0x29},
            {"F1", 0x3A}, {"F2", 0x3B}, {"F3", 0x3C}, {"F4", 0x3D},
            {"F5", 0x3E}, {"F6", 0x3F}, {"F7", 0x40}, {"F8", 0x41},
            {"F9", 0x42}, {"F10", 0x43}, {"F11", 0x44}, {"F12", 0x45},
            {"PrtSc", 0x46}, {"Scroll Lock", 0x47}, {"Pause\nBreak", 0x48},

			// Number row
			{"!\\n1", 0x1E}, {"@\\n2", 0x1F}, {"#\\n3", 0x20}, {"$\\n4", 0x21},
            {"%\\n5", 0x22}, {"^\\n6", 0x23}, {"&\\n7", 0x24}, {"*\\n8", 0x25},
            {"(\\n9", 0x26}, {")\\n0", 0x27},

			// Numpad (with labels)
			{"Num Lock", 0x53}, {"/", 0x54}, {"*", 0x55}, {"-", 0x56},
            {"7\\nHome", 0x5F}, {"8\\n↑", 0x60}, {"9\\nPgUp", 0x61},
            {"4\\n←", 0x5C}, {"5", 0x5D}, {"6\\n→", 0x5E},
            {"+", 0x57}, {"1\\nEnd", 0x59}, {"2\\n↓", 0x5A}, {"3\\nPgDn", 0x5B},
            {"0\\nIns", 0x62}, {".\\nDel", 0x63}, {"Num-Enter", 0x58},

			// Alphabet
			{"A", 0x04}, {"B", 0x05}, {"C", 0x06}, {"D", 0x07}, {"E", 0x08},
            {"F", 0x09}, {"G", 0x0A}, {"H", 0x0B}, {"I", 0x0C}, {"J", 0x0D},
            {"K", 0x0E}, {"L", 0x0F}, {"M", 0x10}, {"N", 0x11}, {"O", 0x12},
            {"P", 0x13}, {"Q", 0x14}, {"R", 0x15}, {"S", 0x16}, {"T", 0x17},
            {"U", 0x18}, {"V", 0x19}, {"W", 0x1A}, {"X", 0x1B}, {"Y", 0x1C}, {"Z", 0x1D},

			// Special characters
			{"~\\n`", 0x35}, {"_\\n-", 0x2D}, {"+\\n=", 0x2E}, {"jp-bp", 0x89},
            {"{\\n[", 0x2F}, {"}\\n]", 0x30}, {"|\\n\\\\", 0x31}, {"#\\n~", 0x32},
            {"\\\\\\n|", 0x64}, {":\\n;", 0x33}, {"\\\"\\n'", 0x34}, {"<\\n,", 0x36}, {">\\n.", 0x37}, {"?\\n/", 0x38},

			// More function/control keys
			{"bksp", 0x2A}, {"Tab", 0x2B}, {"Caps Lock", 0x39}, {"Enter", 0x28},
            {"L-Shift", 0xE1}, {"L-Ctrl", 0xE0}, {"L-Alt", 0xE2}, {"L-Win", 0xE3},
            {"Menu", 0x65},
            {"R-Ctrl", 0xE4}, {"R-Shift", 0xE5}, {"R-Alt", 0xE6}, {"R-Win", 0xE7},
            {"Fn", 0xE8},

			// Navigation
			{"Insert", 0x49}, {"Home", 0x4A}, {"PgUp", 0x4B},
            {"Delete", 0x4C}, {"End", 0x4D}, {"PgDn", 0x4E},
            {"↑", 0x52}, {"←", 0x50}, {"↓", 0x51}, {"→", 0x4F},

			// Spacebar
			{"", 0x2C}
        };
    }
    public struct KeyDef
    {
        public string Label { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float W { get; set; }
        public float H { get; set; }
    }
}