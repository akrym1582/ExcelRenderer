# Original font format fixtures

These six tiny fonts contain original geometric test glyphs under the repository
MIT license. They contain no third-party outlines. They were copied byte-for-byte
from the format fixtures at commit 513ddd4d424d9afa6e92ed154abdc631841ba302 so that
Core owns its inputs independently. SHA256SUMS in the parent directory pins them.
No generator or external script is needed to build or run the tests.

static.ttf, cff.otf and variable.ttf distinguish supported static TrueType from
unsupported CFF and variable formats. collection-distinct.ttc and collection-v2.ttc
share outlines but have distinct metrics/mappings. collection-mixed.ttc contains
static, CFF, variable and COLR v0/v1 faces. Tests cover nonzero collection indices,
shared tables, checksum regeneration and unsupported selected faces.
