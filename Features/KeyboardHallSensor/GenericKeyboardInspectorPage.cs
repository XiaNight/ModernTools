using Base.Core;
using Base.Pages;
using Base.Services;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace KeyboardHallSensor;

[PageInfo("Key Inspector",
    Glyph = "\uE765",
    Description = "Shows which key is pressed/released and counts clicks per key. Works with any keyboard — no device connection required.",
    NavOrder = 50,
    Path = ["Keyboard"],
    ShowDeviceSelection = false)]
public class GenericKeyboardInspectorPage : PageBase
{
    private Canvas _canvas;
    private TextBlock _statusText;

    private readonly Dictionary<string, KeyDisplay> _displayByLabel = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _clickCounts = new(StringComparer.Ordinal);

    private static readonly Brush _pressedBorder;
    private static readonly Brush _releasedBorder;
    static GenericKeyboardInspectorPage()
    {
        // Find color in resource
        var pressed = (Brush)Application.Current.TryFindResource("SystemControlHighlightAccentBrush");
        _pressedBorder = pressed;

        var released = (Brush)Application.Current.TryFindResource("SystemControlForegroundBaseLowBrush");
        _releasedBorder = released;
    }

    public override void Awake()
    {
        base.Awake();
        BuildUI();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        Main.PreviewKeyDown += OnPreviewKeyDown;
        Main.PreviewKeyUp   += OnPreviewKeyUp;
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        Main.PreviewKeyDown -= OnPreviewKeyDown;
        Main.PreviewKeyUp   -= OnPreviewKeyUp;
    }

    // ── UI construction ──────────────────────────────────────────────────

    private void BuildUI()
    {
        var outer = new Grid();
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Top bar
        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(8, 8, 8, 4)
        };

