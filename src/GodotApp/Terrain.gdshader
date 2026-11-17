shader_type canvas_item;
render_mode unshaded;

uniform sampler2D terrain_data : filter_nearest, repeat_disable;
uniform sampler2D climate_data : filter_nearest, repeat_disable;
uniform sampler2DArray ground_art : source_color, filter_linear_mipmap, repeat_disable;
uniform sampler2DArray relief_art : source_color, filter_linear_mipmap, repeat_disable;
uniform sampler2DArray vegetation_art : source_color, filter_linear_mipmap, repeat_disable;
uniform ivec2 map_size;
uniform int layer;
uniform int stage;
uniform bool classified_beaches;
uniform bool show_grid;
uniform bool show_relief = true;
uniform bool show_vegetation = true;
uniform float detail_zoom : hint_range(0.0, 1.0);
uniform ivec2 selected = ivec2(-1);
varying vec2 map_position;

const float ROOT3 = 1.73205080757;
const vec3 OCEAN = vec3(0.065, 0.19, 0.24);
const vec3 SHALLOW = vec3(0.19, 0.43, 0.43);
const vec3 BEACH = vec3(0.73, 0.67, 0.46);

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

vec4 sample_terrain(ivec2 hex) {
    ivec2 offset = to_offset(hex);
    if (!inside(offset)) {
        return vec4(0.0, 0.0, 0.0, 1.0 / 255.0);
    }
    return texelFetch(terrain_data, offset, 0);
}

float unpack16(vec2 bytes) {
    vec2 integer_bytes = round(bytes * 255.0);
    return dot(integer_bytes, vec2(1.0, 256.0));
}

vec3 biome_color(float id) {
    if (id == 1.0) return BEACH;
    if (id == 3.0) return vec3(0.26, 0.43, 0.30);
    if (id == 4.0) return vec3(0.19, 0.38, 0.28);
    if (id == 5.0) return vec3(0.67, 0.62, 0.35);
    if (id == 6.0) return vec3(0.60, 0.54, 0.39);
    if (id == 7.0) return vec3(0.73, 0.59, 0.41);
    if (id == 8.0) return vec3(0.37, 0.49, 0.41);
    if (id == 9.0) return vec3(0.67, 0.70, 0.64);
    if (id == 10.0) return vec3(0.75, 0.73, 0.66);
    return vec3(0.49, 0.55, 0.33);
}

int surface_bits(vec4 data) {
    return int(round(data.a * 255.0));
}

int water_type(vec4 data) {
    return surface_bits(data) & 3;
}

vec2 hex_uv(ivec2 hex) {
    vec2 center = vec2(ROOT3 * (float(hex.x) + 0.5 * float(hex.y)), 1.5 * float(hex.y));
    return (map_position - center) / vec2(ROOT3, 2.0) + vec2(0.5);
}

vec3 ground_color(vec4 data, ivec2 hex, vec2 tile_dx, vec2 tile_dy) {
    int water = water_type(data);
    int biome = int(round(data.b * 255.0));
    int art = water != 0 ? 9 + water : (biome == 0 ? 1 : clamp(biome - 1, 0, 9));
    vec4 texel = textureGrad(ground_art, vec3(clamp(hex_uv(hex), vec2(0.0), vec2(1.0)), float(art)), tile_dx, tile_dy);
    return mix(water != 0 ? OCEAN : biome_color(float(biome)), texel.rgb, texel.a);
}

int vegetation_index(int biome, int water) {
    if (water == 3) return 5;   // Wetlands
    if (water != 0) return -1;
    if (biome == 3) return 0;   // Temperate forest
    if (biome == 4) return 1;   // Rainforest
    if (biome == 8) return 2;   // Taiga
    if (biome == 5) return 3;   // Savanna
    if (biome == 7) return 4;   // Desert
    return -1;
}

bool inside_art(vec2 uv) {
    return all(greaterThanEqual(uv, vec2(0.0))) && all(lessThanEqual(uv, vec2(1.0)));
}

