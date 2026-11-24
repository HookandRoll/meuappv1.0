shader_type canvas_item;
render_mode unshaded;

uniform sampler2DArray map_attributes : filter_nearest, repeat_disable;
uniform sampler2DArray feature_data : filter_nearest, repeat_disable;
uniform sampler2DArray art_catalog : filter_nearest, repeat_disable;
uniform bool has_feature_data = false;
uniform sampler2D river_clearance : filter_linear, repeat_disable;
uniform bool has_river_clearance = false;
uniform bool natural_water_shapes = false;
uniform float elevation_color_max = 4400.0;
uniform vec2 boundary_noise_offset = vec2(0.0);
// Internal renderer round-trip check; zero during normal use.
uniform int format_probe = 0;
uniform sampler2D terrain_data : filter_nearest, repeat_disable;
uniform sampler2D region_data : filter_nearest, repeat_disable;
uniform sampler2D climate_data : filter_nearest, repeat_disable;
uniform sampler2D humidity_data : filter_nearest, repeat_disable;
uniform sampler2D humidity_rows : filter_nearest, repeat_disable;
uniform bool has_humidity = false;
uniform bool humidity_wrap = false;
uniform sampler2D detail_data : filter_nearest, repeat_disable;
uniform sampler2D lock_data : filter_nearest, repeat_disable;
uniform sampler2DArray editor_data : filter_nearest, repeat_disable;
uniform sampler2D editor_climate_data : filter_nearest, repeat_disable;
uniform int editor_band_height = 32;
uniform bool editor_preview = false;
uniform bool editor_climate = false;
uniform sampler2DArray ground_art : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2DArray relief_art : source_color, filter_linear_mipmap, repeat_disable;
uniform sampler2DArray vegetation_art : source_color, filter_linear_mipmap, repeat_disable;
uniform sampler2DArray detail_art : source_color, filter_linear_mipmap, repeat_disable;
uniform ivec2 map_size;
uniform int layer;
uniform int stage;
uniform bool horizontal_wrap;
uniform bool world_ocean_frame = false;
uniform bool atlas_flat_poles = false;
uniform bool full_atlas_domain = false;
uniform vec2 frame_noise_offset = vec2(0.0);
uniform bool show_grid;
uniform bool show_relief = true;
uniform bool show_vegetation = true;
uniform bool show_details = true;
uniform float detail_zoom : hint_range(0.0, 1.0);
uniform float tree_thinning : hint_range(0.0, 1.0) = 0.0;
uniform float mountain_zoom_scale : hint_range(0.0, 1.0) = 1.0;
uniform ivec4 selection_bounds = ivec4(-1);
uniform ivec2 brush_center = ivec2(-1);
uniform int brush_radius = 0;
uniform int brush_shape = 0;
uniform float brush_strength = 1.0;
varying vec2 map_position;

const float ROOT3 = 1.73205080757;
const int VARIANTS = 3;
// Battle Brothers moody low-fantasy water & shore palette
const vec3 OCEAN = vec3(0.082, 0.161, 0.188);
const vec3 SHALLOW = vec3(0.165, 0.294, 0.314);
const vec3 BEACH = vec3(0.655, 0.596, 0.451);
const int SHALLOW_OCEAN_ART = 10;
const int DEEP_OCEAN_ART = 13;

void vertex() {
    map_position = VERTEX;
}

ivec2 to_offset(ivec2 hex) {
    return ivec2(hex.x + (hex.y - (hex.y & 1)) / 2, hex.y);
}

ivec2 nearest_hex(vec2 axial) {
    vec3 cube = vec3(axial, -axial.x - axial.y);
    vec3 rounded = floor(cube + 0.5);
    vec3 error = abs(rounded - cube);
    if (error.x > error.y && error.x > error.z) {
        rounded.x = -rounded.y - rounded.z;
    } else if (error.y > error.z) {
        rounded.y = -rounded.x - rounded.z;
    }
    return ivec2(rounded.xy);
}

bool inside(ivec2 offset) {
    return all(greaterThanEqual(offset, ivec2(0))) && all(lessThan(offset, map_size));
}

bool within_selection(ivec2 offset) {
    return all(greaterThanEqual(offset, selection_bounds.xy)) && all(lessThan(offset, selection_bounds.zw));
}

const ivec2 HEX_NEIGHBORS[6] = {
    ivec2(1, 0), ivec2(0, 1), ivec2(-1, 1), ivec2(-1, 0), ivec2(0, -1), ivec2(1, -1)
};

// Back-to-front 2.5D painter's order across 11 neighbor hexes so large jittered mountains & trees never clip
const ivec2 PAINTER_HEXES[11] = {
    ivec2(-1, -1), ivec2(0, -1), ivec2(1, -1), ivec2(2, -1),
    ivec2(-1, 0), ivec2(0, 0), ivec2(1, 0),
    ivec2(-2, 1), ivec2(-1, 1), ivec2(0, 1), ivec2(1, 1)
};

// Enlarged mountain art reaches beyond the usual decoration neighborhood.
const ivec2 MOUNTAIN_PAINTER_HEXES[37] = {
    ivec2(0, -3), ivec2(1, -3), ivec2(2, -3), ivec2(3, -3),
    ivec2(-1, -2), ivec2(0, -2), ivec2(1, -2), ivec2(2, -2), ivec2(3, -2),
    ivec2(-2, -1), ivec2(-1, -1), ivec2(0, -1), ivec2(1, -1), ivec2(2, -1), ivec2(3, -1),
    ivec2(-3, 0), ivec2(-2, 0), ivec2(-1, 0), ivec2(0, 0), ivec2(1, 0), ivec2(2, 0), ivec2(3, 0),
    ivec2(-3, 1), ivec2(-2, 1), ivec2(-1, 1), ivec2(0, 1), ivec2(1, 1), ivec2(2, 1),
    ivec2(-3, 2), ivec2(-2, 2), ivec2(-1, 2), ivec2(0, 2), ivec2(1, 2),
    ivec2(-3, 3), ivec2(-2, 3), ivec2(-1, 3), ivec2(0, 3)
};

float brush_distance(int q, int r) {
    if (brush_shape == 0) {
        return float(max(abs(r), max(abs(q), abs(q + r))));
    }
    vec2 p = abs(vec2(float(q) + float(r) * 0.5, float(r) * 0.8660254));
    if (brush_shape == 1) {
        return length(p);
    }
    return brush_shape == 2 ? max(p.x, p.y) : p.x + p.y;
}

float distance_to_brush(ivec2 hex) {
    if (!inside(to_offset(hex)) || (selection_bounds.x >= 0 && !within_selection(to_offset(hex)))) {
        return float(brush_radius) + 1.0;
    }
    int q = hex.x - (brush_center.x - (brush_center.y - (brush_center.y & 1)) / 2);
    int r = hex.y - brush_center.y;
    float distance = brush_distance(q, r);
    if (horizontal_wrap) {
        distance = min(distance, min(brush_distance(q + map_size.x, r), brush_distance(q - map_size.x, r)));
    }
    return distance;
}

