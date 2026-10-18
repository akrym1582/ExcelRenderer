# Managed TrueType subset validation

Starting main: `9157b80788d590694ef90d04dbaea9872773c6d6` (fetched from origin).
Branch: `feature/managed-truetype-subset`. This directory records the current
implementation validation; historic native-adapter reports are not reused as proof.

The final results and conditions are recorded in this directory after the clean
build, regression suites, isolated consumers and repeatable measurements complete.
See the [implemented font contract](../../managed-truetype-subset.md),
[Japanese guide](../../managed-truetype-subset.ja.md), and the reproducible
`scripts/validate-managed-fonts.py` / `scripts/benchmark-managed-fonts.py` harnesses.
