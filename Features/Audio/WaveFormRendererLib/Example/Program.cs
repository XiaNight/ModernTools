using System;
using System.Windows.Forms;
using WaveFormRendererApp;

namespace Audio.WaveFormRendererLib.WaveformRenderer
{
    public class Program
    {
        public static void Start()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
