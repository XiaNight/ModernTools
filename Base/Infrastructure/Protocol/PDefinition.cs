using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Base.Infrastructure.Protocol
{
    public class PDefinition
    {
	    public byte Command { get; }
	    public  byte Key { get; }
	    public  short Index { get; } = 0;

        public PSegmentDefinition root { get; }
    }
}