ivec2 data_offset(ivec2 hex) {
    ivec2 offset = to_offset(hex);
    if (horizontal_wrap) {
        offset.x = (offset.x + map_size.x) % map_size.x;
    }
    return offset;
}

float noise_sample(vec2 cell, float period) {
    if (horizontal_wrap) {
        cell.x = mod(cell.x, period);
    }
    vec2 p = fract(cell * vec2(0.3183099, 0.3678794));
    p += dot(p, p.yx + 19.19);
    return fract(p.x * p.y);
}

float coast_noise(vec2 position, float frequency) {
    position += boundary_noise_offset;
    float span = float(map_size.x) * ROOT3;
    float period = max(1.0, round(span * frequency));
    vec2 p = position * vec2(horizontal_wrap ? period / span : frequency, frequency);
    vec2 cell = floor(p);
    vec2 f = smoothstep(vec2(0.0), vec2(1.0), fract(p));
    return mix(
        mix(noise_sample(cell, period), noise_sample(cell + vec2(1.0, 0.0), period), f.x),
        mix(noise_sample(cell + vec2(0.0, 1.0), period), noise_sample(cell + vec2(1.0), period), f.x),
        f.y) * 2.0 - 1.0;
}

#include "res://src/GodotApp/BoundaryNoise.gdshaderinc"

// Renderer schema v2: independent IDs and metadata, decoded once per sample.
struct TerrainSample {
    vec2 height;
    int biome;
    int water;
    int relief;
    int symbols;
    float crowding;
    int landform;
};

float unpack16(vec2 bytes) {
    vec2 integer_bytes = round(bytes * 255.0);
    return dot(integer_bytes, vec2(1.0, 256.0));
}

TerrainSample sea_sample() {
    return TerrainSample(vec2(72.0, 116.0) / 255.0, 0, 1, 0, 0, 0.0, 0);
}

TerrainSample load_sample(ivec2 offset) {
    vec4 terrain = texelFetch(terrain_data, offset, 0);
    vec4 surface = texelFetch(map_attributes, ivec3(offset, 0), 0);
    vec4 geography = texelFetch(map_attributes, ivec3(offset, 1), 0);
    return TerrainSample(terrain.rg, int(unpack16(terrain.ba)), int(round(surface.r * 255.0)),
        int(unpack16(surface.gb)), int(round(surface.a * 255.0)), round(geography.b * 255.0) / 3.0, int(unpack16(geography.rg)));
}

uvec2 geographic_features(ivec2 hex) {
    if (!has_feature_data) return uvec2(0u);
    ivec2 offset = data_offset(hex);
    if (!inside(offset)) return uvec2(0u);
    uvec4 low = uvec4(round(texelFetch(feature_data, ivec3(offset, 0), 0) * 255.0));
    uvec4 high = uvec4(round(texelFetch(feature_data, ivec3(offset, 1), 0) * 255.0));
    return uvec2(low.x | (low.y << 8u) | (low.z << 16u) | (low.w << 24u),
        high.x | (high.y << 8u) | (high.z << 16u) | (high.w << 24u));
}

vec4 catalog_entry(int id, int kind) {
    return texelFetch(art_catalog, ivec3(id & 255, id >> 8, kind), 0);
}

// Decorative continuation of the real boundary, never a generated/editor hex.
// Distance to the rectangular domain rounds its corners; varying reach and
// tangent displacement turn the extended outline into bays and peninsulas.
TerrainSample frame_terrain(ivec2 raw_offset) {
    vec2 p = vec2(raw_offset);
    vec2 edge = clamp(p, vec2(0.0), vec2(map_size - 1));
    vec2 outside = p - edge;
    float distance_out = max(0.0, length(outside) - 0.5);
    vec2 physical = vec2(ROOT3 * (p.x + float(raw_offset.y & 1) * 0.5), 1.5 * p.y);
    float scale = max(6.0, float(map_size.y) * 0.18);
    vec2 noise_position = physical + frame_noise_offset;
    float broad = coast_noise(noise_position, 1.0 / (scale * 2.0));
    float detail = coast_noise(noise_position + vec2(37.0, 19.0), 1.0 / (scale * 0.65));
    float tangent_warp = smoothstep(0.0, scale * 0.45, distance_out) * scale * 0.24;
    vec2 sample_offset = edge;
    if (outside.x != 0.0) sample_offset.y += broad * tangent_warp;
    if (outside.y != 0.0) sample_offset.x += detail * tangent_warp;
    ivec2 source = clamp(ivec2(round(sample_offset)), ivec2(0), map_size - 1);
    TerrainSample data = load_sample(source);
    int bits = data.symbols;
    TerrainSample sea = sea_sample();
    if (data.water != 0) return sea;
    float elevation = dot(round(data.height * 255.0), vec2(1.0, 256.0)) - 32768.0;
    float reach = scale * max(0.18, 0.90 + broad * 0.50 + detail * 0.30)
        * (0.75 + clamp(elevation / 3500.0, 0.0, 0.45));
    vec2 origin = vec2(-ROOT3 * 0.5, -1.0);
    vec2 limit = vec2(ROOT3 * float(map_size.x), 1.5 * float(map_size.y - 1) + 1.0);
    vec2 center = (origin + limit) * 0.5;
    vec2 radii = (limit - origin) * 0.75;
    vec2 edge_physical = vec2(ROOT3 * edge.x, 1.5 * edge.y);
    vec2 direction = normalize(physical - edge_physical + vec2(0.00001));
    vec2 en = (edge_physical - center) / radii;
    vec2 dn = direction / radii;
    float qa = dot(dn, dn);
    float qb = dot(en, dn);
    float qc = dot(en, en) - 1.0;
    float ellipse_room = max(0.0, (-qb + sqrt(max(0.0, qb * qb - qa * qc))) / qa);
    if (atlas_flat_poles) {
        float polar_room = (radii.y * 0.80 - abs(edge_physical.y - center.y)) / max(0.00001, abs(direction.y));
        ellipse_room = min(ellipse_room, max(0.0, polar_room));
    }
    float units_per_hex = length(physical - edge_physical) / max(0.5, length(outside));
    float room = ellipse_room * 0.72 / max(1.0, units_per_hex);
    reach = min(reach, room * (0.75 + broad * 0.20 + detail * 0.10));
    float fine = coast_noise(noise_position, 1.0 / max(2.0, scale * 0.20));
    float coast = reach - distance_out + fine * scale * 0.045 * smoothstep(0.0, scale * 0.3, distance_out);
    if (coast <= 0.0) {
        float depth = 30.0 + 2970.0 * clamp(-coast / max(2.0, reach * 0.25), 0.0, 1.0);
        float encoded_sea = 32768.0 - depth;
        sea.height = vec2(mod(encoded_sea, 256.0), floor(encoded_sea / 256.0)) / 255.0;
        return sea;
    }
    float fade = smoothstep(0.0, max(1.5, reach * 0.7), coast);
    float encoded = clamp(elevation * fade + 32768.0, 0.0, 65535.0);
    data.height = vec2(mod(encoded, 256.0), floor(encoded / 256.0)) / 255.0;
    data.crowding = 0.0;
    if (fade < 0.35) { bits &= 128; data.relief = 1; }
    data.symbols = bits;
    return data;
}