vec4 sample_overlay(sampler2DArray atlas, int art, vec2 uv, vec2 dx, vec2 dy, float minification, float last_mip) {
    // A subpixel hex can contribute the average of its illustration without flickering across the map.
    if (minification >= 1.0) {
        return textureLod(atlas, vec3(0.5, 0.5, float(art)), last_mip);
    }
    vec4 texel = inside_art(uv) ? textureGrad(atlas, vec3(uv, float(art)), dx, dy) : vec4(0.0);
    if (minification > 0.0) {
        vec4 average = textureLod(atlas, vec3(0.5, 0.5, float(art)), last_mip);
        // Mix premultiplied colors here to keep the transparent margin from darkening an edge.
        float opacity = mix(texel.a, average.a, minification);
        vec3 color = mix(texel.rgb * texel.a, average.rgb * average.a, minification);
        return vec4(color / max(opacity, 0.00001), opacity);
    }
    return texel;
}

vec3 decorate(vec3 color, vec4 data, ivec2 hex, vec2 tile_dx, vec2 tile_dy) {
    int surface = surface_bits(data);
    int relief = (surface >> 2) & 7;
    int water = surface & 3;
    vec2 tile = hex_uv(hex);
    float minification = smoothstep(0.25, 0.55, max(length(tile_dx), length(tile_dy)));
    if (show_relief && (surface & 64) != 0) {
        int art = (surface & 32) != 0 ? 4 : relief - 1;
        vec2 anchor = mix(vec2(0.5), vec2(0.5, 0.274), detail_zoom);
        vec2 size = mix(vec2(0.97, 0.84), vec2(0.54, 0.455), detail_zoom);
        vec2 uv = (tile - anchor) / size + vec2(0.5);
        vec4 texel = sample_overlay(relief_art, art, uv, tile_dx / size, tile_dy / size, minification, 8.0);
        color = mix(color, texel.rgb, texel.a);
    }
    if (show_vegetation && (surface & 128) != 0) {
        int art = vegetation_index(int(round(data.b * 255.0)), water);
        if (art >= 0) {
            vec2 anchor = mix(vec2(0.5), vec2(0.29, 0.35), detail_zoom);
            vec2 size = mix(vec2(0.97, 0.84), vec2(0.32, 0.278), detail_zoom);
            vec2 uv = (tile - anchor) / size + vec2(0.5);
            vec4 texel = sample_overlay(vegetation_art, art, uv, tile_dx / size, tile_dy / size, minification, 7.0);
            color = mix(color, texel.rgb, texel.a);
        }
    }
    return color;
}

vec3 terrain_color(vec2 axial, vec2 tile_dx, vec2 tile_dy) {
    ivec2 cell = ivec2(floor(axial));
    vec2 f = fract(axial);
    bool upper = f.x + f.y > 1.0;
    ivec2 hex_a = cell + ivec2(upper ? 1 : 0);
    ivec2 hex_b = cell + ivec2(1, 0);
    ivec2 hex_c = cell + ivec2(0, 1);
    vec4 a = sample_terrain(hex_a);
    vec4 b = sample_terrain(hex_b);
    vec4 c = sample_terrain(hex_c);
    vec3 weights = upper
        ? vec3(f.x + f.y - 1.0, 1.0 - f.y, 1.0 - f.x)
        : vec3(1.0 - f.x - f.y, f.x, f.y);
    vec3 heights = vec3(unpack16(a.rg), unpack16(b.rg), unpack16(c.rg)) - 32768.0;
    float elevation = dot(weights, heights);
    vec3 occupancy = vec3(float(water_type(a) == 0), float(water_type(b) == 0), float(water_type(c) == 0));
    float land_fraction = dot(weights, occupancy);
    if (stage >= 2 && (occupancy.x != occupancy.y || occupancy.x != occupancy.z)) {
        vec3 distances = clamp(abs(heights), vec3(1.0), vec3(260.0)) * (2.0 * occupancy - 1.0);
        float contour = 0.5 + 0.5 * dot(weights, distances) / dot(weights, abs(distances));
        land_fraction = mix(land_fraction, contour, 0.88);
    }
    vec3 ids = round(vec3(a.b, b.b, c.b) * 255.0);
    vec3 sand_ids = 1.0 - step(vec3(0.5), abs(ids - vec3(1.0)));
    float sand_fraction = dot(weights, sand_ids);
    vec3 art_a = ground_color(a, hex_a, tile_dx, tile_dy);
    vec3 art_b = ground_color(b, hex_b, tile_dx, tile_dy);
    vec3 art_c = ground_color(c, hex_c, tile_dx, tile_dy);
    vec3 dry_weights = weights * occupancy;
    vec3 wet_weights = weights * (1.0 - occupancy);
    vec3 land = (art_a * dry_weights.x + art_b * dry_weights.y + art_c * dry_weights.z)
        / max(dot(dry_weights, vec3(1.0)), 0.00001);
    land *= mix(0.96, 1.07, smoothstep(0.0, 4200.0, elevation));
    vec3 water = (art_a * wet_weights.x + art_b * wet_weights.y + art_c * wet_weights.z)
        / max(dot(wet_weights, vec3(1.0)), 0.00001);
    vec3 depth_color = mix(OCEAN, SHALLOW, 0.55 * smoothstep(-2300.0, 0.0, elevation) + 0.45 * land_fraction);
    water = mix(water, depth_color, 0.20);
    float sand = classified_beaches
        ? smoothstep(0.08, 0.46, sand_fraction)
        : 0.65 * (1.0 - smoothstep(15.0, 170.0, elevation));
    vec3 rock = mix(land, vec3(0.43, 0.42, 0.37), 0.48);
    vec3 shore = mix(rock, BEACH, sand);
    float shore_width = mix(0.085, 0.27, sand);
    vec3 coast = mix(shore, land, smoothstep(0.50 + shore_width * 0.30, 0.50 + shore_width, land_fraction));
    return mix(water, coast, smoothstep(0.45, 0.56, land_fraction));
}

