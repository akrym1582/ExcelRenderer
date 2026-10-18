// HarfBuzz is Old MIT; see the adjacent COPYING file. This adapter is MIT.
#include "hb.h"
#include "hb-subset.h"
#ifdef _WIN32
#define ER_EXPORT extern "C" __declspec(dllexport)
#else
#define ER_EXPORT extern "C" __attribute__((visibility("default")))
#endif
ER_EXPORT hb_blob_t* er_subset(const char* data, unsigned length,
                              const hb_codepoint_t* glyphs, unsigned count) {
    hb_blob_t* blob = hb_blob_create(data, length, HB_MEMORY_MODE_DUPLICATE, nullptr, nullptr);
    hb_face_t* face = hb_face_create(blob, 0);
    hb_subset_input_t* input = hb_subset_input_create_or_fail();
    if (!input) { hb_face_destroy(face); hb_blob_destroy(blob); return nullptr; }
    hb_set_t* set = hb_subset_input_glyph_set(input);
    hb_set_clear(set);
    for (unsigned i = 0; i < count; ++i) hb_set_add(set, glyphs[i]);
    hb_subset_input_set_flags(input, HB_SUBSET_FLAGS_RETAIN_GIDS | HB_SUBSET_FLAGS_NOTDEF_OUTLINE);
    hb_face_t* subset = hb_subset_or_fail(face, input);
    hb_blob_t* result = subset ? hb_face_reference_blob(subset) : nullptr;
    if (subset) hb_face_destroy(subset);
    hb_subset_input_destroy(input);
    hb_face_destroy(face);
    hb_blob_destroy(blob);
    return result;
}
ER_EXPORT const char* er_blob_data(hb_blob_t* blob, unsigned* length) {
    return hb_blob_get_data(blob, length);
}
ER_EXPORT void er_blob_destroy(hb_blob_t* blob) { hb_blob_destroy(blob); }
ER_EXPORT const char* er_version() { return hb_version_string(); }
