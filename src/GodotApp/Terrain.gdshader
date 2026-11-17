shader_type canvas_item;
render_mode unshaded;

uniform sampler2D terrain_data : filter_nearest, repeat_disable;
uniform sampler2D climate_data : filter_nearest, repeat_disable;
uniform sampler2D detail_data : filter_nearest, repeat_disable;
uniform sampler2DArray ground_art : source_color, filter_linear_mipmap, repeat_disable;
uniform sampler2DArray relief_art : source_color, filter_linear_mipmap, repeat_disable;
uniform sampler2DArray vegetation_art : source_color, filter_linear_mipmap, repeat_disable;
uniform sampler2DArray detail_art : source_color, filter_linear_mipmap, repeat_disable;
uniform ivec2 map_size;
uniform int layer;
uniform int stage;
uniform bool horizontal_wrap;
uniform bool show_grid;
uniform bool show_relief = true;
uniform bool show_vegetation = true;
uniform bool show_details = true;
uniform float detail_zoom : hint_range(0.0, 1.0);
uniform ivec2 selected = ivec2(-1);
varying vec2 map_position;

const float ROOT3 = 1.73205080757;
const vec3 OCEAN = vec3(0.065, 0.19, 0.24);
const vec3 SHALLOW = vec3(0.19, 0.43, 0.43);
const vec3 BEACH = vec3(0.73, 0.67, 0.46);
const float SHALLOW_OCEAN_ART = 10.0;
const float DEEP_OCEAN_ART = 13.0;

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
    if (horizontal_wrap) {
        offset.x = (offset.x + map_size.x) % map_size.x;
    }
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

int biome_id(vec4 data) {
    return int(round(data.b * 255.0)) & 15;
}

float shore_crowding(vec4 data) {
    return float((int(round(data.b * 255.0)) >> 4) & 3) / 3.0;
}

vec2 hex_center(ivec2 hex) {
    return vec2(ROOT3 * (float(hex.x) + 0.5 * float(hex.y)), 1.5 * float(hex.y));
}

vec2 hex_uv(ivec2 hex) {
    return (map_position - hex_center(hex)) / vec2(ROOT3, 2.0) + vec2(0.5);
}

vec3 ground_color(vec4 data, ivec2 hex, vec2 tile_dx, vec2 tile_dy) {
    int water = water_type(data);
    int biome = biome_id(data);
    int art = water != 0 ? 9 + water : (biome == 0 ? 1 : clamp(biome - 1, 0, 9));
    vec4 texel = textureGrad(ground_art, vec3(clamp(hex_uv(hex), vec2(0.0), vec2(1.0)), float(art)), tile_dx, tile_dy);
    return mix(water != 0 ? OCEAN : biome_color(float(biome)), texel.rgb, texel.a);
}