TerrainSample sample_terrain(ivec2 hex) {
    ivec2 raw_offset = to_offset(hex);
    if (world_ocean_frame && !inside(raw_offset)) return frame_terrain(raw_offset);
    ivec2 offset = data_offset(hex);
    if (!inside(offset)) {
        return sea_sample();
    }
    return load_sample(offset);
}

// Battle Brothers earthy low-fantasy biome colors
vec3 biome_color(float id) {
    if (id == 1.0) return BEACH;
    if (id == 3.0) return vec3(0.23, 0.30, 0.16); // Temperate forest
    if (id == 4.0) return vec3(0.19, 0.29, 0.14); // Rainforest
    if (id == 5.0) return vec3(0.55, 0.47, 0.26); // Savanna
    if (id == 6.0) return vec3(0.48, 0.42, 0.27); // Steppe
    if (id == 7.0) return vec3(0.66, 0.55, 0.34); // Desert
    if (id == 8.0) return vec3(0.25, 0.31, 0.22); // Taiga
    if (id == 9.0) return vec3(0.54, 0.56, 0.51); // Tundra
    if (id == 10.0) return vec3(0.81, 0.84, 0.83); // Alpine snow
    if (id == 11.0) return vec3(0.23, 0.29, 0.19); // Wooded swamp
    if (id == 12.0) return vec3(0.80, 0.87, 0.90);
    if (id == 13.0) return vec3(0.29, 0.36, 0.17);
    if (id == 14.0) return vec3(0.49, 0.43, 0.27);
    if (id == 15.0) return vec3(0.58, 0.37, 0.25);
    if (id == 16.0) return vec3(0.18, 0.31, 0.19);
    if (id == 17.0) return vec3(0.24, 0.31, 0.22);
    if (id == 18.0) return vec3(0.40, 0.31, 0.20);
    if (id == 19.0) return vec3(0.32, 0.43, 0.23);
    if (id == 20.0) return vec3(0.22, 0.21, 0.23);
    if (id == 21.0) return vec3(0.86, 0.83, 0.77);
    return vec3(0.35, 0.41, 0.21); // Grasslands
}

int surface_bits(TerrainSample data) {
    return data.symbols;
}

int water_type(TerrainSample data) {
    return data.water;
}

int biome_id(TerrainSample data) {
    return data.biome;
}

float shore_crowding(TerrainSample data) {
    return data.crowding;
}

vec2 hex_center(ivec2 hex) {
    return vec2(ROOT3 * (float(hex.x) + 0.5 * float(hex.y)), 1.5 * float(hex.y));
}

vec2 hex_uv(ivec2 hex) {
    return (map_position - hex_center(hex)) / vec2(ROOT3, 2.0) + vec2(0.5);
}

uint vegetation_hash(ivec2 hex) {
    uvec2 cell = uvec2(data_offset(hex));
    uint value = (cell.x * 0x9e3779b9u) ^ (cell.y * 0x85ebca6bu);
    value = (value ^ (value >> 16u)) * 0x7feb352du;
    value = (value ^ (value >> 15u)) * 0x846ca68bu;
    return value ^ (value >> 16u);
}

vec3 ground_color(TerrainSample data, ivec2 hex, vec2 tile_dx, vec2 tile_dy) {
    int water = water_type(data);
    int biome = biome_id(data);
    int base_art = water != 0 ? (water == 1 ? 10 : (water == 2 ? 11 : (water == 3 ? 12 : 13))) : int(unpack16(catalog_entry(biome, 0).rg));
    int variant_a = int(vegetation_hash(hex) % uint(VARIANTS));
    int variant_b = (variant_a + 1) % VARIANTS;

    // Seamless dual-scale world-space ground sampling so hexes merge into a continuous painted canvas
    const float scale_a = 3.2;
    const float scale_b = 7.4;
    vec2 world_uv = map_position / vec2(ROOT3, 2.0);
    vec2 uv_a = fract(world_uv / scale_a);
    vec2 uv_b = fract(world_uv / scale_b + vec2(0.37, 0.61));

    vec4 tex_a = textureGrad(ground_art, vec3(uv_a, float(base_art * VARIANTS + variant_a)), tile_dx / scale_a, tile_dy / scale_a);
    vec4 tex_b = textureGrad(ground_art, vec3(uv_b, float(base_art * VARIANTS + variant_b)), tile_dx / scale_b, tile_dy / scale_b);
    vec4 texel = mix(tex_a, tex_b, 0.42);
    return mix(water != 0 ? OCEAN : biome_color(float(biome)), texel.rgb, texel.a);
}

float shelf_distance(ivec2 hex) {
    if (world_ocean_frame && !inside(to_offset(hex))) {
        TerrainSample frame_data = frame_terrain(to_offset(hex));
        float elevation = unpack16(frame_data.height) - 32768.0;
        return clamp(-elevation / 750.0, 0.0, 1.0);
    }
    ivec2 offset = clamp(data_offset(hex), ivec2(0), map_size - 1);
    return texelFetch(detail_data, offset, 0).a;
}

