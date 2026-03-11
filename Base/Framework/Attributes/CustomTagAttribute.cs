using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Base.Core
{

    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class CustomTagAttribute : Attribute
    {
        public string ToolBaseVersion { get; }
    }
}
