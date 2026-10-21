# Planet names

`PlanetNames` combines a starting syllable, an occasional middle syllable, and
an ending. For example, `Tar` + `lon` produces **Tarlon**. The syllable lists live
in `Assets/MassiveArmy/Runtime/Planetary/Celestial/PlanetNames.cs` and can be
extended directly.

Names depend on the world seed and planet ordinal, with deterministic retries
to avoid duplicates within a generated galaxy. The naming stream is independent
of planet generation: it does not change terrain, resource distribution,
orbital parameters, or Unity's global random state. Authored home-world names
are retained; generic placeholders receive generated names. Internal body IDs
and saved references are unchanged.

Both galaxy and legacy local-system generation use these names. The known-world
selector and orbital manifests display names while continuing to use stable
body IDs internally. The world selector remains mounted outside the periodically
refreshed economy ledger, preserving an open dropdown and its selection.

Validation: 1,536 unique names per seed across four seeds (including negative
seeds), deterministic regeneration, readable lengths, English letters, and
preservation of authored names. Results are retained in
`TestResults/Planetary/Names/validation.json`. The UI integration run is retained
in `TestResults/Planetary/UiWorldFix/Gameplay/validation.json`.
