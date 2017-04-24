using System;
using System.Collections.Generic;
using System.Linq;

namespace InterDetect
{
    /// <summary>
    /// Algorithm for calculating precursor interference
    /// </summary>
    public class InterferenceCalculator
    {
        private const double C12_C13_MASS_DIFFERENCE = 1.0033548378;
        private const int NumIsotopesToCheckChargeGuess = 2;
        private const double DataBufferChargeGuess = C12_C13_MASS_DIFFERENCE * (NumIsotopesToCheckChargeGuess + 1);

        /// <summary>
        /// Calculate the interference for the scan based on the provided data
        /// </summary>
        /// <param name="precursorInfo">Precursor info: must set IsolationMass, ChargeState, IsolationWidth</param>
        /// <param name="spectraData2D">Array of centroided peak data of size [2,x], where [0,0] is first m/z and [1,0] is first intensity</param>
        public static void Interference(PrecursorInfo precursorInfo, double[,] spectraData2D)
        {
            var mzToFindLow = precursorInfo.IsolationMass - (precursorInfo.IsolationWidth);
            var mzToFindHigh = precursorInfo.IsolationMass + (precursorInfo.IsolationWidth);

            if (precursorInfo.ChargeState <= 0)
            {
                // Make sure we cover the range needed to guess a charge
                mzToFindLow = Math.Min(mzToFindLow, precursorInfo.IsolationMass - DataBufferChargeGuess);
                mzToFindHigh = Math.Max(mzToFindHigh, precursorInfo.IsolationMass + DataBufferChargeGuess);
            }

            // Limit the number of peaks we convert to Peak objects to peaks that are within reasonable range of the isolation mass
            var min = 0;
            var max = spectraData2D.GetUpperBound(1) + 1;

            // Binary search for the low index
            var lowIndex = BinarySearch(ref spectraData2D, min, max, mzToFindLow);

            // TODO: Given the normal isolation widths, a linear search would likely be faster than the binary search
            // for finding the high index, when the starting point is the low index
            var highIndex = BinarySearch(ref spectraData2D, lowIndex, max, mzToFindHigh);

            //capture all peaks in isowidth+buffer
            var peaks = ConvertToPeaks(ref spectraData2D, lowIndex, highIndex);

            // Run the rest of the algorithm on the converted peaks
            Interference(precursorInfo, peaks);
        }

        /// <summary>
        /// Calculate the interference for the scan based on the provided data
        /// </summary>
        /// <param name="precursorInfo">Precursor info: must set IsolationMass, ChargeState, IsolationWidth</param>
        /// <param name="peakData">list of centroided peakData</param>
        public static void Interference(PrecursorInfo precursorInfo, List<Peak> peakData)
        {

            if (precursorInfo.ChargeState <= 0)
            {
                var newChargeState = ChargeStateGuesstimator(precursorInfo.IsolationMass, peakData);
                precursorInfo.UpdateCharge(newChargeState);

                if (precursorInfo.ChargeState <= 0)
                {
                    Console.WriteLine("Charge state for {0:F2} in scan {1} not supplied, and could not guesstimate it. " +
                                      "Giving bad score.", precursorInfo.IsolationMass, precursorInfo.ScanNumber);

                    precursorInfo.Interference = 0;
                    return;
                }
            }

            var mzWindowLow = precursorInfo.IsolationMass - (precursorInfo.IsolationWidth / 2);
            var mzWindowHigh = precursorInfo.IsolationMass + (precursorInfo.IsolationWidth / 2);

            // Narrow the range of peaks to the final tolerances
            var peaks = FilterPeaksByMZ(mzWindowLow, mzWindowHigh, peakData);

            // find target peak for use as precursor to find interference
            ClosestToTarget(precursorInfo, peaks);

            // perform the calculation
            InterferenceCalculation(precursorInfo, peaks);
        }

        private struct MassChargeData
        {
            public readonly double Mass;
            public readonly int Charge;

            public MassChargeData(double mass, int charge)
            {
                Mass = mass;
                Charge = charge;
            }
        }

        /// <summary>
        /// Given an isolation mass and spectra data, give a reasonable guess of the charge state using abundance summing of potential isotopes
        /// </summary>
        /// <param name="isolationMass"></param>
        /// <param name="spectraData2D">Array of centroided peak data of size [2,x], where [0,0] is first m/z and [1,0] is first intensity</param>
        /// <returns></returns>
        public static int ChargeStateGuesstimator(double isolationMass, double[,] spectraData2D)
        {
            var peaks = ConvertToPeaks(ref spectraData2D);
            return ChargeStateGuesstimator(isolationMass, peaks);
        }

