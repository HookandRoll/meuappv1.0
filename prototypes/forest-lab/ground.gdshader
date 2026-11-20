shader_type canvas_item;
render_mode unshaded;

uniform bool show_grid = false;
float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise(vec2 p) {
    vec2 i = floor(p), f = fract(p); f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1, 0)), f.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), f.x), f.y);
}
void fragment() {
    vec2 p = UV * vec2(1800.0, 1200.0);
    float grass = noise(p * 0.008) * 0.55 + noise(p * 0.029) * 0.28 + noise(p * 0.22) * 0.17;
    vec3 color = mix(vec3(0.27, 0.29, 0.16), vec3(0.43, 0.43, 0.255), grass);
    color += (noise(p * vec2(0.65, 0.95)) - 0.5) * 0.028;
    if (show_grid) {
        vec2 q = vec2(p.x / 30.0 - p.y / 51.9615, p.y / 25.98076);
        vec3 cube = vec3(q.x, -q.x-q.y, q.y);
        vec3 rounded = round(cube), error = abs(rounded - cube);
        if (error.x > error.y && error.x > error.z) rounded.x = -rounded.y-rounded.z;
        else if (error.y > error.z) rounded.y = -rounded.x-rounded.z;
        else rounded.z = -rounded.x-rounded.y;
        vec2 center = vec2(30.0 * (rounded.x + rounded.z * 0.5), 25.98076 * rounded.z);
        vec2 d = abs(p - center);
        float edge = max(d.x / 15.0, (d.x * 0.5 + d.y * 0.8660254) / 15.0);
        color = mix(color, vec3(0.68, 0.63, 0.38), smoothstep(0.94, 1.0, edge) * 0.27);
    }
    COLOR = vec4(color, 1.0);
}
