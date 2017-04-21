namespace InterDetect
{
    /// <summary>
    /// Information required for the algorithm, and the interference score
    /// </summary>
    public class PrecursorInfo
    {
        public double IsolationMass;
        public int ChargeState;
        /// <summary>
        /// The scan number of the product scan (NOT THE PRECURSOR SCAN NUMBER!)
        /// </summary>
        public int ScanNumber;
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
    }
}