vec3 ocean_color(float distance_to_land, vec2 tile_dx, vec2 tile_dy) {
    float shallow = 1.0 - smoothstep(0.05, 0.95, distance_to_land);
    float footprint = max(length(tile_dx), length(tile_dy));
    shallow *= mix(1.0, 0.72, smoothstep(0.10, 0.38, footprint));

    const float repeat_a = 5.5;
    const float repeat_b = 12.0;
    vec2 world_uv = map_position / vec2(ROOT3, 2.0);
    vec2 uv_a = fract(world_uv / repeat_a);
    vec2 uv_b = fract(world_uv / repeat_b + vec2(0.23, 0.47));

    vec4 deep_a = textureGrad(ground_art, vec3(uv_a, float(DEEP_OCEAN_ART * VARIANTS)), tile_dx / repeat_a, tile_dy / repeat_a);
    vec4 deep_b = textureGrad(ground_art, vec3(uv_b, float(DEEP_OCEAN_ART * VARIANTS + 1)), tile_dx / repeat_b, tile_dy / repeat_b);
    vec4 shallow_a = textureGrad(ground_art, vec3(uv_a, float(SHALLOW_OCEAN_ART * VARIANTS)), tile_dx / repeat_a, tile_dy / repeat_a);
    vec4 shallow_b = textureGrad(ground_art, vec3(uv_b, float(SHALLOW_OCEAN_ART * VARIANTS + 1)), tile_dx / repeat_b, tile_dy / repeat_b);
    vec2 uv_c = fract(world_uv / repeat_a + vec2(0.61, 0.17));
    vec4 deep_c = textureGrad(ground_art, vec3(uv_c, float(DEEP_OCEAN_ART * VARIANTS + 2)), tile_dx / repeat_a, tile_dy / repeat_a);
    vec4 shallow_c = textureGrad(ground_art, vec3(uv_c, float(SHALLOW_OCEAN_ART * VARIANTS + 2)), tile_dx / repeat_a, tile_dy / repeat_a);
    vec3 variant_weights = vec3(0.34) + sin(vec3(coast_noise(map_position, 0.027) * 3.14159) + vec3(0.0, 2.0944, 4.1888)) * 0.30;
    variant_weights /= dot(variant_weights, vec3(1.0));

    vec3 deep_col = mix(OCEAN, deep_a.rgb * variant_weights.x + deep_b.rgb * variant_weights.y + deep_c.rgb * variant_weights.z, deep_a.a);
    vec3 shallow_col = mix(SHALLOW, shallow_a.rgb * variant_weights.x + shallow_b.rgb * variant_weights.y + shallow_c.rgb * variant_weights.z, shallow_a.a);
    return mix(deep_col, shallow_col, shallow);
}

int vegetation_index(int biome, int water) {
    if (water == 3) return 5;   // Wetlands
    if (water != 0) return -1;
    int art = int(unpack16(catalog_entry(biome, 0).ba));
    return art == 65535 ? -1 : art;
}

bool inside_art(vec2 uv) {
    return all(greaterThanEqual(uv, vec2(0.0))) && all(lessThanEqual(uv, vec2(1.0)));
}

vec4 sample_overlay(sampler2DArray atlas, int art, vec2 uv, vec2 dx, vec2 dy, float minification, float last_mip) {
    if (minification >= 1.0) {
        if (!inside_art(uv)) return vec4(0.0);
        return textureLod(atlas, vec3(0.5, 0.5, float(art)), last_mip);
    }
    vec4 texel = inside_art(uv) ? textureGrad(atlas, vec3(uv, float(art)), dx, dy) : vec4(0.0);
    if (minification > 0.0 && inside_art(uv)) {
        vec4 average = textureLod(atlas, vec3(0.5, 0.5, float(art)), last_mip);
        float opacity = mix(texel.a, average.a * texel.a, minification);
        vec3 color = mix(texel.rgb * texel.a, average.rgb * opacity, minification);
        return vec4(color / max(opacity, 0.00001), opacity);
    }
    return texel;
}