        /// <summary>
        /// Given an isolation mass and peak data, give a reasonable guess of the charge state using abundance summing of potential isotopes
        /// </summary>
        /// <param name="isolationMass"></param>
        /// <param name="peaks">List of centroided peaks</param>
        /// <returns></returns>
        public static int ChargeStateGuesstimator(double isolationMass, List<Peak> peaks)
        {
            // TODO: Is this sufficient, or should it be changed to a PPM Error tolerance?
            // m/z tolerance
            const double massTol = 0.01;
            const int minChargeToCheck = 1;
            const int maxChargeToCheck = 4;

            // One entry per charge; key=charge, value=sum of intensities of matching peaks
            var chargesAbund = new Dictionary<int, double>();
            // List of all masses to check, duplicates allowed
            var massesToCheck = new List<MassChargeData>();

            // Add initial data to chargesAbund, and configure massesToCheck
            for (var i = minChargeToCheck; i <= maxChargeToCheck; i++)
            {
                chargesAbund.Add(i, 0);

                // Add the isolation mass for each charge being checked
                massesToCheck.Add(new MassChargeData(isolationMass, i));
                // Add all isotope masses to check for this charge
                for (var j = 1; j <= NumIsotopesToCheckChargeGuess; j++)
                {
                    var massDiff = C12_C13_MASS_DIFFERENCE / i;
                    massesToCheck.Add(new MassChargeData(isolationMass - massDiff, i));
                    massesToCheck.Add(new MassChargeData(isolationMass + massDiff, i));
                }
            }

            // Sort by mass, and set the range limits
            massesToCheck.Sort((x,y) => x.Mass.CompareTo(y.Mass));
            var minMass = massesToCheck.First().Mass - massTol;
            var maxMass = massesToCheck.Last().Mass + massTol;
            var isoMassInt = 0.0;

            // Iterate through the peak data, and add the matching m/z's intensities to the appropriate charge abundances
            foreach (var peak in peaks.OrderBy(x => x.Mz))
            {
                var mz = peak.Mz;
                var abund = peak.Abundance;
                // Skip - not in range yet
                if (mz < minMass)
                {
                    continue;
                }
                // Stop - past the range
                if (maxMass < mz)
                {
                    break;
                }
                // IsolationMass intensity, for determining if there were no other peaks that matched a charge state
                if (isolationMass - massTol <= mz && mz <= isolationMass + massTol)
                {
                    isoMassInt += abund;
                }

                var minMz = mz - massTol;
                var maxMz = mz + massTol;
                // Add the matching m/z's intensities to the appropriate charge abundances
                foreach (var match in massesToCheck.Where(x => minMz <= x.Mass && x.Mass <= maxMz))
                {
                    chargesAbund[match.Charge] += abund;
                }
            }

            var mostIntense = chargesAbund.OrderByDescending(x => x.Value).First();
            // If the only peak m/z that was matched was the isolation mass, then we didn't find anything.
            if (mostIntense.Value.Equals(isoMassInt))
            {
                return 0;
            }

            return mostIntense.Key;
        }

        /// <summary>
        /// Calculates interference with the precursor ion
        /// </summary>
        /// <param name="precursorInfo"></param>
        /// <param name="peaks"></param>
        private static void InterferenceCalculation(PrecursorInfo precursorInfo, List<Peak> peaks)
        {
            const double PRECURSOR_ION_TOLERANCE_PPM = 15.0;

            double precursorIntensitySum = 0;

            double intensitySumAllPeaks = 0;

            // Fraction of observed peaks that are from the precursor
            // Higher score is better; 1 means all peaks are from the precursor
            double overallInterference = 0;

            if (peaks.Count > 0)
            {

                for (var j = 0; j < peaks.Count; j++)
                {
                    // Option 1 for determining if the observed peak is close to an expected m/z value
                    /*
                    var adjustedMz = peaks[j].Mz;
                    while (adjustedMz < precursorInfo.ActualMass && precursorInfo.ActualMass - adjustedMz > massToleranceMz)
                    {
                        adjustedMz += C12_C13_MASS_DIFFERENCE / precursorInfo.ChargeState;
                    }

                    while (adjustedMz > precursorInfo.ActualMass && adjustedMz - precursorInfo.ActualMass > massToleranceMz)
                    {
                        adjustedMz -= C12_C13_MASS_DIFFERENCE / precursorInfo.ChargeState;
                    }

                    var deltaMzObserved = adjustedMz - precursorInfo.ActualMass;
                    var massToleranceMz = PRECURSOR_ION_TOLERANCE_PPM * precursorInfo.ActualMass / 1E6;

                    if (Math.Abs(deltaMzObserved) < massToleranceMz)
                    {
                        precursorIntensitySum += peaks[j].Abundance;
                    }
                    */

                    // Option 2 for determining if the observed peak is close to an expected m/z value
                    var difference = (peaks[j].Mz - precursorInfo.ActualMass) * precursorInfo.ChargeState;
                    var differenceRounded = Math.Round(difference);
                    var expectedDifference = differenceRounded * C12_C13_MASS_DIFFERENCE;
                    var differencePpm = Math.Abs((expectedDifference - difference) /
                                                  (precursorInfo.IsolationMass * precursorInfo.ChargeState)) * 1000000;

                    if (differencePpm < PRECURSOR_ION_TOLERANCE_PPM)
                    {
                        precursorIntensitySum += peaks[j].Abundance;
                    }

                    intensitySumAllPeaks += peaks[j].Abundance;
                }

                overallInterference = precursorIntensitySum / intensitySumAllPeaks;
            }
            else
            {
                Console.WriteLine("Did not find the precursor for {0:F2} in scan {1}", precursorInfo.IsolationMass, precursorInfo.ScanNumber);
            }

            precursorInfo.Interference = overallInterference;
        }