        _statusText = new TextBlock
        {
            Text = "Press any key…",
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 24, 0),
            MinWidth = 300
        };
        bar.Children.Add(_statusText);

        var resetBtn = new Button
        {
            Content = "Reset Counters",
            Padding = new Thickness(14, 5, 14, 5)
        };
        resetBtn.Click += (_, _) => ResetAll();
        bar.Children.Add(resetBtn);

        Grid.SetRow(bar, 0);
        outer.Children.Add(bar);

        // Scrollable canvas for keyboard layout
        _canvas = new Canvas { Margin = new Thickness(8) };
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
            Content = _canvas
        };
        Grid.SetRow(scroll, 1);
        outer.Children.Add(scroll);

        root.Children.Add(outer);
        BuildKeyboard();
    }

    private void BuildKeyboard()
    {
        var keyDefs = LayoutConverter.Convert();
        const float unit = 50f;
        float maxX = 0, maxY = 0;

        foreach (var def in keyDefs)
        {
            float px = def.X * unit;
            float py = def.Y * unit;
            float pw = def.W * unit;
            float ph = def.H * unit;

            var display = new KeyDisplay(0, pw, ph, def.Label);
            Canvas.SetLeft(display, px);
            Canvas.SetTop(display, py);
            _canvas.Children.Add(display);
            //display.ShowLabel();
            display.SetText("0");
            display.SetBorderColor(_releasedBorder);

            _displayByLabel[def.Label] = display;
            _clickCounts[def.Label]    = 0;

            maxX = Math.Max(maxX, px + pw);
            maxY = Math.Max(maxY, py + ph);
        }

        _canvas.Width  = maxX + 8;
        _canvas.Height = maxY + 8;
    }

    // ── Key event handlers ───────────────────────────────────────────────

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.IsRepeat) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (!_keyToLabel.TryGetValue(key, out var label)) return;

        _clickCounts[label] = _clickCounts.GetValueOrDefault(label) + 1;

        if (_displayByLabel.TryGetValue(label, out var d))
        {
            d.SetBorderColor(_pressedBorder);
            d.SetText(_clickCounts[label].ToString());
        }

        _statusText.Text = $"▼  {label.Replace("\n", " / ")}   ×{_clickCounts[label]}";
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (!_keyToLabel.TryGetValue(key, out var label)) return;
        
        if (_displayByLabel.TryGetValue(label, out var d))
            d.SetBorderColor(_releasedBorder);

        _statusText.Text = $"▲  {label.Replace("\n", " / ")}";
    }

    private void ResetAll()
    {
        foreach (var (label, display) in _displayByLabel)
        {
            _clickCounts[label] = 0;
            display.SetText("0");
            display.SetBorderColor(_releasedBorder);
        }
        _statusText.Text = "Press any key…";
    }

    // ── WPF Key → layout label mapping ──────────────────────────────────
    // Labels match keyboard_layout.txt exactly (including \n for multi-line keys)

    private static readonly Dictionary<Key, string> _keyToLabel = new()
    {
        { Key.Escape,          "Esc"          },
        { Key.F1,              "F1"           }, { Key.F2,  "F2"  }, { Key.F3,  "F3"  }, { Key.F4,  "F4"  },
        { Key.F5,              "F5"           }, { Key.F6,  "F6"  }, { Key.F7,  "F7"  }, { Key.F8,  "F8"  },
        { Key.F9,              "F9"           }, { Key.F10, "F10" }, { Key.F11, "F11" }, { Key.F12, "F12" },
        { Key.PrintScreen,     "PrtSc"        },
        { Key.Scroll,          "Scroll Lock"  },
        { Key.Pause,           "Pause\\nBreak" },

        // Number row
        { Key.OemTilde,        "~\\n`"         },
        { Key.D1,              "!\\n1"         }, { Key.D2, "@\\n2" }, { Key.D3, "#\\n3" },
        { Key.D4,              "$\\n4"         }, { Key.D5, "%\\n5" }, { Key.D6, "^\\n6" },
        { Key.D7,              "&\\n7"         }, { Key.D8, "*\\n8" }, { Key.D9, "(\\n9" }, { Key.D0, ")\\n0" },
        { Key.OemMinus,        "_\\n-"         },
        { Key.OemPlus,         "+\\n="         },
        { Key.Back,            "bksp"         },

        // Top row
        { Key.Tab,             "Tab"          },
        { Key.Q,  "Q" }, { Key.W, "W" }, { Key.E, "E" }, { Key.R, "R" }, { Key.T, "T" },
        { Key.Y,  "Y" }, { Key.U, "U" }, { Key.I, "I" }, { Key.O, "O" }, { Key.P, "P" },
        { Key.OemOpenBrackets, "{\\n["         },
        { Key.OemCloseBrackets,"}\\n]"         },
        { Key.OemPipe,         "|\\n\\"        },

        // Home row
        { Key.CapsLock,        "Caps Lock"    },
        { Key.A, "A" }, { Key.S, "S" }, { Key.D, "D" }, { Key.F, "F" }, { Key.G, "G" },
        { Key.H, "H" }, { Key.J, "J" }, { Key.K, "K" }, { Key.L, "L" },
        { Key.OemSemicolon,    ":\\n;"         },
        { Key.OemQuotes,       "\"\\n'"        },
        { Key.Return,          "Enter"        },

        // Bottom row
        { Key.LeftShift,       "L-Shift"      },
        { Key.Z, "Z" }, { Key.X, "X" }, { Key.C, "C" }, { Key.V, "V" },
        { Key.B, "B" }, { Key.N, "N" }, { Key.M, "M" },
        { Key.OemComma,        "<\\n,"         },
        { Key.OemPeriod,       ">\\n."         },
        { Key.OemQuestion,     "?\\n/"         },
        { Key.RightShift,      "R-Shift"      },

        // Modifier row
        { Key.LeftCtrl,        "L-Ctrl"       },
        { Key.LWin,            "L-Win"        },
        { Key.LeftAlt,         "L-Alt"        },
        { Key.Space,           ""             },
        { Key.RightAlt,        "R-Alt"        },
        { Key.RWin,            "R-Win"        },
        { Key.Apps,            "Menu"         },
        { Key.RightCtrl,       "R-Ctrl"       },

        // Navigation cluster
        { Key.Insert,          "Insert"       },
        { Key.Home,            "Home"         },
        { Key.PageUp,          "PgUp"         },
        { Key.Delete,          "Delete"       },
        { Key.End,             "End"          },
        { Key.PageDown,        "PgDn"         },

        // Arrow keys — layout uses Unicode arrows
        { Key.Up,              "↑"            },
        { Key.Left,            "←"            },
        { Key.Down,            "↓"            },
        { Key.Right,           "→"            },

        // Numpad
        { Key.NumLock,         "Num Lock"     },
        { Key.Divide,          "/"            },
        { Key.Multiply,        "*"            },
        { Key.Subtract,        "-"            },
        { Key.Add,             "+"            },
        { Key.NumPad7,         "7\\nHome"      }, { Key.NumPad8, "8\\n↑"   }, { Key.NumPad9, "9\\nPgUp" },
        { Key.NumPad4,         "4\\n←"         }, { Key.NumPad5, "5"      }, { Key.NumPad6, "6\\n→"    },
        { Key.NumPad1,         "1\\nEnd"       }, { Key.NumPad2, "2\\n↓"   }, { Key.NumPad3, "3\\nPgDn" },
        { Key.NumPad0,         "0\\nIns"       },
        { Key.Decimal,         ".\\nDel"       },
    };
}
