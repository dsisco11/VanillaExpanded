#version 330 core

in vec2 uv;
out vec4 outColor;
uniform sampler2D iconTexture;
uniform vec4 tileBounds;
uniform vec4 textureBounds;
uniform int recipeState;
uniform int hasIcon;
uniform float guiScale;

void main()
{
    vec4 background = recipeState == 2 ? vec4(0.0, 0.0, 0.0, 0.92) : vec4(0.0);
    vec4 icon = vec4(0.0);
    float size = min(tileBounds.z, tileBounds.w);
    vec2 iconUv = (uv * tileBounds.zw - (tileBounds.zw - size) * 0.5) / size;
    if (hasIcon != 0 && all(greaterThanEqual(iconUv, vec2(0.0))) && all(lessThanEqual(iconUv, vec2(1.0))))
    {
        icon = texture(iconTexture, mix(textureBounds.xy, textureBounds.zw, iconUv));
        if (recipeState == 2)
        {
            float luminance = dot(icon.rgb, vec3(0.2126, 0.7152, 0.0722));
            icon.rgb = vec3(luminance * 0.12);
        }
    }
    outColor = icon + background * (1.0 - icon.a);
    vec2 edgeDistances = min(uv, vec2(1.0) - uv) * tileBounds.zw;
    float edgeDistance = min(edgeDistances.x, edgeDistances.y);
    if ((recipeState == 1 || recipeState == 3) && edgeDistance >= 2.0 * guiScale && edgeDistance < 4.0 * guiScale)
        outColor = recipeState == 3 ? vec4(0.95, 0.85, 0.12, 1.0) : vec4(0.85, 0.12, 0.12, 1.0);
    if (outColor.a < 0.001) discard;
}
