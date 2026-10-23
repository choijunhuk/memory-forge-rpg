# Comparison scope of controlled Editor probes

Raw probe receipts retain their original field names. A few private QA snippets used `CanonicalState.Hash` and informally named the result "fullState"/"fullMirror"/"fullCreate". CanonicalState deliberately omits log/ev keys; those particular scalars establish canonical gameplay state equality, not an independent log/event comparison. The canonizer is unchanged.

The actual NUnit evidence is stronger: ExpeditionClient/TurnFlow/Picker tests use Fingerprint with the Run log plus battle log/events hashed separately; Failure tests use complete JSON Run graphs and the whole session store. S2full119 and initial S3new7 passed with these explicit complete-state checks. Private probes complement that proof and do not replace it. Later actual scene probes should compare complete JSON text/trees when claiming whole-graph equality. No expected state was injected in either path.
