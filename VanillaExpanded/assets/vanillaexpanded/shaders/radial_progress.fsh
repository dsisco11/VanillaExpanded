#version 330 core

// Required injected defines
#ifndef START_OFFSET
    #define START_OFFSET 0.0
#endif

#ifndef CLOCKWISE
    #define CLOCKWISE 0
#endif

in vec2 vUV;              // 0..1 UV
out vec4 fragColor;

uniform sampler2D packedTex;

// Requested uniforms:
uniform float progressScalar;   // 0..1
uniform float outerRadius;      // 0..1 (1 = edge of the inscribed circle)
uniform float innerRadius;      // 0..1 (0 = center). Full disc: 0.
uniform vec4 tintColor;         // RGBA tint applied to output

float saturate(float x) { return clamp(x, 0.0, 1.0); }

void main()
{// Note: Doing all of this math in the pixel shader each frame is still more performant than a solution which uses a radial vertex mesh (Also this shader scales perfectly to any resolution).
    vec4 t = texture(packedTex, vUV);

    // ---- Decode high-precision angle from RG (stored as hi/lo bytes) ----
    float hi = t.r * 255.0;
    float lo = t.g * 255.0;
    float angle01 = (hi * 256.0 + lo) / 65535.0; // [0,1]

    // Apply start offset + direction (compile-time)
    angle01 = fract(angle01 - float(START_OFFSET));
#if CLOCKWISE
    angle01 = 1.0 - angle01;
#endif

    // ---- Radius from A channel ----
    // In the generated packed texture: A == r clamped to [0,1]
    float r = t.a;

    // ---- Compute geometric radius for proper corner masking ----
    // The texture clamps r to [0,1], but corners of the quad are at ~1.414
    // Use actual geometric radius for outer mask to properly discard corners
    vec2 centered = vUV * 2.0 - 1.0;
    float geometricRadius = length(centered);

    // ---- Anti-alias widths (derivative-based) ----
    float rAA = fwidth(r) + 1e-6;
    float gAA = fwidth(geometricRadius) + 1e-6;
    float aAA = fwidth(angle01) + 1e-6;

    // ---- Ring mask using innerRadius/outerRadius ----
    // Use geometric radius for outer (handles corners), texture radius for inner (more precise)
    float outerMask = 1.0 - smoothstep(outerRadius - gAA, outerRadius + gAA, geometricRadius);
    float innerMask = smoothstep(innerRadius - rAA, innerRadius + rAA, r);
    float ringMask  = outerMask * innerMask;

    // ---- Progress fill (angle01 <= progressScalar) ----
    float fillMask = smoothstep(progressScalar + aAA, progressScalar - aAA, angle01);

    float alpha = ringMask * fillMask;

    fragColor = vec4(tintColor.rgb, tintColor.a * alpha);
}
