# OPC UA Part 13 aggregate conformance tests

Tests the server's aggregate calculators against OPC 10000-13 v1.05.07, independent of the CTT.
Where Part 13 is clear the tests follow the text; where it leaves a point open the 1.5.378
behaviour is kept and the test says so. Bounding values follow the WG answer to Mantis 11462:
TreatUncertainAsBad does not apply to them, so an Uncertain raw value is used and makes the bound
Uncertain_DataSubNormal.

- `AggregateExamples.csv`: the official Annex A examples (Annex A.1.1), from
  OPCFoundation/UA-Nodeset tag `UA-1.05.07-2026-04-15`, `Schema/AggregateExamples.csv`
  (SHA-256 `917a8d6a6565586765548be21f80f37ed6acad47e86aed2763754c7b4aca2c3d`, unchanged since UA-1.05.02).
  Embedded in the test assembly as `Part13.AggregateExamples.csv`.
- `AnnexAExamples.cs`: parser (5 raw data sets, 159 example tables).
- `AggregateResultComparer.cs`: runs `Aggregators.CreateStandardCalculator` and compares value,
  StatusCode, aggregate bits (Raw/Calculated/Interpolated, Partial, MultipleValues) and timestamp.
- `Part13Deviations.cs`: Annex A is informative. Rows that contradict an explicit normative clause
  (Kind `AnnexAContradictsNormativeText`) or sit on a clause Part 13 leaves open (Kind
  `SpecificationAmbiguous`: the reading the server implements, the 1.5.378 behaviour or, for
  bounds, the WG answer to Mantis 11462) are replaced by the normative expectation.
  Every entry names the clause and the reason; related Mantis ids are given.
- `Part13AnnexATests.cs`: one test per Annex A table (`AnnexA.<Aggregate>.<DataSet>`) and
  `WriteAnnexAComparisonReport`, which writes `Part13-AnnexA-report.csv` and
  `Part13-AnnexA-summary.txt` to `%PART13_REPORT_DIR%`.
- `Part13NormativeTests.cs`: hand-computed cases, each derived from the quoted Part 13 sentence:
  WorstQuality2 (Mantis 11422), Minimum/Maximum/ActualTime/Range status and bits (Tables 58-62,
  Mantis 11427), the PercentDataBad/PercentDataGood rules of §4.2.1.2 (tie, invalid settings),
  TimeAverage/Total on Stepped variables, real bounds on integer data, integer bounds, Interpolative
  with a skipped value (Table 52), the Partial bit of a shortened last interval (§5.3.3.2), the
  Good-only aggregates Average, Delta, StandardDeviation and Variance, TimeAverage2/Total2 without
  non-Good data (§5.4.3.7), the region that starts at the last raw value (§5.4.3.31), Duration
  results rounded to whole milliseconds (Mantis 11499), and AnnotationCount, which counts the queued
  Annotations (§5.4.3.20, Table 68).
- `Part13DataTypeTests.cs`: result data types (Table 50): Delta/DeltaBounds keep the raw data type,
  a delta that does not fit it is Bad_AggregateInvalidInputs (Table 45), Range/Range2 return a Double
  as in 1.5.378 (Table 50 gives the raw data type; kept to avoid a breaking change), and integer
  interpolation keeps the 1.5.378 TypeInfo.Cast conversion (Mantis 11494, 11499, 11518).
- `Part13ValidDataTypeTests.cs`: the valid source data types of Table 50 (§5.4.2.3). Numeric
  aggregates on Boolean, String (also a String holding a number), DateTime or array values give
  Bad_AggregateInvalidInputs (Table 45); DurationInStateZero/NonZero and NumberOfTransitions also
  accept Boolean; the other aggregates accept every data type. A non-numeric sample in a numeric
  variable invalidates only the intervals that use it, and `AggregateManager.IsSupportedForDataType`
  gives the node-level answer (Bad_AggregateNotSupported). The live checks for processed HistoryRead
  and MonitoredItem AggregateFilters are in `ReferenceServerTest`.
- `Part13SteppedDataTypeTests.cs`: stepped versus sloped interpolation per data type (Int32, Float,
  Double, Boolean, String), a DeltaBounds example calculated by hand from §3.1.9, and
  `AggregateManager.GetConfiguration`, `FindHistoricalConfiguration` and `IsStepped`, which take Stepped
  and the AggregateConfiguration from the node's HA Configuration.
- `AggregateBehaviourMatrix.cs`, `Master378AggregateResults.txt.gz` and
  `Part13Master378BehaviourTests.cs`: compare every result of the branch with the 1.5.378 release
  branch (see below).

## Comparison with 1.5.378

`AggregateBehaviourMatrix` runs all 37 standard aggregates over the five Annex A histories and Int32
copies of the four numeric ones, for 5 s and 16 s intervals forward, 16 s backward and one interval
for the whole range, each with TreatUncertainAsBad as configured and inverted (23,310 results).
`Master378AggregateResults.txt.gz` is its output on the 1.5.378 release branch (`master378`
359abe68b; the later `master378` commits do not touch the aggregates). The uncompressed file has
SHA-256 `2197c56809f8ad36f7c8ef28d6ad8b7ea2ad79263e3fbb537d71071a190972e5`, which
`GoldenFileIsThe1_5_378Output` checks.

`EveryDifferenceFrom1_5_378IsExplainedByAPart13Rule` runs the matrix on the current build and requires
every result that differs from 1.5.378 to be explained by one of its rules. Each rule names the
Part 13 clause (or Mantis answer, or upstream commit) that requires the change and, where it can,
computes the expected result from the raw data, from the aggregate definition or from another 1.5.378
result. The rules are applied to the unchanged results as well, so a rule that is too broad fails, and
a rule that explains no difference fails, so the list cannot keep stale entries. The test output lists
the number of results each rule explains.

A new difference therefore fails the test until it is either reverted or added as a rule with its
reason. The golden file is regenerated only from the 1.5.378 release branch, never from this branch:

1. Check out `master378` in a separate worktree and build `Libraries/Opc.Ua.Server` (Release, net10.0).
2. Create a console project that compiles `Tests/Opc.Ua.Server.Tests/Part13/AggregateBehaviourMatrix.cs`
   (linked) and references `Opc.Ua.Core.dll`, `Opc.Ua.Types.dll` and `Opc.Ua.Server.dll` of that build.
   Its `Main` loads the data sets with `AggregateBehaviourMatrix.LoadDataSets` from
   `AggregateExamples.csv` and writes one line `Key<TAB>Index<TAB>Result` (LF, UTF-8 without BOM) per
   `AggregateBehaviourMatrix.Run` result.
3. Compress the output with gzip to `Master378AggregateResults.txt.gz` and update the SHA-256 here and
   in `Part13Master378BehaviourTests`.

Run:

```
dotnet test Tests\Opc.Ua.Server.Tests\Opc.Ua.Server.Tests.csproj -f net10.0 /p:CustomTestTarget=net10.0 --filter "Category=Part13"
```