vec3 decorate_single_hex(vec3 color, ivec2 hex, bool is_current, vec2 tile_dx, vec2 tile_dy, float minification, float symbol_visibility, bool mountain_only) {
    ivec2 offset = data_offset(hex);
    bool decorative = world_ocean_frame && !inside(to_offset(hex));
    if (!inside(offset) && !decorative) return color;
    TerrainSample data = decorative ? frame_terrain(to_offset(hex)) : load_sample(offset);
    int surface = surface_bits(data);
    int relief = data.relief;
    int water = data.water;
    if ((surface & 192) == 0 && !is_current) {
        return color;
    }

    vec2 tile = hex_uv(hex);
    uint rank = vegetation_hash(hex);
    vec4 detail = vec4(0.0);
    if (!decorative && show_details && minification < 1.0) {
        detail = texelFetch(detail_data, offset, 0);
    }
    int icon_base = int(unpack16(catalog_entry(int(unpack16(detail.rg)), 2).rg));
    if (icon_base == 65535) icon_base = -1;
    bool close_icon = (int(round(detail.b * 255.0)) & 1) != 0;
    float close_reveal = smoothstep(0.43, 0.90, detail_zoom);
    float reveal = close_icon ? close_reveal : 1.0;
    bool shared_hex = close_icon ||
        (show_relief && show_vegetation && (surface & 192) == 192);
    float shared_layout = shared_hex ? smoothstep(0.30, 0.57, detail_zoom) : 0.0;

    // 1. Continuous 2.5D Overlapping Mountain Ranges & Hills (Battle Brothers organic randomized massif layout)
    // Volcano centers are drawn once by VolcanoOverlay, above this entire pass.
    bool volcano_center = (surface & 16) == 0 && (surface & 8) != 0 && (surface & 7) == 1;
    if (show_relief && (surface & 64) != 0 && !volcano_center) {
        int base_relief = (surface & 32) != 0 ? 4 : int(unpack16(catalog_entry(relief, 1).rg));
        if (data.landform == 4 && (surface & 16) != 0) base_relief = 5;
        if ((surface & 16) == 0) {
            int special = surface & 7;
            if (special >= 1 && special <= 3) base_relief = 5 + special;
        }
        bool mountain = base_relief >= 2 && base_relief <= 4;
        if (mountain_only && !mountain) return color;
        int variant = int(rank % uint(VARIANTS));
        int art = base_relief * VARIANTS + variant;

        vec2 rand2 = vec2(float(rank & 255u), float((rank >> 8u) & 255u)) / 127.5 - 1.0;
        vec2 rand_scale = vec2(
            mix(0.84, 1.28, float((rank >> 16u) & 255u) / 255.0),
            mix(0.82, 1.26, float((rank >> 24u) & 255u) / 255.0));
        float skew = (float((rank >> 12u) & 255u) / 127.5 - 1.0) * 0.18;

        // Large vertical and horizontal jitter breaks the hexagonal row alignment completely
        vec2 jitter = rand2 * vec2(0.32, 0.36);
        vec2 anchor = mix(vec2(0.5, 0.46) + jitter, vec2(0.5, 0.44) + jitter * 0.9, detail_zoom);
        vec2 size = mix(vec2(1.82, 1.56), vec2(1.66, 1.42), detail_zoom) * rand_scale;
        anchor = mix(anchor, vec2(0.5, 0.34) + jitter * 0.45, shared_layout * 0.55);
        size = mix(size, vec2(1.22, 1.06) * rand_scale, shared_layout * 0.55);
        if ((surface & 16) != 0) {
            int direction = (surface & 7) - 1;
            vec2 outward = vec2(0.0);
            if (direction == 0) outward = vec2(1.0, 0.0);
            else if (direction == 1) outward = vec2(0.5, 0.75);
            else if (direction == 2) outward = vec2(-0.5, 0.75);
            else if (direction == 3) outward = vec2(-1.0, 0.0);
            else if (direction == 4) outward = vec2(-0.5, -0.75);
            else if (direction == 5) outward = vec2(0.5, -0.75);
            anchor = vec2(0.5, 0.47) + outward * 0.18 + jitter * 0.06;
            size = vec2(1.38, 1.28) * mix(vec2(1.0), rand_scale, 0.15);
        }
        if ((surface & 8) != 0) {
            anchor = vec2(0.5, 0.46) + jitter * 0.45;
            size = vec2(1.35, 1.24) * mix(vec2(1.0), rand_scale, 0.4);
        }

        float distant_relief = 1.0 - smoothstep(0.20, 0.72, detail_zoom);
        float vertical_skew = 0.0;
        if ((surface & 8) == 0 && (base_relief >= 2 && base_relief <= 5)) {
            bool cliff = (surface & 16) != 0;
            anchor += rand2 * (cliff ? vec2(0.11, 0.19) : vec2(0.045, 0.075)) * distant_relief;
            vertical_skew = (float((rank >> 20u) & 255u) / 127.5 - 1.0) * 0.24 * distant_relief;
        }
        if (mountain) {
            // Half VolcanoOverlay.SymbolSize, converted from map units to hex UV units.
            vec2 mountain_size = mix(vec2(3.0, 2.25), vec2(0.675, 0.62), detail_zoom);
            size = mix(size, mountain_size, mountain_zoom_scale);
        }
        vec2 sample_pos = (tile - anchor) / size;
        mat2 projection = mat2(vec2(1.0, vertical_skew), vec2(skew, 1.0));
        vec2 uv = projection * sample_pos + vec2(0.5);
        if (inside_art(uv)) {
            vec4 texel = sample_overlay(relief_art, art, uv, projection * (tile_dx / size), projection * (tile_dy / size), minification, 8.0);
            float lum = dot(texel.rgb, vec3(0.299, 0.587, 0.114));
            // Preserve crisp dark ink contours & cliff shadows (lum < 0.25) and bright white snow caps (lum > 0.78),
            // while blending the grey stone/hill slopes semi-transparently with the underlying biome ground color!
            float mid_slope = smoothstep(0.12, 0.34, lum) * (1.0 - smoothstep(0.74, 0.92, lum));
            float biome_tint_strength = base_relief == 0 ? 0.76 : (base_relief == 1 ? 0.56 : (base_relief == 2 ? 0.40 : 0.26));
            vec3 biome_shaded = color * (0.46 + 1.14 * lum);
            vec3 relief_rgb = mix(texel.rgb, biome_shaded, mid_slope * biome_tint_strength);
            color = mix(color, relief_rgb, texel.a);
        }
    }

    if (mountain_only) return color;

    // 2. Dense Overlapping 2.5D Battle Brothers Forests & Scrub (randomized position breaks hex rows)
    if (show_vegetation && (surface & 128) != 0) {
        int base_veg = vegetation_index(biome_id(data), water);
        bool trees = (base_veg >= 0 && base_veg <= 3) || (base_veg >= 6 && base_veg <= 9);
        uint thinning_rank = vegetation_hash(hex + ivec2(frame_noise_offset * 1000.0));
        float tree_visibility = trees && (thinning_rank & 1u) == 0u ? 1.0 - tree_thinning : 1.0;
        if (base_veg >= 0 && tree_visibility > 0.0) {
            int variant = int((rank >> 2u) % uint(VARIANTS));
            int art = base_veg * VARIANTS + variant;

            // Close zoom gives every vegetation patch the upper-left of its hex,
            // leaving the center/right open for terrain and detail icons.
            vec2 anchor = mix(vec2(0.5, 0.48), vec2(0.28, 0.28), detail_zoom);
            vec2 size = mix(vec2(1.42, 1.24), vec2(0.72, 0.64), detail_zoom);

            vec2 offset_vec = vec2(float(rank & 255u), float((rank >> 8u) & 255u)) / 127.5 - 1.0;
            vec2 jitter = mix(vec2(0.28, 0.32), vec2(0.045, 0.04), detail_zoom);
            anchor += offset_vec * jitter;
            float scale = mix(0.88, 1.18, float((rank >> 16u) & 255u) / 255.0);
            size *= scale;
            vec2 projection = vec2((rank & 0x1000000u) == 0u ? -1.0 : 1.0, 1.0) / size;
            vec2 uv = (tile - anchor) * projection + vec2(0.5);
            if (inside_art(uv)) {
                vec4 texel = sample_overlay(vegetation_art, art, uv, tile_dx * projection, tile_dy * projection, minification, 8.0);
                color = mix(color, texel.rgb, texel.a * symbol_visibility * tree_visibility);
            }
        }
    }

    // 3. Hand-Painted Low-Fantasy Detail Icons (3 variants per icon)
    if (is_current && icon_base >= 0 && reveal > 0.0) {
        int icon_variant = int((rank >> 5u) % uint(VARIANTS));
        int icon_art = icon_base * VARIANTS + icon_variant;
        vec2 anchor = mix(vec2(0.56, 0.48), vec2(0.70, 0.48), detail_zoom * shared_layout);
        vec2 size = mix(vec2(0.68, 0.56), vec2(0.52, 0.44), detail_zoom);
        if ((surface & 0xc0) != 0) {
            size = mix(size, vec2(0.42, 0.36), shared_layout);
        }
        vec2 uv = (tile - anchor) / size + vec2(0.5);
        if (inside_art(uv)) {
            vec4 icon = textureGrad(detail_art, vec3(uv, float(icon_art)), tile_dx / size, tile_dy / size);
            color = mix(color, icon.rgb, icon.a * reveal * (1.0 - minification) * symbol_visibility);
        }
    }
    return color;
}

vec3 decorate(vec3 color, TerrainSample data, ivec2 hex, vec2 tile_dx, vec2 tile_dy, float symbol_visibility) {
    float minification = smoothstep(0.42, 0.88, max(length(tile_dx), length(tile_dy)));
    if (minification >= 0.999) {
        return decorate_single_hex(color, hex, true, tile_dx, tile_dy, minification, symbol_visibility, false);
    }
    if (mountain_zoom_scale > 0.0 && detail_zoom < 0.75) {
        for (int i = 0; i < 37; i++) {
            ivec2 offset_hex = MOUNTAIN_PAINTER_HEXES[i];
            bool usual_hex = (offset_hex.y == -1 && offset_hex.x >= -1 && offset_hex.x <= 2)
                || (offset_hex.y == 0 && abs(offset_hex.x) <= 1)
                || (offset_hex.y == 1 && offset_hex.x >= -2 && offset_hex.x <= 1);
            color = decorate_single_hex(color, hex + offset_hex, all(equal(offset_hex, ivec2(0))), tile_dx, tile_dy, minification, symbol_visibility, !usual_hex);
        }
        return color;
    }
    // Render 11-hex neighborhood in 2.5D back-to-front painter's order so mountains and forests overlap across hex borders
    for (int i = 0; i < 11; i++) {
        ivec2 offset_hex = PAINTER_HEXES[i];
        bool is_current = (i == 5);
        color = decorate_single_hex(color, hex + offset_hex, is_current, tile_dx, tile_dy, minification, symbol_visibility, false);
    }
    return color;
}

