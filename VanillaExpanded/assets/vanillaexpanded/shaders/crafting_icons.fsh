#version 330 core

in vec2 uv;
out vec4 outColor;
uniform sampler2D iconTexture;
uniform vec4 tileBounds;
uniform vec4 textureBounds;
uniform int recipeState;
uniform int hasIcon;

void main()
{
    vec4 background = recipeState == 2 ? vec4(0.0, 0.0, 0.0, 0.92)
        : recipeState == 1 ? vec4(vec3(0.12 * 0.55), 0.55) : vec4(0.0);
    vec4 icon = vec4(0.0);
    float size = min(tileBounds.z, tileBounds.w);
    vec2 iconUv = (uv * tileBounds.zw - (tileBounds.zw - size) * 0.5) / size;
    if (hasIcon != 0 && all(greaterThanEqual(iconUv, vec2(0.0))) && all(lessThanEqual(iconUv, vec2(1.0))))
    {
        icon = texture(iconTexture, mix(textureBounds.xy, textureBounds.zw, iconUv));
        if (recipeState != 0)
        {
            float luminance = dot(icon.rgb, vec3(0.2126, 0.7152, 0.0722));
            icon.rgb = vec3(luminance * (recipeState == 2 ? 0.12 : 0.6));
        }
    }
    outColor = icon + background * (1.0 - icon.a);
    if (outColor.a < 0.001) discard;
}
