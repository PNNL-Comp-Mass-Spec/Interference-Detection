using System;
using System.Collections.Generic;
using System.Linq;

namespace InterDetect
{
    public class InterferenceCalculator
    {
        const double C12_C13_MASS_DIFFERENCE = 1.0033548378;

        /// <summary>
        /// Calculate the interference for the scan based on the provided data
        /// </summary>
        /// <param name="precursorInfo">Precursor info: must set IsolationMass, ChargeState, IsolationWidth</param>
        /// <param name="spectraData2D">Array of centroided peak data of size [2,x], where [0,0] is first m/z and [1,0] is first intensity</param>
        public static void Interference(PrecursorInfo precursorInfo, double[,] spectraData2D)
        {
            if (precursorInfo.ChargeState <= 0)
            {
                precursorInfo.ChargeState = ChargeStateGuesstimator(precursorInfo.IsolationMass, spectraData2D);
            }
            if (precursorInfo.ChargeState <= 0)
            {
                Console.WriteLine("Charge state for " + precursorInfo.IsolationMass + " in scan " + precursorInfo.ScanNumber + " not supplied, and could not guesstimate it. Giving bad score.");
                precursorInfo.Interference = 0;
                return;
            }

            var mzToFindLow = precursorInfo.IsolationMass - (precursorInfo.IsolationWidth);
            var mzToFindHigh = precursorInfo.IsolationMass + (precursorInfo.IsolationWidth);

            var a = 0;
            var b = spectraData2D.GetUpperBound(1) + 1;
            var c = 0;

            var lowInd = BinarySearch(ref spectraData2D, a, b, c, mzToFindLow);
            var highInd = BinarySearch(ref spectraData2D, lowInd, b, c, mzToFindHigh);

            //capture all peaks in isowidth+buffer
            var peaks = ConvertToPeaks(ref spectraData2D, lowInd, highInd);

            var mzWindowLow = precursorInfo.IsolationMass - (precursorInfo.IsolationWidth / 2);
            var mzWindowHigh = precursorInfo.IsolationMass + (precursorInfo.IsolationWidth / 2);

            // Narrow the range of peaks to the final tolerances
            peaks = FilterPeaksByMZ(mzWindowLow, mzWindowHigh, peaks);

            //find target peak for use as precursor to find interference
            ClosestToTarget(precursorInfo, peaks);

            //perform the calculation
            InterferenceCalculation(precursorInfo, peaks);
        }

        private struct MassChargeData
        {
            public double Mass;
            public int Charge;

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
            // TODO: Is this sufficient, or should it be changed to a PPM Error tolerance?
            // m/z tolerance
            const double massTol = 0.01;
            const int numIsotopesToCheck = 2;
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
                for (var j = 1; j <= numIsotopesToCheck; j++)
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

            var maxIndex = spectraData2D.GetUpperBound(1);
            // Iterate through the peak data, and add the matching m/z's intensities to the appropriate charge abundances
            for (var i = 0; i <= maxIndex; i++)
            {
                var mz = spectraData2D[0, i];
                var abund = spectraData2D[1, i];
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
            const double PreErrorAllowed = 10.0;
            double MaxPreInt = 0;
            double MaxInterfereInt = 0;
            double OverallInterference = 0;

            if (peaks.Count > 0)
            {
                for (var j = 0; j < peaks.Count; j++)
                {
                    var difference = (peaks[j].Mz - precursorInfo.ActualMass) * precursorInfo.ChargeState;
                    var difference_Rounded = Math.Round(difference);
                    var expected_difference = difference_Rounded * C12_C13_MASS_DIFFERENCE;
                    var Difference_ppm = Math.Abs((expected_difference - difference) /
                                                  (precursorInfo.IsolationMass * precursorInfo.ChargeState)) * 1000000;

                    if (Difference_ppm < PreErrorAllowed)
                    {
                        MaxPreInt += peaks[j].Abundance;
                    }

                    MaxInterfereInt += peaks[j].Abundance;
                }
                OverallInterference = MaxPreInt / MaxInterfereInt;
            }
            else
            {
                Console.WriteLine("Did not find the precursor for " + precursorInfo.IsolationMass + " in scan " + precursorInfo.ScanNumber);
            }

            precursorInfo.Interference = OverallInterference;
        }

        private static List<Peak> ConvertToPeaks(ref double[,] spectraData2D, int lowind, int highind)
        {
            var mzs = new List<Peak>();

            for (var i = lowind + 1; i <= highind - 1; i++)
            {
                if (spectraData2D[1, i] > 0)
                {
                    /*
                    // Old code: simplistic centroiding
                    var j = i;
                    var abusum = 0.0;
                    while (spectraData2D[1, i] > 0 && !(spectraData2D[1, i - 1] > spectraData2D[1, i] && spectraData2D[1, i + 1] > spectraData2D[1, i]))
                    {
                        abusum += spectraData2D[1, i];
                        i++;
                    }
                    var end = i;
                    i = j;
                    var peaksum = 0.0;
                    var peakmax = 0.0;
                    while (i != end)
                    {
                        //test using maximum of peak
                        if (spectraData2D[1, i] > peakmax)
                        {
                            peakmax = spectraData2D[1, i];
                        }

                        peaksum += spectraData2D[1, i] / abusum * spectraData2D[0, i];
                        i++;
                    }
                    var centroidPeak = new Peak
                    {
                        Mz = peaksum,
                        Abundance = peakmax
                    };
                    mzs.Add(centroidPeak);
                    */

                    mzs.Add(new Peak
                    {
                        Mz = spectraData2D[0, i],
                        Abundance = spectraData2D[1, i]
                    });
                }
            }

            return mzs;
        }

        private static int BinarySearch(ref double[,] spectraData2D, int a, int b, int c, double mzToFind)
        {
            const double tol = 0.1;

            while (true)
            {
                if (Math.Abs(spectraData2D[0, c] - mzToFind) < tol || c == (b + a) / 2)
                {
                    break;
                }
                c = (b + a) / 2;
                if (spectraData2D[0, c] < mzToFind)
                {
                    a = c;
                }
                if (spectraData2D[0, c] > mzToFind)
                {
                    b = c;
                }
            }
            return c;
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

            if (precursorInfo is PrecursorIntense)
            {
                ((PrecursorIntense) precursorInfo).PrecursorIntensity = abund;
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