float quadratic_boundary(vec3 ordered, float endpoint) {
    float control = (4.0 * endpoint - 1.0) / 3.0;
    float t = clamp(0.5 + 0.5 * (ordered.z - ordered.y) / (1.0 - endpoint), 0.0, 1.0);
    return mix(endpoint, control, 2.0 * t * (1.0 - t));
}

float curved_shore(vec3 weights, ivec3 types, float edge) {
    bvec3 land = equal(types, ivec3(0));
    bool single_land = int(land.x) + int(land.y) + int(land.z) == 1;
    vec3 ordered = land.x == single_land ? weights.xyz
        : (land.y == single_land ? weights.yzx : weights.zxy);
    float curve = quadratic_boundary(ordered, single_land ? edge : 1.0 - edge);
    return single_land ? ordered.x - curve : curve - ordered.x;
}

float curved_biome_share(vec3 weights) {
    float interior = smoothstep(0.0, 0.19, min(weights.y, weights.z));
    float present = smoothstep(0.0, 0.19, weights.x);
    float bend = 0.5 - quadratic_boundary(weights, 0.5);
    return clamp(weights.x + bend * interior * present * 0.80, 0.0, 1.0);
}

vec3 curved_biome_shares(vec3 ids, vec3 weights) {
    if (ids.x == ids.y) {
        float single = curved_biome_share(weights.zxy);
        return vec3(1.0 - single, 1.0 - single, single);
    }
    if (ids.x == ids.z) {
        float single = curved_biome_share(weights.yzx);
        return vec3(1.0 - single, single, 1.0 - single);
    }
    if (ids.y == ids.z) {
        float single = curved_biome_share(weights.xyz);
        return vec3(single, 1.0 - single, 1.0 - single);
    }
    vec3 shares = vec3(curved_biome_share(weights.xyz), curved_biome_share(weights.yzx), curved_biome_share(weights.zxy));
    return shares / dot(shares, vec3(1.0));
}

vec3 terrain_color(vec2 axial, TerrainSample current, ivec2 current_hex, vec2 tile_dx, vec2 tile_dy) {
    ivec2 cell = ivec2(floor(axial));
    vec2 f = fract(axial);
    bool upper = f.x + f.y > 1.0;
    ivec2 hex_a = cell + ivec2(upper ? 1 : 0);
    ivec2 hex_b = cell + ivec2(1, 0);
    ivec2 hex_c = cell + ivec2(0, 1);
    TerrainSample a = sample_terrain(hex_a);
    TerrainSample b = sample_terrain(hex_b);
    TerrainSample c = sample_terrain(hex_c);
    vec3 weights = upper
        ? vec3(f.x + f.y - 1.0, 1.0 - f.y, 1.0 - f.x)
        : vec3(1.0 - f.x - f.y, f.x, f.y);
    vec3 heights = vec3(unpack16(a.height), unpack16(b.height), unpack16(c.height)) - 32768.0;
    ivec3 types = ivec3(water_type(a), water_type(b), water_type(c));
    int current_type = water_type(current);
    vec3 dry_weights = weights * vec3(equal(types, ivec3(0)));
    float ocean_fraction = dot(weights, vec3(equal(types, ivec3(1))));
    float lake_fraction = dot(weights, vec3(equal(types, ivec3(2))));
    int shore_type = current_type == 0 ? (ocean_fraction >= lake_fraction ? 1 : 2) : current_type;
    vec3 water_weights = weights * vec3(equal(types, ivec3(shore_type)));
    float land_fraction = dot(dry_weights, vec3(1.0));
    float water_fraction = dot(water_weights, vec3(1.0));
    // Evaluate the same coast on both sides of the hex edge. The bounded
    // feather and sand band fall off before reaching a one-hex island's core.
    bool shore = (shore_type == 1 || shore_type == 2)
        && land_fraction > 0.0 && water_fraction > 0.0 && land_fraction + water_fraction > 0.99999;

    vec3 water = vec3(0.0);
    if (current_type != 0 || shore) {
        if (shore_type == 1) {
            float distance_to_land = dot(weights, vec3(shelf_distance(hex_a), shelf_distance(hex_b), shelf_distance(hex_c)));
            water = ocean_color(distance_to_land, tile_dx, tile_dy);
        } else if (shore && shore_type == 2) {
            vec3 lake_a = water_weights.x > 0.0 ? ground_color(a, hex_a, tile_dx, tile_dy) : vec3(0.0);
            vec3 lake_b = water_weights.y > 0.0 ? ground_color(b, hex_b, tile_dx, tile_dy) : vec3(0.0);
            vec3 lake_c = water_weights.z > 0.0 ? ground_color(c, hex_c, tile_dx, tile_dy) : vec3(0.0);
            water = (lake_a * water_weights.x + lake_b * water_weights.y + lake_c * water_weights.z) / water_fraction;
        } else {
            water = ground_color(current, current_hex, tile_dx, tile_dy);
        }
        if (!shore) {
            return water;
        }
    }

    vec3 ids = vec3(float(biome_id(a)), float(biome_id(b)), float(biome_id(c)));
    bool blend_biomes = stage == 3 && (
        (ids.x != ids.y && dry_weights.x * dry_weights.y > 0.0) ||
        (ids.x != ids.z && dry_weights.x * dry_weights.z > 0.0) ||
        (ids.y != ids.z && dry_weights.y * dry_weights.z > 0.0));
    vec2 noise = vec2(0.0);
    float footprint = max(length(tile_dx), length(tile_dy));
    if (shore || blend_biomes) {
        noise = vec2(coast_noise(map_position, 0.31),
            coast_noise(map_position, 1.24) * (1.0 - smoothstep(0.18, 0.70, footprint)));
    }
    float elevation = dot(dry_weights, heights) / max(land_fraction, 0.00001);
    vec3 art_a = dry_weights.x > 0.0 ? ground_color(a, hex_a, tile_dx, tile_dy) : vec3(0.0);
    vec3 art_b = dry_weights.y > 0.0 ? ground_color(b, hex_b, tile_dx, tile_dy) : vec3(0.0);
    vec3 art_c = dry_weights.z > 0.0 ? ground_color(c, hex_c, tile_dx, tile_dy) : vec3(0.0);
    if (blend_biomes) {
        vec3 same = vec3(float(ids.x == ids.y), float(ids.x == ids.z), float(ids.y == ids.z));
        vec3 biome_weights = dry_weights + vec3(
            dot(dry_weights.yz, same.xy),
            dot(dry_weights.xz, same.xz),
            dot(dry_weights.xy, same.yz));
        vec3 share = biome_weights / land_fraction;
        vec3 curved = mix(share, curved_biome_shares(ids, dry_weights / land_fraction), 0.12);
        vec3 phase = ids * 2.39996;
        vec3 shift = noise.x * sin(phase) * 0.16 + noise.y * cos(phase) * 0.08;
        shift += boundary_roughness(map_position, footprint) * sin(phase + 1.3) * 0.32;
        curved += 4.0 * share * (1.0 - share) * shift;
        float width = clamp(0.18 + noise.x * 0.06 + footprint * 0.10, 0.10, 0.30);
        vec3 feather = smoothstep(vec3(0.5 - width), vec3(0.5 + width), curved);
        dry_weights *= max(feather, share * 0.05) / max(share, vec3(0.00001));
    }
    vec3 land = (art_a * dry_weights.x + art_b * dry_weights.y + art_c * dry_weights.z)
        / max(dot(dry_weights, vec3(1.0)), 0.00001);

    // Topographic directional hillshading from elevation gradients + altitude brightness
    float slope_light = clamp(((heights.x - heights.y) * (upper ? -0.6 : 0.6) + (heights.x - heights.z) * (upper ? -0.5 : 0.5)) * 0.00018, -0.10, 0.10);
    land *= mix(0.93, 1.09, smoothstep(0.0, 4200.0, elevation)) + slope_light;
    if (!shore) {
        return land;
    }

    // Battle Brothers coastal transition: sandy-pebble beach bank & dark wet-shore rim
    float crowding = dot(weights, vec3(shore_crowding(a), shore_crowding(b), shore_crowding(c)));
    float tight = smoothstep(0.13, 0.68, crowding);
    float feather = clamp(0.020 + footprint * 0.25, 0.020, 0.09);
    vec2 warped = map_position + noise * 0.31;
    float contour = coast_noise(warped, 0.73);
    float fine = coast_noise(warped, 2.45) * (1.0 - smoothstep(0.09, 0.34, footprint));
    float edge = clamp(mix(0.35, 0.43, tight) + 0.105 * noise.x + 0.065 * contour
        + 0.045 * noise.y + 0.026 * fine, 0.19, 0.49);
    edge = clamp(edge + boundary_roughness(map_position, footprint) * 0.14, 0.14, 0.55);
    float bend = clamp(0.32 + 0.30 * noise.x - 0.23 * contour, 0.04, 0.76);
    if (natural_water_shapes) bend *= any(equal(types, ivec3(2))) ? 0.35 : 0.70;
    bend *= 0.45;
    float curve = mix(land_fraction - edge, curved_shore(weights, types, edge), bend);
    float strip = smoothstep(-feather, feather, curve);

    // Warm sandy-pebble shoreline bank right along the land-water interface (like Battle Brothers Image 5)
    float sand_band = exp(-pow((curve - 0.035) / max(0.065, feather * 1.6), 2.0));
    float dark_rim = exp(-pow((curve + 0.015) / max(0.032, feather * 0.9), 2.0));
    vec3 shore_sand = vec3(0.73, 0.68, 0.53) * (0.94 + 0.08 * contour);
    vec3 rimmed_land = mix(land, shore_sand, sand_band * 0.68);
    vec3 rimmed_water = water * (1.0 - dark_rim * 0.24);
    return mix(rimmed_water, rimmed_land, strip);
}

