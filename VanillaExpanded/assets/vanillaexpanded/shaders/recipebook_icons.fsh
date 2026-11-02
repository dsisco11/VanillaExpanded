#version 330 core

in vec2 uv;
out vec4 outColor;
uniform sampler2D iconTexture;
uniform vec4 tileBounds;
uniform vec4 textureBounds;
uniform int recipeState;
uniform int hasIcon;
uniform int selected;
uniform float guiScale;
uniform float previewInset;

void main()
{
    vec4 background = recipeState == 2 ? vec4(0.0, 0.0, 0.0, 0.92) : vec4(73.0 / 255.0, 61.0 / 255.0, 44.0 / 255.0, 1.0);
    vec4 icon = vec4(0.0);
    float size = max(1.0, min(tileBounds.z, tileBounds.w) - 2.0 * floor(previewInset * guiScale + 0.5));
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
    float borderWidth = max(1.0, floor(guiScale + 0.5));
    if (edgeDistance < borderWidth)
        outColor = recipeState == 3 ? vec4(0.95, 0.85, 0.12, 1.0)
            : recipeState == 1 ? vec4(0.85, 0.12, 0.12, 1.0) : vec4(113.0 / 255.0, 97.0 / 255.0, 70.0 / 255.0, 1.0);
    vec2 corner = uv * tileBounds.zw - floor(3.0 * guiScale + 0.5);
    float length = max(1.0, floor(8.0 * guiScale + 0.5));
    if (selected != 0 && all(greaterThanEqual(corner, vec2(0.0))) &&
        ((corner.x < length && corner.y < borderWidth) || (corner.y < length && corner.x < borderWidth)))
        outColor = vec4(245.0 / 255.0, 213.0 / 255.0, 141.0 / 255.0, 1.0);
    if (outColor.a < 0.001) discard;
}
