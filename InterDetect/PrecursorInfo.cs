namespace InterDetect
{
    /// <summary>
    /// Information required for the algorithm, and the interference score
    /// </summary>
    public class PrecursorInfo
    {
        public double IsoloationMass;
        public int ChargeState;
        public int ScanNumber;
        public int PrecursorScanNumber;
        public double IsolationWidth;

        /// <summary>
        /// Interference score
        /// </summary>
        public double Interference;

        /// <summary>
        /// Populated by the algorithm
        /// </summary>
        public double ActualMass;
    }

    /// <summary>
    /// Additional data only output to text (not used in algorithm)
    /// </summary>
    public class PrecursorIntense : PrecursorInfo
    {
        public double PrecursorIntensity;
        public double IonCollectionTime;
    }
}
