namespace InterDetect
{
    /// <summary>
    /// Information required for the algorithm, and the interference score
    /// </summary>
    public class PrecursorInfo
    {
        public double IsolationMass;
        public int ChargeState;
        public double IsolationWidth;

        /// <summary>
        /// Interference score - larger is better, with a max of 1 and minimum of 0
        /// </summary>
        public double Interference;

        /// <summary>
        /// The scan number of the product scan (NOT THE PRECURSOR SCAN NUMBER!)
        /// Used only for output
        /// </summary>
        public int ScanNumber;

        /// <summary>
        /// Populated by the algorithm
        /// </summary>
        public double ActualMass;

        public PrecursorInfo()
        {
        }

        public PrecursorInfo(double isolationMass, int chargeState, double isolationWidth)
        {
            IsolationMass = isolationMass;
            ChargeState = chargeState;
            IsolationWidth = isolationWidth;
        }
        public override string ToString()
        {
            return string.Format("MS2 scan {0} @ {1:F2} m/z, charge {2}", ScanNumber, IsolationMass, ChargeState);
        }
    }

    /// <summary>
    /// Additional data only used by the full workflow of InterferenceDetector, but not by InterferenceCalculator
    /// </summary>
    public class PrecursorIntense : PrecursorInfo
    {
        /// <summary>
        /// Used to read the precursor scan data for the scan
        /// </summary>
        public int PrecursorScanNumber;

        /// <summary>
        /// Populated by the algorithm, and output to text
        /// </summary>
        public double PrecursorIntensity;

        /// <summary>
        /// Read from raw file and output to text
        /// </summary>
        public double IonCollectionTime;

        public override string ToString()
        {
            return string.Format("Precursor scan {0} for {1}", PrecursorScanNumber, base.ToString());
        }
    }
}
