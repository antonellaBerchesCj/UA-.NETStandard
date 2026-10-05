# Part 13 aggregate fixes on the 1.4.371 code base

This branch (`master378_cttBackport` of the antonellaBerchesCj fork) is at
`fffa8bf4` (2022-12-12, 1.4.371-preview, C# 7.3). It is not the `master378`
branch, so the aggregate fixes that `master378` received through the CTT
backports were applied here by hand. The public API is unchanged apart from the
new protected helper `AggregateCalculator.TryGetRawValueAtOrBefore`: the
calculator constructors keep their 1.4.371 signatures (no `ITelemetryContext`),
and no file outside `Libraries/Opc.Ua.Server/Aggregates` changes behaviour.

The goal is that the calculators give the same results as `master378` with the
CTT backports, for the CTT 1.05.513 aggregate scripts.

## What changed

Origin is the upstream pull request the change comes from (#4326 is the first
`master378` CTT backport; #2505 is a 2024 `master` fix).

| Area | Change | Part 13 | Origin |
|------|--------|---------|--------|
| `AggregateCalculator.QueueRawValue` | `Bad_BoundNotFound` placeholders are ignored like `Bad_NoData`. The start and end of data are tracked chronologically, after the order check, so they are right for reverse reads and are not moved by a rejected value. | §5.3.3.2 | #4326, #4478 |
| `AggregateCalculator.GetProcessedValue` | The Partial bit for the end of data is also set on reverse reads. `OverflowException`/`InvalidCastException` from a calculation give `Bad_TypeMismatch` instead of failing the read. | §5.3.3.2 | #4478, #4503 |
| `AggregateCalculator.Interpolate` | A non-Bad raw value at the timestamp is returned as is. | §3.1.8 | #4326 |
| `GetValueBasedStatusCode` | Uncertain values count as Good when TreatUncertainAsBad is false and as Bad when it is true. A Bad result keeps the aggregate bits. | §4.2.1.2, §5.4.3.2.1 | #4503 |
| `GetTimeBasedStatusCode` | Uncertain regions count as Bad only when TreatUncertainAsBad is true, otherwise as Good. The Bad ratio is checked before the Good ratio (when Bad regions exist). A Bad result keeps the aggregate bits. | §5.4.3.2.1 | #4478 |
| `TryGetRawValueAtOrBefore` | New helper used by DurationGood/DurationBad/PercentGood/PercentBad. | — | #4478 |
| Count: DurationInStateZero/NonZero | The status regions follow the variable's interpolation, so a region ending in an Uncertain end bound is Uncertain. | §5.4.3.2.2 | #4545 |
| Count: NumberOfTransitions | The previous value is the early bound (honours TreatUncertainAsBad); the first value counts as a transition when there is no previous value; values are compared as they are, not cast to Double. | §5.4.3.24 | #4326, #4545 |
| Min/Max | Uncertain_DataSubNormal also when an Uncertain value lies beyond the Good extremum. Calculated is set for Uncertain results, and the "at interval start" test uses the request-direction timestamp. | §5.4.3.10-§5.4.3.14 | #4326, #4477 |
| Range/Range2 | The result has the source data type, Double only when the range does not fit it. | §5.4.3.14, §5.4.3.19 | #4571 |
| Delta/DeltaBounds | A delta outside the source type is widened to the next signed type (Double for 64-bit sources) instead of Bad_TypeMismatch. DeltaBounds returns Bad_NoData for Bad bounds and Uncertain_DataSubNormal for Uncertain bounds, whatever TreatUncertainAsBad is (the simple bounds themselves follow TreatUncertainAsBad). | §5.4.3.27, §5.4.3.30 | #4503, #4571 |
| DurationGood/Bad, PercentGood/Bad | Uncertain regions are counted per TreatUncertainAsBad. The first region has the status of the raw value at or before the interval start (Bad if none). | §5.4.3.31-§5.4.3.34 | #4326, #4478 |
| WorstQuality/WorstQuality2 | WorstQuality2 adds only the start bound, taken at the early time of the interval; values are evaluated chronologically in both directions. Multiple Good values set MultipleValues. | §5.4.2.2, §5.4.3.35-§5.4.3.36 | #4326, #4478 |
| StdDev/Variance | StandardDeviationSample and VariancePopulation were swapped; the sample variance divides by n - 1; both use the Good raw values without bounds. | §5.4.3.37-§5.4.3.40 | #2505, #4326 |
| `AggregateManager` | The server default configuration has TreatUncertainAsBad = True (Part 13 default). `DiagnosticsNodeManager` may be null. | §4.2.1.2 | #4326 |

Not taken: the Reference Server processed-history adapter and the AnnotationCount
changes in `ProcessedHistoryAdapter` (this code base has no processed history in
the Reference Server), and the `ITelemetryContext`/code clean-up changes.

## Tests

All files are in `Tests/Opc.Ua.Server.Tests` and drive the calculators directly.

- `AggregateCttTests.cs`: the scenarios of the upstream CTT regression
  tests for the fixes above (the direct half of the master378 "DirectAndLive"
  tests, which also read through a Reference Server that this branch does not
  have).
- Calculator suites from the first `master378` backport (#4326), ported to
  C# 7.3 and the 1.4.371 API with the changes made to them by the later fixes:
  `AggregatorsTests.cs`, `AverageAggregateCalculatorTests.cs`,
  `CountAggregateCalculatorTests.cs`, `MinMaxAggregateCalculatorTests.cs`,
  `StartEndAggregateCalculatorTests.cs`, `StartEndAggregateCalculatorEdgeTests.cs`,
  `StatusAggregateCalculatorTests.cs`, `StdDevAggregateCalculatorTests.cs`,
  `StdDevAggregateCalculatorRegressionTests.cs`.
- `AggregateTestCompatibility.cs`: extension methods that map the newer test
  API (`TryGetProcessedValue`, `Variant.TryGetValue`, `ConvertToDouble`,
  `IsNull()`) to the 1.4.371 types.

Changes beyond syntax, each marked `// 1.4.371:` in the code: `Is.Default`
became `Is.Null` (NUnit 3.13 has no `Is.Default`), and WorstQuality results are
compared through `StatusCode.Code` (the 1.4.371 `StatusCode` does not implement
`IEquatable<uint>`, so NUnit would not match it against a `StatusCodes` constant).

Not ported: the tests that need processed history in the Reference Server
(`ProcessedHistoryAdapter*`, the "Live" half of `AggregateCttRegressionTests`,
the history parts of `ReferenceServerTest`).

```powershell
dotnet test Tests\Opc.Ua.Server.Tests\Opc.Ua.Server.Tests.csproj --filter "Category=Aggregators|Category=AverageAggregateCalculator"
```

`targets.props` selects the test frameworks from `$(VisualStudioVersion)`: with
17.0 (Visual Studio 2022) they are net48, net462, netcoreapp3.1 and net6.0,
otherwise only net462. Add `-f net6.0` only when that framework is in the list.