vec3 ocean_color(float depth, float land_fraction, vec2 tile_dx, vec2 tile_dy) {
    float shallow = max(smoothstep(-900.0, -130.0, depth), 0.85 * smoothstep(0.0, 0.55, land_fraction));
    float footprint = max(length(tile_dx), length(tile_dy));
    shallow *= mix(1.0, 0.68, smoothstep(0.10, 0.38, footprint));
    // A wave tile spans several hexes and is anchored to the map across shore and ocean.
    const float repeat_size = 6.0;
    vec2 uv = fract(map_position / vec2(ROOT3, 2.0) / repeat_size);
    vec2 dx = tile_dx / repeat_size;
    vec2 dy = tile_dy / repeat_size;
    vec4 deep_color = textureGrad(ground_art, vec3(uv, DEEP_OCEAN_ART), dx, dy);
    vec4 shallow_color = textureGrad(ground_art, vec3(uv, SHALLOW_OCEAN_ART), dx, dy);
    return mix(mix(OCEAN, deep_color.rgb, deep_color.a), mix(SHALLOW, shallow_color.rgb, shallow_color.a), shallow);
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
    int detail = 0;
    if (show_details && minification < 1.0) {
        detail = int(round(texelFetch(detail_data, to_offset(hex), 0).r * 255.0));
    }
    int icon_art = (detail & 31) - 1;
    float close_reveal = smoothstep(0.43, 0.90, detail_zoom);
    float reveal = (detail & 32) != 0 ? close_reveal : 1.0;
    float shared_layout = (detail & 32) != 0 ? smoothstep(0.30, 0.57, detail_zoom) : 0.0;
    if (show_relief && (surface & 64) != 0) {
        int art = (surface & 32) != 0 ? 4 : relief - 1;
        vec2 anchor = mix(vec2(0.5), vec2(0.5, 0.274), detail_zoom);
        vec2 size = mix(vec2(0.97, 0.84), vec2(0.54, 0.455), detail_zoom);
        anchor = mix(anchor, vec2(0.5, 0.225), shared_layout);
        size = mix(size, vec2(0.26, 0.24), shared_layout);
        vec2 uv = (tile - anchor) / size + vec2(0.5);
        vec4 texel = sample_overlay(relief_art, art, uv, tile_dx / size, tile_dy / size, minification, 8.0);
        color = mix(color, texel.rgb, texel.a);
    }
    if (show_vegetation && (surface & 128) != 0) {
        int art = vegetation_index(biome_id(data), water);
        if (art >= 0) {
            vec2 anchor = mix(vec2(0.5), vec2(0.29, 0.35), detail_zoom);
            vec2 size = mix(vec2(0.97, 0.84), vec2(0.32, 0.278), detail_zoom);
            anchor = mix(anchor, vec2(0.245, 0.40), shared_layout);
            size = mix(size, vec2(0.24, 0.21), shared_layout);
            vec2 uv = (tile - anchor) / size + vec2(0.5);
            vec4 texel = sample_overlay(vegetation_art, art, uv, tile_dx / size, tile_dy / size, minification, 7.0);
            color = mix(color, texel.rgb, texel.a);
        }
    }
    if (icon_art >= 0 && reveal > 0.0) {
        vec2 anchor = mix(vec2(0.64, 0.42), vec2(0.76, 0.40), detail_zoom);
        vec2 size = mix(vec2(0.51, 0.40), vec2(0.32, 0.28), detail_zoom);
        if ((surface & 0xc0) != 0) {
            size = mix(size, vec2(0.25, 0.22), shared_layout);
        }
        vec2 uv = (tile - anchor) / size + vec2(0.5);
        if (inside_art(uv)) {
            vec4 icon = textureGrad(detail_art, vec3(uv, float(icon_art)), tile_dx / size, tile_dy / size);
            color = mix(color, icon.rgb, icon.a * reveal * (1.0 - minification));
        }
    }
    return color;
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
    float span = float(map_size.x) * ROOT3;
    float period = round(span * frequency);
    vec2 p = position * vec2(horizontal_wrap ? period / span : frequency, frequency);
    vec2 cell = floor(p);
    vec2 f = smoothstep(vec2(0.0), vec2(1.0), fract(p));
    return mix(
        mix(noise_sample(cell, period), noise_sample(cell + vec2(1.0, 0.0), period), f.x),
        mix(noise_sample(cell + vec2(0.0, 1.0), period), noise_sample(cell + vec2(1.0), period), f.x),
        f.y) * 2.0 - 1.0;
}

