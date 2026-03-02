shader_type canvas_item;

// Warm yellow-white glow color
uniform vec4 glow_color : source_color = vec4(1.0, 0.92, 0.15, 1.0);

// Base intensity of the bright core
uniform float intensity : hint_range(0.5, 12.0) = 5.5;

// How quickly the glow falls off from centre (higher = tighter)
uniform float falloff : hint_range(1.0, 20.0) = 5.5;

// Pulse: amplitude fraction and speed (Hz)
uniform float pulse_amount : hint_range(0.0, 0.5) = 0.25;
uniform float pulse_speed  : hint_range(0.1, 5.0) = 0.8;

void fragment() {
    // Remap UV from [0,1] to [-1,1] so the origin is the centre
    vec2 uv = UV * 2.0 - 1.0;
    float dist = length(uv);

    // Pulsating intensity — breathes between (1 - amount) and (1 + amount)
    float pulse = 1.0 + pulse_amount * sin(TIME * pulse_speed * 6.2832);
    float pulsed_intensity = intensity * pulse;

    // Tight bright core — classic exponential point-light glow
    float core = exp(-dist * falloff) * pulsed_intensity;

    // Wider, softer halo for the "light spilling out" look
    float halo = exp(-dist * (falloff * 0.35)) * (pulsed_intensity * 0.18);

    float alpha = clamp(core + halo, 0.0, 1.0);

    COLOR = vec4(glow_color.rgb, alpha);
}