void fragment() {
    vec2 map_dx = dFdx(map_position);
    vec2 map_dy = dFdy(map_position);
    vec2 tile_dx = map_dx / vec2(ROOT3, 2.0);
    vec2 tile_dy = map_dy / vec2(ROOT3, 2.0);
    float r = map_position.y * (2.0 / 3.0);
    vec2 axial = vec2(map_position.x / ROOT3 - r * 0.5, r);
    ivec2 hex = nearest_hex(axial);
    ivec2 offset = to_offset(hex);
    vec3 color = vec3(0.038, 0.068, 0.083);

    if (inside(offset)) {
        if (stage == 0 && layer != 5) {
            color = vec3(0.095, 0.15, 0.17);
        } else if (layer == 0) {
            color = decorate(terrain_color(axial, tile_dx, tile_dy), sample_terrain(hex), hex, tile_dx, tile_dy);
        } else if (layer == 1 || layer == 4) {
            vec4 data = sample_terrain(hex);
            float elevation = unpack16(data.rg) - 32768.0;
            color = layer == 1
                ? mix(vec3(0.09, 0.14, 0.22), vec3(0.96, 0.87, 0.62), clamp((elevation + 2400.0) / 6800.0, 0.0, 1.0))
                : (water_type(data) != 0 ? OCEAN : biome_color(round(data.b * 255.0)));
        } else if (layer == 2 || layer == 3) {
            vec4 data = texelFetch(climate_data, offset, 0);
            color = layer == 2
                ? mix(vec3(0.23, 0.49, 0.69), vec3(0.94, 0.46, 0.24), clamp((unpack16(data.rg) - 32468.0) / 650.0, 0.0, 1.0))
                : mix(vec3(0.75, 0.56, 0.35), vec3(0.14, 0.42, 0.63), clamp(unpack16(data.ba) / 3000.0, 0.0, 1.0));
        } else {
            color = vec3(0.14, 0.22, 0.24);
        }

        vec2 center = vec2(ROOT3 * (float(hex.x) + 0.5 * float(hex.y)), 1.5 * float(hex.y));
        vec2 local = abs(map_position - center);
        float edge_distance = ROOT3 * 0.5 - max(local.x, 0.5 * local.x + ROOT3 * 0.5 * local.y);
        float units_per_pixel = max(length(map_dx), length(map_dy));
        float edge = 1.0 - smoothstep(0.0, units_per_pixel * 1.3, edge_distance);
        if (show_grid) {
            float visibility = 1.0 - smoothstep(0.13, 0.35, units_per_pixel);
            color = mix(color, vec3(0.05, 0.12, 0.14), 0.6 * edge * visibility);
        }
        if (all(equal(offset, selected))) {
            color = mix(color, vec3(1.0, 0.84, 0.45), 0.28 + 0.72 * edge);
        }
    }
    COLOR = vec4(color, 1.0);
}