float quadratic_boundary(vec3 ordered, float endpoint) {
    // Endpoints lie on the mixed edges. The control point sits on both edge normals
    // so adjacent quadratics share the tangent at their crossings.
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
    // Along a shared edge the correction vanishes, including at the mouth of a three-biome junction.
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

vec3 terrain_color(vec2 axial, vec4 current, ivec2 current_hex, vec2 tile_dx, vec2 tile_dy) {
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
    ivec3 types = ivec3(water_type(a), water_type(b), water_type(c));
    int current_type = water_type(current);
    vec3 dry_weights = weights * vec3(equal(types, ivec3(0)));
    vec3 sea_weights = weights * vec3(equal(types, ivec3(1)));
    float land_fraction = dot(dry_weights, vec3(1.0));
    float sea_fraction = dot(sea_weights, vec3(1.0));
    bool shore = current_type == 1 && land_fraction > 0.0 && sea_fraction > 0.0;

    vec3 water = vec3(0.0);
    if (current_type != 0) {
        if (current_type == 1) {
            float depth = dot(sea_weights, heights) / max(sea_fraction, 0.00001);
            if (stage == 1) {
                depth = mix(-900.0, -130.0, smoothstep(0.0, 0.38, land_fraction));
            }
            water = ocean_color(depth, land_fraction, tile_dx, tile_dy);
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
    if (shore || blend_biomes) {
        noise = vec2(coast_noise(map_position, 0.49), coast_noise(map_position, 1.81));
    }
    float elevation = dot(dry_weights, heights) / max(land_fraction, 0.00001);
    vec3 art_a = dry_weights.x > 0.0 ? ground_color(a, hex_a, tile_dx, tile_dy) : vec3(0.0);
    vec3 art_b = dry_weights.y > 0.0 ? ground_color(b, hex_b, tile_dx, tile_dy) : vec3(0.0);
    vec3 art_c = dry_weights.z > 0.0 ? ground_color(c, hex_c, tile_dx, tile_dy) : vec3(0.0);
    if (blend_biomes) {
        // Work with total weight per biome so a triangle's zero-weight vertex cannot change its edge.
        vec3 same = vec3(float(ids.x == ids.y), float(ids.x == ids.z), float(ids.y == ids.z));
        vec3 biome_weights = dry_weights + vec3(
            dot(dry_weights.yz, same.xy),
            dot(dry_weights.xz, same.xz),
            dot(dry_weights.xy, same.yz));
        vec3 share = biome_weights / land_fraction;
        vec3 curved = curved_biome_shares(ids, dry_weights / land_fraction);
        vec3 phase = ids * 2.39996;
        vec3 bias = noise.x * sin(phase) + noise.y * cos(phase) * 0.48;
        vec3 feather = smoothstep(vec3(0.08), vec3(0.92), curved);
        dry_weights *= feather * (1.0 + bias * 0.26) / max(share, vec3(0.00001));
    }
    vec3 land = (art_a * dry_weights.x + art_b * dry_weights.y + art_c * dry_weights.z)
        / max(dot(dry_weights, vec3(1.0)), 0.00001);
    land *= mix(0.96, 1.07, smoothstep(0.0, 4200.0, elevation));
    if (!shore) {
        return land;
    }

    // Extend the same land material into the coastal strip, then fade it into the water.
    float crowding = dot(weights, vec3(shore_crowding(a), shore_crowding(b), shore_crowding(c)));
    float tight = smoothstep(0.13, 0.68, crowding);
    float footprint = max(length(tile_dx), length(tile_dy));
    float feather = clamp(0.035 + footprint * 0.40, 0.035, 0.11);
    float edge = mix(0.35, 0.43, tight);
    float curve = curved_shore(weights, types, edge) + 0.021 * noise.x + 0.012 * noise.y;
    float strip = smoothstep(-feather, feather, curve);
    return mix(water, land, strip);
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
            vec4 data = sample_terrain(hex);
            color = decorate(terrain_color(axial, data, hex, tile_dx, tile_dy), data, hex, tile_dx, tile_dy);
        } else if (layer == 1 || layer == 4) {
            vec4 data = sample_terrain(hex);
            float elevation = unpack16(data.rg) - 32768.0;
            color = layer == 1
                ? mix(vec3(0.09, 0.14, 0.22), vec3(0.96, 0.87, 0.62), clamp((elevation + 2400.0) / 6800.0, 0.0, 1.0))
                : (water_type(data) != 0 ? OCEAN : biome_color(float(biome_id(data))));
        } else if (layer == 2 || layer == 3) {
            vec4 data = texelFetch(climate_data, offset, 0);
            color = layer == 2
                ? mix(vec3(0.23, 0.49, 0.69), vec3(0.94, 0.46, 0.24), clamp((unpack16(data.rg) - 32468.0) / 650.0, 0.0, 1.0))
                : mix(vec3(0.75, 0.56, 0.35), vec3(0.14, 0.42, 0.63), clamp(unpack16(data.ba) / 3000.0, 0.0, 1.0));
        } else {
            color = vec3(0.14, 0.22, 0.24);
        }

        vec2 local = abs(map_position - hex_center(hex));
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
