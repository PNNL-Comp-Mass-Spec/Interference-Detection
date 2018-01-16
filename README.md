# Interference Detector

The Interference Detector library implements an algorithm for quantifying
the homogeneity of species isolated for fragmentation in
LC-MS/MS analyses using data directed acquisition (DDA).
It works with Thermo .Raw files and _isos.csv files from DeconTools.
The inteference score computed for each parent ion is the
fraction of ions in the isolation window that are from the precursor
(weighted by intensity).

## Overview

In DDA analyses, compounds (e.g. peptides) are isolated then 
bombarded with extra energy to cause the compound to break apart
into components (fragments).  Ideally a single ion is isolated 
for fragmentation, but in complex samples, multiple species 
may be isolated simultaneously.

The InterferenceDetector class examines the parent MS1 spectrum
of each fragmented ion, looking for other ions that were included 
in the isolation window.  It computes an interference score 
for each parent ion, writing the results to a new SQLite table.

The DMS Analysis Manager uses InterDetect.dll when processing
iTRAQ or TMT data to compute interference scores.

## IDM Console

The IDM_Console.exe program can be used to manually process a SQLite file.

```
IDM_Console.exe 
  InputFile.db3
  [/Tol:PrecursorTolerancePPM] 
  [/CSTol:ChargeStateEstimationTolerance] 
  [/KeepTemp]
```

The input file is a SQLite database with table `T_MSMS_Raw_Files` 
and optionally either `T_Results_Metadata_Typed` or `T_Results_Metadata`

Use /Tol to specify the PPM tolerance when looking for the precursor ion in the isolation window
The default is +/-15 PPM

Use /CSTol to specify the m/z tolerance when guesstimating the charge
The default is +/-0.01 m/z

Use /KeepTemp to not delete the temporary precursor info file, `prec_info_temp.txt`


## Required inputs

A SQLite file with the following tables.

`T_MSMS_Raw_Files`

| Column     | Description    |
|------------|----------------|
| Dataset_ID | Dataset ID     |
| Dataset    | Dataset Name   |
| Folder     | Dataset Folder Path |

The program assumes that datasets in `T_MSMS_Raw_Files` are Thermo datasets
and will therefore append extension .raw to the dataset names when determining
the dataset file path.

Optionally a table with DeconTools job info.  This info can be 
in either `T_Results_Metadata_Typed` or `T_Results_Metadata`

| Column     | Description         |
|------------|---------------------|
| Tool       | Analysis Tool Name  |
| Dataset_ID | Dataset ID          |
| Dataset    | Dataset Name        |
| Folder     | Dataset Folder Path |

The program looks for rows in this table where the tool name starts with `Decon`.

It is not a fatal error if this table is missing, or if the table has no DeconTools jobs.
If the file does exist, it will be used to determine the charge state of parent ions
listed in the .Raw file as having a charge of 0 (unknown charge).  The tolerance used
when finding matching ions in the _isos.csv file is +/-0.005 m/z

### Isos.csv file

The _isos.csv file is a comma separated file created by DeconTools.  Required columns:

| Column     | Description   |
|------------|---------------|
| scan_num   | Scan number   |
| mz         | Ion m/z       |
| charge     | Charge state  |
| abundance  | Ion abundance |


## Interfence Detection Workflow

The InterferenceDetector class opens the Results.db3 file and determines 
the datasets to process using tables `T_MSMS_Raw_Files` and `T_Results_Metadata_Typed`. 
For each dataset, it copies the thermo .Raw file locally, then reads the data
using the ThermoRawFileReader. It also looks for the _isos.csv file and 
caches the data if found.

For each ion chosen for fragmentation, the ions in the region of the precursor ion
(plus or minus the isolation width) are examined. If the charge state
was not determined by the Thermo acquisition software or by DeconTools,
is is estimated using the ChargeStateGuesstimator method.  If the charge cannot be
guesstimated, the interference score for the ion will be 0.

For precursor ions with a known charge, the algorithm computes the m/z difference
(in ppm) between a given ion and the precursor ion.  If this difference corresponds
to an expected difference for the given charge state (+/- 15 ppm), the ion is considered
to have arisen from the precursor.

The algorithm computes the sum of the intensity of all of the ions in the isolation window,
along with a sum of the intensity of all of the precursor-associated ions in the window,
then computes the interference score as
`interferenceScore = intensitySumPrecursorIons / intensitySumAllPeaks`

Example calculations

| Precursor Ions Intensity sum | All Ions Intensity Sum | Inteference Score |
|------------------------------|------------------------|-------------------|
| 22102                        | 31812                  | 0.695             |
| 8593334                      | 8936750                | 0.962             |
| 201366                       | 410589                 | 0.490             |
| 281669                       | 320436                 | 0.879             |

An Inteference score of 1 means that all of the peaks in the 
isolation window were from the precursor ion.

## Results table

The program will create (or replace) table `T_Precursor_Interference` with columns:

| Column            | Description             |
|-------------------|-------------------------|
| Dataset_ID        | Dataset ID              |
| ScanNumber        | MS/MS scan number       |
| PrecursorScan     | Precursor (MS1) scan number |
| ParentMZ          | Parent ion m/z          |
| ChargeState       | Parent ion charge state |
| IsoWidth          | Isolation width         |
| Interference      | Interference score; larger is better, with a max of 1 and minimum of 0 |
| PreIntensity      | Parent ion abundance in the precursor scan                             |
| IonCollectionTime | Ion Injection Time (ms)                                                |

This data is also written to file `prec_info_temp.txt` ,though that file 
is deleted after the information is successfully added to the SQLite database.

## Contacts

Written by Josh Aldrich for the Department of Energy (PNNL, Richland, WA) \
E-mail: proteomics@pnnl.gov
Website: https://omics.pnl.gov/ or https://panomics.pnnl.gov/

## License

The Interference Detector library is licensed under the Apache License, Version 2.0; you may not use this 
file except in compliance with the License.  You may obtain a copy of the 
License at https://opensource.org/licenses/Apache-2.0
