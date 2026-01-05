#version 330 core

// Inject these at compile time (defaults provided)
#ifndef START_OFFSET
    #define START_OFFSET 0.0   // 0..1, 0 at +X
#endif

#ifndef CLOCKWISE
    #define CLOCKWISE 0        // 0 = CCW, 1 = CW
#endif

in vec2 vPos;     // [-1,1] quad coordinates
out vec4 fragColor;

uniform float progressScalar;   // 0..1
uniform float outerRadius;      // 0..1 (1 = inscribed circle edge)
uniform float innerRadius;      // 0..1
uniform vec4  tintColor;        // RGBA

const float TAU = 6.28318530717958647692;

/*
    Radial progress bars are typically small UI elements (often <10% of the screen).
    On desktop GPUs, the per-fragment cost of a couple of math ops + one atan() here is
    negligible compared to overall UI composition (blending/overdraw/draw calls), and
    avoiding lookup textures prevents seam/filtering artifacts and simplifies setup.
*/
void main()
{
    // vPos is already centered at origin, range [-1,1]
    // r=1 at inscribed circle edge
    float r = length(vPos);

    // Angle in [0,1): 0 at +X, increases CCW
    float angle01 = atan(vPos.y, vPos.x) / TAU + 0.5;
    angle01 = fract(angle01 - float(START_OFFSET));
#if CLOCKWISE
    angle01 = 1.0 - angle01;
#endif

    // Derivative-based anti-alias widths
    float rAA = fwidth(r) + 1e-6;
    float aAA = fwidth(angle01) + 1e-6;

    // Ring mask
    float outerMask = 1.0 - smoothstep(outerRadius - rAA, outerRadius + rAA, r);
    float innerMask =       smoothstep(innerRadius - rAA, innerRadius + rAA, r);
    float ringMask  = outerMask * innerMask;

    // Progress fill: 1 when angle01 <= progressScalar (AA at boundary)
    float fillMask = smoothstep(progressScalar + aAA, progressScalar - aAA, angle01);

    float alpha = ringMask * fillMask;
    fragColor = vec4(tintColor.rgb, tintColor.a * alpha);
}
