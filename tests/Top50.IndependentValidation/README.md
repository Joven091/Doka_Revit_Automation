# Independent Top50 validation

This console suite source-links the production calculation and view-model code. It targets .NET 10 for Linux/Windows and .NET Framework 4.8 for native Windows verification without WPF or Revit. It does not establish Windows UI/installer compatibility.

Expected results come from complete stock-bounded panel-count enumeration and labelled group partition search, an independent brute-force connector procurement oracle, exact decimal reference values, and hand-computed ranking tradeoffs. Production pattern enumeration and `Top50Validation` are not used to derive the expected answers.

```bash
dotnet run --project tests/Top50.IndependentValidation -f net10.0
dotnet run --project tests/Top50.IndependentValidation -f net10.0 -- --recorded
dotnet run --project tests/Top50.IndependentValidation -f net10.0 -- --recorded --identical
dotnet run --project tests/Top50.IndependentValidation -f net10.0 -- --kits-sweep
```

The default run includes 756 exhaustive pooled-stock allocation cases, 120 exact fixed-kit cases, four recovered mixed-length kit cases with complete inventory enumeration up to eight panels, 256 connector procurement vectors, short/long/filler boundary cases, all three presets, both offering methods, input precision and stale/history persistence, and acceptance tests for the independently discovered defects. `--recorded` independently checks every returned arrangement and utilization value in the recorded 63-wall distribution; `--identical` enables its five-kit variant. Neither representative mode proves a global optimum. `--kits-sweep` checks the ordinary identical-kit search path over 286 mixed-length three-wall inputs and every returned offer.

`--baseline` permits the three known preimplementation defect observations and skips the new acceptance cases; default acceptance mode must be used for final release validation. The original revision's source-compatible scratch probe and immutable baseline evidence are described in `docs/top50-engineering-review/baseline-qa.md`. This durable suite also calls APIs introduced by the approved changes.