void fragment() {
    float symbol_visibility = has_river_clearance ? 1.0 - texture(river_clearance, SCREEN_UV).a : 1.0;
    vec2 map_dx = dFdx(map_position);
    vec2 map_dy = dFdy(map_position);
    vec2 tile_dx = map_dx / vec2(ROOT3, 2.0);
    vec2 tile_dy = map_dy / vec2(ROOT3, 2.0);
    float r = map_position.y * (2.0 / 3.0);
    vec2 axial = vec2(map_position.x / ROOT3 - r * 0.5, r);
    ivec2 hex = nearest_hex(axial);
    ivec2 offset = to_offset(hex);
    vec3 color = vec3(0.055, 0.047, 0.038);

    if (inside(offset)) {
        if (editor_preview && layer != 2 && layer != 3 && layer != 7 && layer != 8) {
            ivec3 preview_offset = ivec3(offset.x, offset.y % editor_band_height, offset.y / editor_band_height);
            vec4 data = texelFetch(editor_data, preview_offset, 0);
            vec4 surface = texelFetch(editor_data, preview_offset + ivec3(0, editor_band_height, 0), 0);
            vec4 geography = texelFetch(editor_data, preview_offset + ivec3(0, editor_band_height * 2, 0), 0);
            int water = int(round(surface.r * 255.0));
            int relief = int(unpack16(surface.gb));
            int locks = int(round(geography.a * 255.0));
            float elevation = unpack16(data.rg) - 32768.0;
            if (layer == 5) {
                color = locks == 2 ? vec3(0.69, 0.31, 0.37)
                    : locks == 1 ? vec3(0.72, 0.53, 0.24) : vec3(0.18, 0.16, 0.13);
            } else if (stage == 0) {
                color = vec3(0.14, 0.12, 0.10);
            } else if (layer == 1) {
                color = mix(vec3(0.09, 0.14, 0.22), vec3(0.96, 0.87, 0.62),
                    clamp((elevation + 2400.0) / (elevation_color_max + 2400.0), 0.0, 1.0));
            } else if (water == 0) {
                color = biome_color(unpack16(data.ba));
                if (layer == 0) {
                    color *= 0.83 + float(relief) * 0.095 + clamp(elevation * 0.00008, -0.12, 0.18);
                }
            } else {
                color = water == 3 ? vec3(0.28, 0.29, 0.19) : water == 2 ? vec3(0.16, 0.29, 0.31) : OCEAN;
            }
        } else if (stage == 0 && layer != 5) {
            color = vec3(0.14, 0.12, 0.10);
        } else if (layer == 0) {
            TerrainSample data = sample_terrain(hex);
            color = decorate(terrain_color(axial, data, hex, tile_dx, tile_dy), data, hex, tile_dx, tile_dy, symbol_visibility);
        } else if (layer == 1 || layer == 4) {
            TerrainSample data = sample_terrain(hex);
            float elevation = unpack16(data.height) - 32768.0;
            color = layer == 1
                ? mix(vec3(0.09, 0.14, 0.22), vec3(0.96, 0.87, 0.62), clamp((elevation + 2400.0) / (elevation_color_max + 2400.0), 0.0, 1.0))
                : (water_type(data) != 0 ? OCEAN : biome_color(float(biome_id(data))));
        } else if (layer == 2 || layer == 3) {
            vec4 data = editor_climate ? texelFetch(editor_climate_data, offset, 0) : texelFetch(climate_data, offset, 0);
            color = layer == 2
                ? mix(vec3(0.23, 0.49, 0.69), vec3(0.94, 0.46, 0.24), clamp((unpack16(data.rg) - 32468.0) / 650.0, 0.0, 1.0))
                : mix(vec3(0.75, 0.56, 0.35), vec3(0.14, 0.42, 0.63), clamp(unpack16(data.ba) / 3000.0, 0.0, 1.0));
        } else if (layer == 11) {
            color = vec3(0.22, 0.23, 0.24);
            if (has_humidity) {
                vec2 bounds = texelFetch(humidity_rows, ivec2(0, offset.y), 0).rg;
                ivec2 size = textureSize(humidity_data, 0);
                vec2 pos = vec2((float(offset.x) - bounds.x + 0.5) / bounds.y,
                    (float(offset.y) + 0.5) / float(map_size.y)) * vec2(size) - 0.5;
                ivec2 low = ivec2(floor(pos));
                ivec2 high = low + ivec2(1);
                low.y = clamp(low.y, 0, size.y - 1);
                high.y = clamp(high.y, 0, size.y - 1);
                low.x = humidity_wrap ? (low.x % size.x + size.x) % size.x : clamp(low.x, 0, size.x - 1);
                high.x = humidity_wrap ? (high.x % size.x + size.x) % size.x : clamp(high.x, 0, size.x - 1);
                float bottom = mix(texelFetch(humidity_data, low, 0).r,
                    texelFetch(humidity_data, ivec2(high.x, low.y), 0).r, fract(pos.x));
                float top = mix(texelFetch(humidity_data, ivec2(low.x, high.y), 0).r,
                    texelFetch(humidity_data, high, 0).r, fract(pos.x));
                float rh = clamp(mix(bottom, top, fract(pos.y)), 0.0, 1.0);
                vec3 dry = vec3(0.75, 0.12, 0.16);
                vec3 middle = vec3(0.96, 0.92, 0.82);
                vec3 wet = vec3(0.08, 0.30, 0.78);
                color = rh < 0.5 ? mix(dry, middle, rh * 2.0) : mix(middle, wet, (rh - 0.5) * 2.0);
            }
        } else if (layer == 7) {
            color = texelFetch(region_data, offset, 0).rgb;
        } else if (layer == 8) {
            TerrainSample data = sample_terrain(hex);
            int water = water_type(data);
            float elevation = max(0.0, unpack16(data.height) - 32768.0);
            color = water == 1 || water == 2 ? vec3(0.09, 0.17, 0.22)
                : mix(vec3(0.22, 0.24, 0.22), vec3(0.38, 0.37, 0.32), clamp(elevation / 3500.0, 0.0, 1.0));
        } else {
            float locks = round(texelFetch(lock_data, offset, 0).r * 255.0);
            color = locks >= 2.0 ? vec3(0.69, 0.31, 0.37)
                : locks >= 1.0 ? vec3(0.72, 0.53, 0.24) : vec3(0.18, 0.16, 0.13);
        }

        vec2 local = abs(map_position - hex_center(hex));
        float edge_distance = ROOT3 * 0.5 - max(local.x, 0.5 * local.x + ROOT3 * 0.5 * local.y);
        float units_per_pixel = max(length(map_dx), length(map_dy));
        float edge = 1.0 - smoothstep(0.0, units_per_pixel * 1.3, edge_distance);
        if (show_grid) {
            float visibility = 1.0 - smoothstep(0.13, 0.35, units_per_pixel);
            color = mix(color, vec3(0.09, 0.08, 0.06), 0.55 * edge * visibility);
        }
        bool selected = within_selection(offset);
        float brush_distance_at_hex = brush_center.x >= 0 ? distance_to_brush(hex) : float(brush_radius) + 1.0;
        bool brushed = brush_distance_at_hex <= float(brush_radius) + 0.0001;
        if (selected || brushed) {
            float selection_edge = 0.0;
            float brush_edge = 0.0;
            vec2 center = hex_center(hex);
            for (int i = 0; i < 6; i++) {
                ivec2 neighbor = hex + HEX_NEIGHBORS[i];
                vec2 normal = normalize(hex_center(neighbor) - center);
                float side = ROOT3 * 0.5 - dot(map_position - center, normal);
                float opacity = 1.0 - smoothstep(0.0, units_per_pixel * 1.6, side);
                if (selected && !within_selection(to_offset(neighbor))) {
                    selection_edge = max(selection_edge, opacity);
                }
                if (brushed && distance_to_brush(neighbor) > float(brush_radius) + 0.0001) {
                    brush_edge = max(brush_edge, opacity);
                }
            }
            if (selected) {
                color = mix(color, vec3(0.92, 0.75, 0.38), 0.22 + 0.78 * selection_edge);
            }
            if (brushed) {
                float weight = mix(1.0, brush_strength, brush_distance_at_hex / float(max(brush_radius, 1)));
                color = mix(color, vec3(0.86, 0.78, 0.52), 0.22 * weight + 0.68 * brush_edge);
            }
        }
    }
    float alpha = 1.0;
    if (world_ocean_frame && !inside(offset)) {
        // Decorative coast and water: no extra gameplay/editor data is created.
        TerrainSample frame_data = sample_terrain(hex);
        color = stage > 0 ? decorate(terrain_color(axial, frame_data, hex, tile_dx, tile_dy), frame_data, hex, tile_dx, tile_dy, symbol_visibility) : OCEAN;
        vec2 data_origin = vec2(-ROOT3 * 0.5, -1.0);
        vec2 data_limit = vec2(ROOT3 * float(map_size.x), 1.5 * float(map_size.y - 1) + 1.0);
        vec2 center = (data_origin + data_limit) * 0.5;
        vec2 radii = (data_limit - data_origin) * 0.75;
        float radius = length((map_position - center) / radii);
        alpha = 1.0 - smoothstep(0.995, 1.0, radius);
        if (atlas_flat_poles) {
            float polar = abs(map_position.y - center.y) / radii.y;
            alpha *= 1.0 - smoothstep(0.797, 0.800, polar);
        }
    }
    if (full_atlas_domain) {
        vec2 atlas = (map_position / vec2(ROOT3, 1.5) + vec2(0.5)) / vec2(map_size) * 2.0 - 1.0;
        float outline = max(abs(atlas.y), length(vec2(atlas.x, atlas.y * 0.8)));
        float edge_width = max(length(fwidth(atlas)), 0.0001);
        alpha = 1.0 - smoothstep(1.0 - edge_width, 1.0, outline);
    }
    if (format_probe > 0 && inside(offset)) {
        TerrainSample probe = sample_terrain(hex);
        uvec2 features = geographic_features(hex);
        ivec3 bytes = format_probe == 1 ? ivec3(probe.biome & 255, probe.biome >> 8, probe.water)
            : (format_probe == 2 ? ivec3(probe.relief & 255, probe.relief >> 8, probe.landform & 255)
            : ivec3(probe.landform >> 8, int(features.x & 255u), int(features.y >> 24u)));
        color = vec3(bytes) / 255.0; alpha = 1.0;
    }
    COLOR = vec4(color, alpha);
}
