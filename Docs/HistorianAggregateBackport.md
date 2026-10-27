# Additive Historian Aggregate Backport

This backport brings the aggregate corrections and aggregate-focused tests from
`master` into `master378`. It deliberately keeps the
`master378` historian architecture, public API, and wire behavior.

## Compatibility boundary

- `HistoryFile` remains the raw-history implementation of
  `IHistoryDataSource`.
- `IHistoryDataSource` still exposes only `FirstRaw` and `NextRaw`.
- `HistoryRecord` still contains only `RawData` and `Historizing`.
- `HistoryEntry` still contains only `Value` and `IsModified`.
- Existing Int32 archive generation remains oldest value `1000` through newest
  value `0`.
- No async historian provider, registry, client facade, or new public historian
  contract from `master` is introduced.
- Processed reads are added behind the existing synchronous NodeManager
  overrides. Existing clients continue to send the standard OPC UA
  `HistoryRead` request and do not need adapting.

The new `ProcessedHistoryAdapter` reads the existing `HistoryFile` through
`IHistoryDataSource`, feeds those values into the existing aggregate
calculators, and returns standard `HistoryData`. It does not rewrite or append
raw history. A compatibility test fingerprints the record fields and compares
all raw values, status codes, and timestamps before and after a processed read.

Annotations are stored in a separate in-memory timestamp dictionary. They are
never inserted into `HistoryRecord.RawData`, so the raw-history storage layout
and raw-read ordering remain unchanged.

## Processed-read behavior

- Forward and reverse ranges are supported.
- Leading and trailing bounds are supplied from the existing raw source.
- Aggregate configuration uses an explicit request when supplied; an implicit
  or requested server default is resolved through `AggregateManager`.
- Results are buffered as a stable request snapshot, limited to 100,000
  aggregate outputs, and returned in pages of 1,000.
- A non-empty continuation point is the only “more data” signal; the per-node
  result remains `Good`.
- Continuation points are tied to the session, node, and aggregate, and can be
  released through the existing HistoryRead release operation.
- Index range, data encoding, and `TimestampsToReturn` are applied to every
  result page.
- `AnnotationCount` counts the annotation sidecar timestamps (duplicates
  included), passed to the calculator with `AggregateCalculator.QueueAnnotation`.
  The raw history decides where the data starts and ends: an interval before or
  after it is Bad_NoData, and an interval at its edge is Partial (Part 13 Table 68).

## Part 13 calculator changes

The aggregate calculators follow OPC 10000-13 v1.05.07 where its text is clear; where it leaves a
point open they keep the 1.5.378 behaviour. No public member of the calculators is removed or
changed (`AggregateCalculator.SlopedInterpolate` still returns Bad_TypeMismatch instead of throwing);
`AggregateCalculator.QueueAnnotation` is new.
`Tests/Opc.Ua.Server.Tests/Part13/Part13Master378BehaviourTests.cs` compares every result with the
1.5.378 output and names the clause behind each difference (see `Tests/Opc.Ua.Server.Tests/Part13/README.md`).

Changes a client can see:

- No result data type changes. Range and Range2 still return a Double, as in 1.5.378. Table 50
  gives them the data type of the raw values, but that would break clients, so it is not applied.
- Aggregates are limited to the data types of Part 13 Table 50. The numeric aggregates
  (Interpolative, Average, TimeAverage, Total, the Minimum/Maximum/Range family, Delta, DeltaBounds,
  StandardDeviation and Variance) no longer calculate Boolean, String, DateTime or array values: an
  interval that uses such a value is Bad_AggregateInvalidInputs, and a processed HistoryRead or a
  MonitoredItem AggregateFilter on a variable with such a DataType is refused with
  Bad_AggregateNotSupported. DurationInStateZero/NonZero and NumberOfTransitions also accept Boolean
  and enumerations; the other aggregates accept every data type.
- TreatUncertainAsBad does not apply to bounding values (WG answer to Mantis 11462): bounds are
  built from the non-Bad raw values, and an Uncertain raw value makes the bound
  Uncertain_DataSubNormal (§3.1.8, §3.1.9). 1.5.378 applied TreatUncertainAsBad = True to the bounds
  (§3.1.7), so Interpolative, StartBound, EndBound, DeltaBounds, TimeAverage, Total and the bounds of
  the other time-based aggregates change next to an Uncertain value. An Uncertain StartBound or
  EndBound is returned with its status (Tables 76, 77).
- TimeAverage and Total always use a sloped line (§5.4.3.6, §5.4.3.8) and real-valued bounds.
- Average, Delta, StandardDeviation and Variance use Good raw values only; Minimum, Maximum and
  their ActualTime variants report an Uncertain value beyond the Good result as
  Uncertain_DataSubNormal with the Calculated bit (Tables 58-62).
