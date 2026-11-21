// Shared coastline displacement for terrain, visible rivers and clearance.
float boundary_roughness(vec2 position, float footprint) {
    vec2 warp = position + vec2(coast_noise(position, 0.17), coast_noise(position + vec2(13.0, 7.0), 0.21)) * 0.65;
    float middle = 1.0 - smoothstep(0.08, 0.30, footprint);
    float fine = 1.0 - smoothstep(0.025, 0.12, footprint);
    return 0.50 * coast_noise(warp, 0.90) + 0.32 * coast_noise(warp, 2.8) * middle
        + 0.18 * coast_noise(warp, 7.3) * fine;
}
