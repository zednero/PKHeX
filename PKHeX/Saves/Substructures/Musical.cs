using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PKHeX.Core
{
    public class Musical
    {
        public Musical(byte[] data)
        {
            Data = (byte[])data.Clone();
        }

        private readonly byte[] Data;

        public byte[] getData()
        {
            return Data;
        }
    }
}
