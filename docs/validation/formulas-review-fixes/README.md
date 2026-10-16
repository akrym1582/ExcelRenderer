# Formula review fixes: retained evidence

Base fetched from `origin/main` on 2026-10-16:
`492f07b03f4a92be1e316d0afb939a0a48fb9711`.
Initial library, integration tests and benchmark implementation:
`d4b8f26860f7e6adf2d8aa612ae2617708526069`.
Validation scripts and final verification target:
`ec174461a17289dadda7c2d1d15432e3e6231318` (includes signed-zero and empty-sheet-name compatibility,
current package consumer script and pre-allocation regression test).

See [the validation report](../../formulas-review-fixes-validation.md) for the
results, commands, expected-value provenance, performance and remaining limits.
Historical evidence under `../formulas/` is unchanged.

`before-reproductions.log` captures the initial nine focused C# cases before the
regression suite was expanded: seven failed, two passed. The failures include
three parser cases, all three rounding cases and the absent limits API.
`before-string-chain.log` separately captures the actual old evaluator returning
16 UTF-16 units from only five cells, without allocating a large string.
The `.cs.txt` and `.csproj.txt` retain that small reproduction harness; use the
baseline Formulas DLL as its reference, not a rebuilt current DLL.

Raw `before/after-*.jsonl` measurements include a metadata record followed by
one warmup and five samples per case/size. Each operation validates its results.
`performance-summary.json` records medians and current/baseline ratios. From the
repository root, run `python3 docs/validation/formulas-review-fixes/summarize.py`
to regenerate that JSON and print the comparison table.
Any `Failed!` lines in mutation logs are expected deliberate faults; success means
they were detected and original source bytes were restored. The ordinary final
test log must contain no failures or skips.
