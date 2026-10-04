#version 330 core
uniform vec4 color;
in vec2 grainLocal;
in vec2 slotLocal;
flat in vec3 grainColor;
layout(location=0) out vec4 fragColor;

/* Shapes inexpensive small quads into softly edged grains with stable tonal variation. */
void main()
{
    if (any(lessThan(slotLocal,vec2(0.0))) || any(greaterThan(slotLocal,vec2(1.0)))) discard;
    float distance = length(grainLocal);
    float edge = max(fwidth(distance),0.02);
    float alpha = color.a*(1.0-smoothstep(1.0-edge,1.0,distance));
    fragColor = vec4(grainColor*alpha,alpha);
}
