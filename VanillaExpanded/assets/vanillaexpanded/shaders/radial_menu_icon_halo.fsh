#version 330 core
uniform sampler2D iconMask;
uniform vec2 viewportOrigin;
uniform float haloRadius;
uniform vec4 haloTint;
out vec4 fragColor;

/* Treats pixels outside the capture as empty rather than extending its edge texels. */
float coverage(ivec2 pixel)
{
    ivec2 size = textureSize(iconMask, 0);
    if (any(lessThan(pixel, ivec2(0))) || any(greaterThanEqual(pixel, size))) return 0.0;
    return texelFetch(iconMask, pixel, 0).a;
}

/* Finds the nearest covered pixel in a circular neighborhood. */
void main()
{
    ivec2 pixel = ivec2(gl_FragCoord.xy - viewportOrigin);
    vec4 icon = texelFetch(iconMask, pixel, 0);
    if (icon.a > 0.1)
    {
        fragColor = icon;
        return;
    }
    float nearestSquared = (haloRadius + 1.0) * (haloRadius + 1.0);
    int radius = int(ceil(haloRadius + 0.5));
    // Euclidean distance keeps the same radius along horizontal, vertical and diagonal edges.
    for (int y = -radius; y <= radius; ++y)
    for (int x = -radius; x <= radius; ++x)
    {
        float distanceSquared = float(x * x + y * y);
        if (distanceSquared < nearestSquared && coverage(pixel + ivec2(x, y)) > 0.1)
            nearestSquared = distanceSquared;
    }
    float distancePixels = max(0.0, sqrt(nearestSquared) - 0.5);
    if (distancePixels > haloRadius) discard;
    fragColor = haloTint;
}
