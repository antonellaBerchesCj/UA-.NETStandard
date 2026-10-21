# OPC UA Part 13 aggregate conformance tests

Tests the server's aggregate calculators against OPC 10000-13 v1.05.07, independent of the CTT.

- `AggregateExamples.csv`: the official Annex A examples (Annex A.1.1), from
  OPCFoundation/UA-Nodeset tag `UA-1.05.07-2026-04-15`, `Schema/AggregateExamples.csv`
  (SHA-256 `917a8d6a6565586765548be21f80f37ed6acad47e86aed2763754c7b4aca2c3d`, unchanged since UA-1.05.02).
  Embedded in the test assembly as `Part13.AggregateExamples.csv`.
- `AnnexAExamples.cs`: parser (5 raw data sets, 159 example tables).
- `AggregateResultComparer.cs`: runs `Aggregators.CreateStandardCalculator` and compares value,
  StatusCode, aggregate bits (Raw/Calculated/Interpolated, Partial, MultipleValues) and timestamp.
- `Part13Deviations.cs`: Annex A is informative. Rows that contradict an explicit normative clause
  (Kind `AnnexAContradictsNormativeText`) or sit on a clause Part 13 leaves open (Kind
  `SpecificationAmbiguous`, server reading accepted) are replaced by the normative expectation.
  Every entry names the clause and the reason; related Mantis ids are given.
- `Part13AnnexATests.cs`: one test per Annex A table (`AnnexA.<Aggregate>.<DataSet>`) and
  `WriteAnnexAComparisonReport`, which writes `Part13-AnnexA-report.csv` and
  `Part13-AnnexA-summary.txt` to `%PART13_REPORT_DIR%`.
- `Part13NormativeTests.cs`: hand-computed cases for the rules disputed with the CTT
  (Mantis 11422, 11426, 11427, 11466) and for the server defects found and fixed in
  `Libraries/Opc.Ua.Server/Aggregates` (TimeAverage/Total on Stepped variables, WorstQuality2 and
  TreatUncertainAsBad, real bounds on integer data, rounding, the PercentDataBad/PercentDataGood tie of
  §4.2.1.2, Interpolative on Stepped variables with a Bad value on the timestamp).
- `Part13SteppedDataTypeTests.cs`: stepped versus sloped interpolation per data type (Int32, Float,
  Double, Boolean, String) for the aggregates of the CTT report on `StaticHistoricalDataItem_HistorianInt2`
  (DeltaBounds, DurationInStateZero/NonZero, StartBound, EndBound, Interpolative, Minimum2, Maximum2,
  MinimumActualTime2, MaximumActualTime2, Range2, TimeAverage2, Total, Total2), the CTT DeltaBounds graph
  (-713.89 with Stepped = False, -120.57 with Stepped = True) and the
  `AggregateManager.CreateCalculator(..., HistoricalDataConfigurationState, ...)` overload that takes Stepped
  and the AggregateConfiguration from the node's HA Configuration.
- Live tests against a running server are in the Softing samples (`Client\SampleClient.Tests\Part13Live`,
  `Category=Part13Live`); they run the same calculations through ReadProcessed.

Run:

```
dotnet test Tests\Opc.Ua.Server.Tests\Opc.Ua.Server.Tests.csproj -f net8.0 --filter "Category=Part13"
```
