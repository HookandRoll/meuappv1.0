shader_type canvas_item;
render_mode unshaded;

uniform sampler2D terrain_data : filter_nearest, repeat_disable;
uniform sampler2D climate_data : filter_nearest, repeat_disable;
uniform ivec2 map_size;
uniform int layer;
uniform bool show_grid;
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
        return vec4(0.0);
    }
    return texelFetch(terrain_data, offset, 0);
}

float unpack16(vec2 bytes) {
    vec2 integer_bytes = round(bytes * 255.0);
    return dot(integer_bytes, vec2(1.0, 256.0)) / 65535.0;
}

vec3 biome_color(float id) {
    if (id == 1.0) return vec3(0.51, 0.57, 0.31);
    if (id == 2.0) return vec3(0.23, 0.42, 0.30);
    if (id == 3.0) return vec3(0.65, 0.61, 0.49);
    return SHALLOW;
}

vec3 terrain_color(vec2 axial) {
    ivec2 cell = ivec2(floor(axial));
    vec2 f = fract(axial);
    bool upper = f.x + f.y > 1.0;
    vec4 a = sample_terrain(cell + ivec2(upper ? 1 : 0));
    vec4 b = sample_terrain(cell + ivec2(1, 0));
    vec4 c = sample_terrain(cell + ivec2(0, 1));
    vec3 weights = upper
        ? vec3(f.x + f.y - 1.0, 1.0 - f.y, 1.0 - f.x)
        : vec3(1.0 - f.x - f.y, f.x, f.y);
    float elevation = dot(weights, vec3(unpack16(a.rg), unpack16(b.rg), unpack16(c.rg)));
    vec3 ids = round(vec3(a.b, b.b, c.b) * 255.0);
    vec3 land = biome_color(ids.x) * weights.x
        + biome_color(ids.y) * weights.y + biome_color(ids.z) * weights.z;
    vec3 water = mix(OCEAN, SHALLOW, smoothstep(0.14, 0.42, elevation));
    vec3 coast = mix(BEACH, land, smoothstep(0.420, 0.442, elevation));
    return mix(water, coast, smoothstep(0.416, 0.424, elevation));
}

void fragment() {
    float r = map_position.y * (2.0 / 3.0);
    vec2 axial = vec2(map_position.x / ROOT3 - r * 0.5, r);
    ivec2 hex = nearest_hex(axial);
    ivec2 offset = to_offset(hex);
    vec3 color = vec3(0.038, 0.068, 0.083);

    if (inside(offset)) {
        if (layer == 0) {
            color = terrain_color(axial);
        } else if (layer == 1 || layer == 4) {
            vec4 data = sample_terrain(hex);
            float elevation = unpack16(data.rg);
            color = layer == 1
                ? mix(vec3(0.09, 0.14, 0.22), vec3(0.96, 0.87, 0.62), elevation)
                : biome_color(round(data.b * 255.0));
        } else if (layer == 2 || layer == 3) {
            vec4 data = texelFetch(climate_data, offset, 0);
            color = layer == 2
                ? mix(vec3(0.23, 0.49, 0.69), vec3(0.94, 0.46, 0.24), unpack16(data.rg))
                : mix(vec3(0.75, 0.56, 0.35), vec3(0.14, 0.42, 0.63), unpack16(data.ba));
        } else {
            color = vec3(0.14, 0.22, 0.24);
        }

        vec2 center = vec2(ROOT3 * (float(hex.x) + 0.5 * float(hex.y)), 1.5 * float(hex.y));
        vec2 local = abs(map_position - center);
        float edge_distance = ROOT3 * 0.5 - max(local.x, 0.5 * local.x + ROOT3 * 0.5 * local.y);
        float units_per_pixel = max(length(dFdx(map_position)), length(dFdy(map_position)));
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
