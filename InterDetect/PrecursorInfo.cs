using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace InterDetect
{
    public class PrecursorInfo
    {
        public double dIsoloationMass;
        public int nChargeState;
        public int nScanNumber;
        public int preScanNumber;
        public double isolationwidth;
        public double interference;

    }

    public class PrecursorInfoTest : PrecursorInfo
    {
        public double dActualMass;
    }
}
