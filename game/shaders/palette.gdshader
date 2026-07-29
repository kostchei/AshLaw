shader_type canvas_item;
render_mode unshaded;

// Indexed-colour lookup, per build plan section 2.6.
//
// index_tex   : Image.Format.R8, one byte per pixel holding a palette index.
// palette_tex : 256x1 RGBA8.
//
// BOTH textures must be imported with nearest filtering, no mipmaps, and no
// repeat. Linear filtering on either one silently blends palette indices, which
// produces colours that are in no palette at all.
//
// The +0.5 half-texel offset is mandatory. Without it the sample lands exactly on
// the boundary between two texels and the driver decides which one you get.
// Ash.Core.PaletteShaderModel replicates this arithmetic and
// PaletteShaderModelTests asserts every one of the 256 indices samples its texel
// centre, so a regression here fails in CI rather than on a particular GPU.

uniform sampler2D palette_tex : filter_nearest, repeat_disable;

const float PALETTE_WIDTH = 256.0;
const float INDEX_MAX = 255.0;

void fragment() {
    float idx = texture(TEXTURE, UV).r;

    // Index 0 is the transparency key.
    if (idx < 0.5 / INDEX_MAX) {
        discard;
    }

    vec2 pal_uv = vec2((idx * INDEX_MAX + 0.5) / PALETTE_WIDTH, 0.5);
    COLOR = texture(palette_tex, pal_uv);
}