        /// <summary>
        /// Converts a two-dimensional array of peak data to a list of peaks
        /// </summary>
        /// <param name="spectraData2D">Array of centroided peak data of size [2,x], where [0,0] is first m/z and [1,0] is first intensity</param>
        /// <param name="lowIndex">Lowest index to convert</param>
        /// <param name="highIndex">Highest index to convert</param>
        /// <returns>List of Peaks extracted from spectraData2D (between lowIndex and highIndex)</returns>
        public static List<Peak> ConvertToPeaks(ref double[,] spectraData2D, int lowIndex = 0, int highIndex = int.MaxValue)
        {
            var mzs = new List<Peak>();
            var maxIndex = spectraData2D.GetUpperBound(1);
            lowIndex = Math.Max(lowIndex, 0);
            highIndex = Math.Min(highIndex, maxIndex);

            for (var i = lowIndex; i <= highIndex; i++)
            {
                if (spectraData2D[1, i] > 0)
                {
                    mzs.Add(new Peak
                    {
                        Mz = spectraData2D[0, i],
                        Abundance = spectraData2D[1, i]
                    });

                }
            }

            return mzs;
        }

        /// <summary>
        /// Binary search for search the m/z dimension of the 2-dimensional array (.NET binary search doesn't support multi-dimensional arrays)
        /// </summary>
        /// <param name="spectraData2D"></param>
        /// <param name="min"></param>
        /// <param name="max"></param>
        /// <param name="mzToFind"></param>
        /// <returns></returns>
        private static int BinarySearch(ref double[,] spectraData2D, int min, int max, double mzToFind)
        {
            const double tol = 0.1;
            var mid = 0;

            while (true)
            {
                // Exit condition
                if (Math.Abs(spectraData2D[0, mid] - mzToFind) < tol || mid == (max + min) / 2)
                {
                    break;
                }

                // set midpoint
                mid = (max + min) / 2;

                // mid is smaller - next min is mid
                if (spectraData2D[0, mid] < mzToFind)
                {
                    min = mid;
                }

                // mid is larger - next max is mid
                if (spectraData2D[0, mid] > mzToFind)
                {
                    max = mid;
                }
            }
            return mid;
        }

        /// <summary>
        /// finds the peak closest to the targeted peak to be isolated in raw header and
        /// gets the intensity info.
        /// </summary>
        /// <param name="precursorInfo"></param>
        /// <param name="peaks"></param>
        private static void ClosestToTarget(PrecursorInfo precursorInfo, IEnumerable<Peak> peaks)
        {
            var closest = 100000.0;
            var abund = 0.0;
            foreach (var p in peaks)
            {
                var temp = Math.Abs(p.Mz - precursorInfo.IsolationMass);
                if (temp < closest)
                {
                    precursorInfo.ActualMass = p.Mz;
                    closest = temp;
                    abund = p.Abundance;
                }
            }

            // Only set the Precursor Intensity if we have a place to store it (usually only used for full workflow in InterferenceDetector)
            var precursorInfoWithIntensity = precursorInfo as PrecursorIntense;
            if (precursorInfoWithIntensity != null)
            {
                precursorInfoWithIntensity.PrecursorIntensity = abund;
            }
        }

        /// <summary>
        /// Filters the peak list to only retain peaks between lowMz and highMz
        /// </summary>
        /// <param name="lowMz"></param>
        /// <param name="highMz"></param>
        /// <param name="peaks"></param>
        private static List<Peak> FilterPeaksByMZ(double lowMz, double highMz, IEnumerable<Peak> peaks)
        {
            return peaks.Where(x => lowMz < x.Mz && x.Mz < highMz).OrderBy(x => x.Mz).ToList();
        }
    }
}
