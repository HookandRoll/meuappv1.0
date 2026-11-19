shader_type canvas_item;
render_mode unshaded;
uniform sampler2D terrain_data : filter_nearest, repeat_disable;
uniform ivec2 map_size;
uniform bool horizontal_wrap;
varying vec2 map_position;
const float ROOT3 = 1.73205080757;
void vertex() { map_position = VERTEX; }
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
ivec2 data_offset(ivec2 hex) {
    ivec2 offset = to_offset(hex);
    if (horizontal_wrap) {
        offset.x = (offset.x + map_size.x) % map_size.x;
    }
    return offset;
}
vec4 sample_terrain(ivec2 hex) {
    ivec2 offset = data_offset(hex);
    if (!inside(offset)) {
        return vec4(0.0, 0.0, 0.0, 1.0 / 255.0);
    }
    return texelFetch(terrain_data, offset, 0);
}
int surface_bits(vec4 data) {
    return int(round(data.a * 255.0));
}
int water_type(vec4 data) {
    return surface_bits(data) & 3;
}
float shore_crowding(vec4 data) {
    return float((int(round(data.b * 255.0)) >> 4) & 3) / 3.0;
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

void fragment() {
    float r = map_position.y * (2.0 / 3.0);
    vec2 axial = vec2(map_position.x / ROOT3 - r * 0.5, r);
    ivec2 cell = ivec2(floor(axial)); vec2 f = fract(axial);
    bool upper = f.x + f.y > 1.0;
    vec4 a = sample_terrain(cell + ivec2(upper ? 1 : 0));
    vec4 b = sample_terrain(cell + ivec2(1, 0));
    vec4 c = sample_terrain(cell + ivec2(0, 1));
    vec3 weights = upper ? vec3(f.x + f.y - 1.0, 1.0 - f.y, 1.0 - f.x) : vec3(1.0 - f.x - f.y, f.x, f.y);
    ivec3 types = ivec3(water_type(a), water_type(b), water_type(c));
    float land_fraction = dot(weights, vec3(equal(types, ivec3(0))));
    vec2 tile_dx = dFdx(map_position) / vec2(ROOT3, 2.0);
    vec2 tile_dy = dFdy(map_position) / vec2(ROOT3, 2.0);
    float footprint = max(length(tile_dx), length(tile_dy));
    vec2 noise = vec2(coast_noise(map_position, 0.31), coast_noise(map_position, 1.24) * (1.0 - smoothstep(0.18, 0.70, footprint)));
    float crowding = dot(weights, vec3(shore_crowding(a), shore_crowding(b), shore_crowding(c)));
    float tight = smoothstep(0.13, 0.68, crowding);
    float feather = clamp(0.025 + footprint * 0.40, 0.025, 0.11);
    vec2 warped = map_position + noise * 0.31;
    float contour = coast_noise(warped, 0.73);
    float fine = coast_noise(warped, 2.45) * (1.0 - smoothstep(0.09, 0.34, footprint));
    float edge = clamp(mix(0.35, 0.43, tight) + 0.105 * noise.x + 0.065 * contour + 0.045 * noise.y + 0.026 * fine, 0.19, 0.49);
    float bend = clamp(0.32 + 0.30 * noise.x - 0.23 * contour, 0.04, 0.76);
    float curve = mix(land_fraction - edge, curved_shore(weights, types, edge), bend);
    float land = land_fraction > 0.999 ? 1.0 : land_fraction < 0.001 ? 0.0 : smoothstep(-feather, feather, curve);
    float bank = smoothstep(0.0, 0.15, UV.x) * (1.0 - smoothstep(0.85, 1.0, UV.x));
    float ripple = sin(UV.y * 7.0 - TIME * 1.2) * 0.5 + 0.5;
    float size = smoothstep(0.04, 1.0, COLOR.r);
    vec3 water = mix(vec3(0.33, 0.56, 0.69), vec3(0.08, 0.25, 0.41), size);
    water *= 0.98 + ripple * 0.035;
    COLOR = vec4(water, bank * land);
}
