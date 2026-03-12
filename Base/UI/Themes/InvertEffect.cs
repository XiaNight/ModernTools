using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Base.Helpers
{
    class InvertEffect : ShaderEffect
    {
        private static readonly PixelShader _shader =
            new PixelShader
            {
                UriSource = new Uri(
                    "pack://application:,,,/Base;component/Helpers/InvertEffect.ps",
                    UriKind.Absolute)
            };

        public InvertEffect()
        {
            PixelShader = _shader;
            UpdateShaderValue(InputProperty);
        }

        public Brush Input
        {
            get { return (Brush)GetValue(InputProperty); }
            set { SetValue(InputProperty, value); }
        }

        public static readonly DependencyProperty InputProperty =
            ShaderEffect.RegisterPixelShaderSamplerProperty("Input", typeof(InvertEffect), 0);
    }
}
