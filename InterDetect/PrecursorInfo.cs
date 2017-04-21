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

    public class PrecursorIntense : PrecursorInfoTest
    {
        public double dPrecursorIntensity;
        public double ionCollectionTime;
    }
}
