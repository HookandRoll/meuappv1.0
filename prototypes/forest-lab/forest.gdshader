shader_type canvas_item;
render_mode unshaded;

uniform vec4 foliage_dark : source_color = vec4(0.075, 0.105, 0.055, 1.0);
uniform vec4 foliage_light : source_color = vec4(0.32, 0.36, 0.18, 1.0);
uniform float light_strength : hint_range(0.0, 1.0) = 0.65;
uniform float ink_strength : hint_range(0.0, 1.0) = 0.65;
uniform bool show_shadows = true;
varying vec4 tree_data;

float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}
float noise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1, 0)), f.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), f.x), f.y);
}
void vertex() { tree_data = INSTANCE_CUSTOM; }

void fragment() {
    vec2 tree_uv = vec2(UV.x, 1.0 - UV.y);
    vec2 p = vec2((tree_uv.x - 0.5) * 2.0, tree_uv.y);
    float seed = tree_data.x * 137.0;
    float lean = (tree_data.y - 0.5) * 0.13;
    p.x -= lean * (1.0 - p.y);
    float silhouette = 0.0;
    vec3 crown_color = foliage_dark.rgb;
    // Branch tiers overlap, each with irregular drooping tips and a dark underside.
    // The shader draws the tree; MultiMesh supplies placement and painter order.
    for (int branch = 9; branch >= 0; branch--) {
        float tier = float(branch) / 9.0;
        float top = 0.065 + tier * 0.58;
        float height = 0.17 + tier * 0.12;
        float y = (p.y - top) / height;
        float branch_variation = mix(0.86, 1.10, hash(vec2(seed, float(branch))));
        float width = (0.10 + tier * 0.63) * clamp(y, 0.0, 1.0) * branch_variation * mix(0.87, 1.10, tree_data.w);
        float teeth = (noise(vec2(p.x * 39.0 + seed, float(branch) * 3.1)) - 0.5) * (0.015 + tier * 0.035);
        float bottom = 1.0 - 0.13 * abs(p.x) / max(0.03, width) + teeth * 2.0;
        float edge = width - abs(p.x) + teeth;
        float aa = max(fwidth(p.x), 0.003);
        float shape = smoothstep(-aa, aa, edge) * smoothstep(-0.01, 0.01, y) * (1.0 - smoothstep(bottom - 0.02, bottom, y));
        float left_light = clamp(0.53 - p.x * 0.70 + (1.0 - y) * 0.14, 0.0, 1.0);
        float grain = noise(vec2(p.x * 51.0 + seed, p.y * 97.0)) * 0.15;
        float needles = noise(vec2(p.x * 93.0 + p.y * 34.0 + seed, p.y * 135.0)) * 0.09;
        float shade = clamp(left_light * light_strength + grain + needles - smoothstep(0.65, 1.0, y) * 0.17, 0.0, 1.0);
        vec3 color = mix(foliage_dark.rgb, foliage_light.rgb, shade);
        float rim = (1.0 - smoothstep(0.0, 0.035, edge)) * ink_strength;
        color *= 1.0 - rim * 0.40;
        crown_color = mix(crown_color, color, shape);
        silhouette = max(silhouette, shape);
    }
    float trunk = (1.0 - smoothstep(0.032, 0.047, abs(p.x))) * smoothstep(0.72, 0.75, p.y) * (1.0 - smoothstep(0.94, 0.96, p.y));
    vec3 color = mix(vec3(0.20, 0.155, 0.085), crown_color, silhouette);
    float alpha = max(silhouette, trunk);
    float shadow = 0.0;
    if (show_shadows) {
        vec2 s = (vec2((tree_uv.x - 0.5) * 2.0, tree_uv.y) - vec2(0.12, 0.91)) / vec2(0.83, 0.075);
        shadow = (1.0 - smoothstep(0.35, 1.0, dot(s, s))) * 0.28;
    }
    color = mix(vec3(0.035, 0.045, 0.025), color, alpha);
    color *= mix(0.88, 1.12, tree_data.z);
    COLOR = vec4(color, alpha + shadow * (1.0 - alpha));
}
