namespace InterDetect
{
    /// <summary>
    /// Information required for the algorithm, and the interference score
    /// </summary>
    public class PrecursorInfo
    {
        // Ignore Spelling: workflow

        public double IsolationMass { get; }

        public double IsolationWidth { get; }

        public int ChargeState { get; private set; }

        /// <summary>
        /// Interference score: fraction of observed peaks that are from the precursor
        /// Larger is better, with a max of 1 and minimum of 0
        /// 1 means all peaks are from the precursor
        /// </summary>
        public double Interference { get; set; }

        /// <summary>
        /// The scan number of the product scan (NOT THE PRECURSOR SCAN NUMBER!)
        /// Used only for output
        /// </summary>
        public int ScanNumber { get; set; }

        /// <summary>
        /// Populated by the algorithm
        /// </summary>
        public double ActualMass { get; set; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="isolationMass">Parent ion m/z</param>
        /// <param name="isolationWidth">Isolation window (full width)</param>
        /// <param name="chargeState">Charge state; 0 if unknown</param>
        public PrecursorInfo(double isolationMass, double isolationWidth, int chargeState)
        {
            IsolationMass = isolationMass;
            IsolationWidth = isolationWidth;
            ChargeState = chargeState;
        }

        public void UpdateCharge(int newChargeState)
        {
            ChargeState = newChargeState;
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
        public int PrecursorScanNumber { get; set; }

        /// <summary>
        /// Populated by the algorithm, and output to text
        /// </summary>
        public double PrecursorIntensity { get; set; }

        /// <summary>
        /// Read from raw file and output to text
        /// </summary>
        public double IonCollectionTime { get; set; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="isolationMass">Parent ion m/z</param>
        /// <param name="isolationWidth">Isolation window (full width)</param>
        /// <param name="chargeState">Charge state; 0 if unknown</param>
        public PrecursorIntense(double isolationMass, double isolationWidth, int chargeState)
            : base(isolationMass, isolationWidth, chargeState)
        {
        }

        public override string ToString()
        {
            return string.Format("Precursor scan {0} for {1}", PrecursorScanNumber, base.ToString());
        }
    }
}