- DurationGood/DurationBad use the status of each data point (§5.4.3.31-34); WorstQuality2 ignores
  TreatUncertainAsBad (§5.4.3.36).
- With TreatUncertainAsBad = True Uncertain data counts as Bad in the StatusCode calculation
  (§4.2.1.2, §5.4.3.2.1); with PercentDataGood = 100 - PercentDataBad a result is never Bad, and
  invalid PercentDataGood/PercentDataBad settings give Bad_AggregateInvalidInputs (§4.2.1.2).
- The Partial bit is also correct for backward reads and for a shortened last interval (§5.3.3.2).
- TimeAverage2 and Total2 leave all non-Good data out of the value, also Uncertain data with
  TreatUncertainAsBad = False (§5.4.3.7; Tables 55, 57); without Good data the result is Bad.
- NumberOfTransitions does not count an Uncertain value in the interval when TreatUncertainAsBad =
  True (§4.2.1.2, §5.4.3.24). The value before the interval is its bound ("a non-Bad value prior to
  the interval", Table 72), so it may be Uncertain whatever TreatUncertainAsBad is (Mantis 11462).
- The region that starts at the last raw value runs to the end of the interval (DurationGood,
  DurationBad, PercentGood, PercentBad, DurationInState, TimeAverage2, Total2); 1.5.378 ended it
  after 1 ms.
- DurationGood, DurationBad, DurationInStateZero and DurationInStateNonZero are rounded to whole
  milliseconds, half to even (Mantis 11499: Part 13 will say that Duration values are rounded,
  without fixing the algorithm); PercentGood and PercentBad use the unrounded durations.
- AnnotationCount counts the Annotations in the interval (§5.4.3.20) instead of the raw values. The
  history layer passes them with `AggregateCalculator.QueueAnnotation`; without them the result is 0.
  An interval before the start or after the end of data is Bad_NoData (Table 68). A MonitoredItem
  AggregateFilter with AnnotationCount is refused with Bad_AggregateNotSupported, because
  Annotations never reach a MonitoredItem.

## Seeded Reference Server data

The Reference Server adds deterministic historizing scalar nodes for Boolean,
Int32, Float, Double, and String. Selected pre-existing static scalar nodes are
also made historizing. Each record has 1,001 samples at ten-second intervals
with deterministic Good, Bad, and Uncertain status codes. Historical
configuration metadata is linked with `HasHistoricalConfiguration`.

## Source commit ledger in plain language

Times are the commits' recorded ISO timestamps.

| Source commit | Time | What it means for this backport |
| --- | --- | --- |
| `64ca85fce304` | 2026-04-14 13:32:10 +02:00 | Added more tests around existing behavior. Relevant aggregate tests were inventoried. |
| `14e995c1d061` | 2026-05-24 06:23:16 +02:00 | Added aggregate history integration tests while reorganizing the test projects. The tests are mapped to the older project layout. |
| `c89bc92e7cdd` | 2026-05-26 10:45:32 +02:00 | Added processed-history and annotation scenarios as part of a new historian framework. The framework is not copied; the observable behavior is adapted to the old historian. |
| `b2b4dbb13f4f` | 2026-06-10 08:34:09 +02:00 | Added the main Part 13 aggregate correctness fixes and broad tests. This is the central calculator backport. |
| `e1c734438458` | 2026-06-28 12:29:11 +02:00 | Changed general StatusCode equality rules. The broad API change is not copied; aggregate tests compare the intended code and aggregate bits explicitly. |
| `ed090304986f` | 2026-07-06 08:48:56 +02:00 | Added calculator, manager, edge-case, and regression tests that exposed additional aggregate defects. |
| `b26be47a487a` | 2026-07-08 16:24:19 +02:00 | Added tests for processed-historian branches and error paths. |
| `d7cb170b86e4` | 2026-07-09 09:08:27 +02:00 | Added CTT facets, deterministic history nodes, configuration metadata, and continuation fixes. These are implemented through the legacy NodeManager path. |
| `7f409657ade9` | 2026-07-11 04:47:25 +02:00 | Fixed which timestamp Min/Max aggregates return when the chosen value comes from a raw sample. |
| `70f2e0557a88` | 2026-07-11 20:43:06 +02:00 | Added processed-read configuration coverage, including server-default resolution. |
| `0da85b9d8988` | 2026-07-13 19:14:19 +02:00 | Fixed reverse reads, bounds, quality handling, status results, and aggregate configuration behavior. |
| `84fa8e8906c7` | 2026-07-18 05:01:03 +02:00 | Fixed bound markers, transition counting, and aggregate behavior for nonnumeric values. |
