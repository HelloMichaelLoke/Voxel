Shader "Custom/S_Terrain"
{
    Properties
    {
        _TexColor("Texture Color", 2DArray) = "white" {}
        _TexHeight("Texture Height", 2DArray) = "white" {}
        _TexScale("Scale", Float) = 1.0
        _TexBrightness("Brightness", Range(0.0, 1.0)) = 0.1
        _SunLight("Sun Light", Range(0.0, 1.0)) = 1.0
    }
        SubShader
        {
            Pass
            {
                Tags {"LightMode" = "ForwardBase"}
                LOD 200

                CGPROGRAM
                #include "UnityCG.cginc"
                #include "UnityLightingCommon.cginc"
                #pragma vertex vert
                #pragma fragment frag
                #pragma require 2darray

                float _TexScale;
                float _TexBrightness;
                float _SunLight;

                struct appdata
                {
                    float4 vertex : POSITION;
                    float3 normal : NORMAL;
                    float4 texcoord0 : TEXCOORD0;
                    float4 texcoord1 : TEXCOORD1;
                    float4 texcoord2 : TEXCOORD2;
                    float4 texcoord3 : TEXCOORD3;
                    float2 texcoord4 : TEXCOORD4;
                };

                struct v2f
                {
                    float4 pos : SV_POSITION;
                    fixed4 diff : COLOR0;
                    float3 normal : NORMAL;
                    float2 weight12 : TEXCOORD0;
                    float2 weight34 : TEXCOORD1;
                    float2 weight56 : TEXCOORD2;
                    float2 weight78 : TEXCOORD3;
                    nointerpolation float4 mat1234 : TEXCOORD4;
                    nointerpolation float4 mat5678 : TEXCOORD5;
                    float3 worldPos : TEXCOORD6;
                    float3 worldNormal : TEXCOORD7;
                    float2 light : TEXCOORD8;
                };

                v2f vert(appdata i)
                {
                    v2f o;
                    o.pos = UnityObjectToClipPos(i.vertex);
                    o.worldPos = mul(unity_ObjectToWorld, i.vertex).xyz * _TexScale;
                    o.worldNormal = UnityObjectToWorldNormal(i.normal);
                    o.normal = i.normal;
                    o.weight12 = float2(i.texcoord0.x, i.texcoord0.y);
                    o.weight34 = float2(i.texcoord0.z, i.texcoord0.w);
                    o.weight56 = float2(i.texcoord1.x, i.texcoord1.y);
                    o.weight78 = float2(i.texcoord1.z, i.texcoord1.w);
                    o.mat1234 = i.texcoord2;
                    o.mat5678 = i.texcoord3;
                    o.light = i.texcoord4;
                    half nl = max(0, dot(o.worldNormal, _WorldSpaceLightPos0.xyz));
                    o.diff = nl * _LightColor0;
                    return o;
                }

                UNITY_DECLARE_TEX2DARRAY(_TexColor);
                UNITY_DECLARE_TEX2DARRAY(_TexHeight);

                fixed4 frag(v2f i) : SV_Target
                {
                    float3 weight = abs(i.worldNormal);
                    weight /= weight.x + weight.y + weight.z;

                    float mat = -1.0;
                    float height = 0.0;
                    float mat2 = -1.0;
                    float height2 = 0.0;

                    float currentMat = 0.0;
                    float currentHeight = 0.0;
                    float3 currentXY = float3(0.0, 0.0, 0.0);
                    float3 currentXZ = float3(0.0, 0.0, 0.0);
                    float3 currentYZ = float3(0.0, 0.0, 0.0);

                    if (i.weight12.x != 0.0)
                    {
                        currentMat = i.mat1234.x;
                        currentXY = UNITY_SAMPLE_TEX2DARRAY(_TexHeight, float3(i.worldPos.xy, currentMat)) * weight.z;
                        currentXZ = UNITY_SAMPLE_TEX2DARRAY(_TexHeight, float3(i.worldPos.xz, currentMat)) * weight.y;
                        currentYZ = UNITY_SAMPLE_TEX2DARRAY(_TexHeight, float3(i.worldPos.yz, currentMat)) * weight.x;
                        currentHeight = i.weight12.x * (currentXY + currentXZ + currentYZ).x;
                        if (currentHeight >= height)
                        {
                            height = currentHeight;
                            mat = currentMat;
                        }
                    }

                    if (i.weight12.y != 0.0)
                    {
                        currentMat = i.mat1234.y;
                        currentXY = UNITY_SAMPLE_TEX2DARRAY(_TexHeight, float3(i.worldPos.xy, currentMat)) * weight.z;
                        currentXZ = UNITY_SAMPLE_TEX2DARRAY(_TexHeight, float3(i.worldPos.xz, currentMat)) * weight.y;
                        currentYZ = UNITY_SAMPLE_TEX2DARRAY(_TexHeight, float3(i.worldPos.yz, currentMat)) * weight.x;
                        currentHeight = i.weight12.y * (currentXY + currentXZ + currentYZ).x;
                        if (currentHeight >= height)
                        {
                            height2 = height;
                            mat2 = mat;
                            height = currentHeight;
                            mat = currentMat;
                        }
                        else if (currentHeight >= height2)
                        {
                            height2 = currentHeight;
                            mat2 = currentMat;
                        }
                    }

                    if (i.weight34.x != 0.0)
                    {
                        currentMat = i.mat1234.z;
                        currentXY = UNITY_SAMPLE_TEX2DARRAY(_TexHeight, float3(i.worldPos.xy, currentMat)) * weight.z;
                        currentXZ = UNITY_SAMPLE_TEX2DARRAY(_TexHeight, float3(i.worldPos.xz, currentMat)) * weight.y;
                        currentYZ = UNITY_SAMPLE_TEX2DARRAY(_TexHeight, float3(i.worldPos.yz, currentMat)) * weight.x;
                        currentHeight = i.weight34.x * (currentXY + currentXZ + currentYZ).x;
                        if (currentHeight >= height)
                        {
                            height2 = height;
                            mat2 = mat;
                            height = currentHeight;
                            mat = currentMat;
                        }
                        else if (currentHeight >= height2)
                        {
                            height2 = currentHeight;
                            mat2 = currentMat;
                        }
                    }

                    if (i.weight34.y != 0.0)
                    {
                        currentMat = i.mat1234.w;
                        currentXY = UNITY_SAMPLE_TEX2DARRAY(_TexHeight, float3(i.worldPos.xy, currentMat)) * weight.z;
                        currentXZ = UNITY_SAMPLE_TEX2DARRAY(_TexHeight, float3(i.worldPos.xz, currentMat)) * weight.y;
                        currentYZ = UNITY_SAMPLE_TEX2DARRAY(_TexHeight, float3(i.worldPos.yz, currentMat)) * weight.x;
                        currentHeight = i.weight34.y * (currentXY + currentXZ + currentYZ).x;
                        if (currentHeight >= height)
                        {
                            height2 = height;
                            mat2 = mat;
                            height = currentHeight;
                            mat = currentMat;
                        }
                        else if (currentHeight >= height2)
                        {
                            height2 = currentHeight;
                            mat2 = currentMat;
                        }
                    }

                    if (i.weight56.x != 0.0)
                    {
                        currentMat = i.mat5678.x;
                        currentXY = UNITY_SAMPLE_TEX2DARRAY(_TexHeight, float3(i.worldPos.xy, currentMat)) * weight.z;
                        currentXZ = UNITY_SAMPLE_TEX2DARRAY(_TexHeight, float3(i.worldPos.xz, currentMat)) * weight.y;
                        currentYZ = UNITY_SAMPLE_TEX2DARRAY(_TexHeight, float3(i.worldPos.yz, currentMat)) * weight.x;
                        currentHeight = i.weight56.x * (currentXY + currentXZ + currentYZ).x;
                        if (currentHeight >= height)
                        {
                            height2 = height;
                            mat2 = mat;
                            height = currentHeight;
                            mat = currentMat;
                        }
                        else if (currentHeight >= height2)
                        {
                            height2 = currentHeight;
                            mat2 = currentMat;
                        }
                    }

                    if (i.weight56.y != 0.0)
                    {
                        currentMat = i.mat5678.y;
                        currentXY = UNITY_SAMPLE_TEX2DARRAY(_TexHeight, float3(i.worldPos.xy, currentMat)) * weight.z;
                        currentXZ = UNITY_SAMPLE_TEX2DARRAY(_TexHeight, float3(i.worldPos.xz, currentMat)) * weight.y;
                        currentYZ = UNITY_SAMPLE_TEX2DARRAY(_TexHeight, float3(i.worldPos.yz, currentMat)) * weight.x;
                        currentHeight = i.weight56.y * (currentXY + currentXZ + currentYZ).x;
                        if (currentHeight >= height)
                        {
                            height2 = height;
                            mat2 = mat;
                            height = currentHeight;
                            mat = currentMat;
                        }
                        else if (currentHeight >= height2)
                        {
                            height2 = currentHeight;
                            mat2 = currentMat;
                        }
                    }

                    if (i.weight78.x != 0.0)
                    {
                        currentMat = i.mat5678.z;
                        currentXY = UNITY_SAMPLE_TEX2DARRAY(_TexHeight, float3(i.worldPos.xy, currentMat)) * weight.z;
                        currentXZ = UNITY_SAMPLE_TEX2DARRAY(_TexHeight, float3(i.worldPos.xz, currentMat)) * weight.y;
                        currentYZ = UNITY_SAMPLE_TEX2DARRAY(_TexHeight, float3(i.worldPos.yz, currentMat)) * weight.x;
                        currentHeight = i.weight78.x * (currentXY + currentXZ + currentYZ).x;
                        if (currentHeight >= height)
                        {
                            height2 = height;
                            mat2 = mat;
                            height = currentHeight;
                            mat = currentMat;
                        }
                        else if (currentHeight >= height2)
                        {
                            height2 = currentHeight;
                            mat2 = currentMat;
                        }
                    }

                    if (i.weight78.y != 0.0)
                    {
                        currentMat = i.mat5678.w;
                        currentXY = UNITY_SAMPLE_TEX2DARRAY(_TexHeight, float3(i.worldPos.xy, currentMat)) * weight.z;
                        currentXZ = UNITY_SAMPLE_TEX2DARRAY(_TexHeight, float3(i.worldPos.xz, currentMat)) * weight.y;
                        currentYZ = UNITY_SAMPLE_TEX2DARRAY(_TexHeight, float3(i.worldPos.yz, currentMat)) * weight.x;
                        currentHeight = i.weight78.y * (currentXY + currentXZ + currentYZ).x;
                        if (currentHeight >= height)
                        {
                            height2 = height;
                            mat2 = mat;
                            height = currentHeight;
                            mat = currentMat;
                        }
                        else if (currentHeight >= height2)
                        {
                            height2 = currentHeight;
                            mat2 = currentMat;
                        }
                    }

                    float3 albedoZ = UNITY_SAMPLE_TEX2DARRAY(_TexColor, float3(i.worldPos.xy, mat)) * weight.z;
                    float3 albedoY = UNITY_SAMPLE_TEX2DARRAY(_TexColor, float3(i.worldPos.xz, mat)) * weight.y;
                    float3 albedoX = UNITY_SAMPLE_TEX2DARRAY(_TexColor, float3(i.worldPos.yz, mat)) * weight.x;
                    float3 albedo = albedoX + albedoY + albedoZ;

                    float diffuse = 0.2 + 0.8 * i.diff;
                    float light = max(i.light.x * _SunLight * diffuse, i.light.y);
                    float camDist = distance(_WorldSpaceCameraPos, (i.worldPos / _TexScale));

                    float lightThreshold = 0.1;
                    if (camDist < 10.0 && light < lightThreshold)
                    {
                        float lerpValue = (camDist / 10.0);
                        light = lerpValue * light + (1.0 - lerpValue) * lightThreshold;
                    }

                    // Gamma Correction
                    //light = pow(light, 1.0 / 2.2);

                    // Diffuse Light
                    //float3 color = i.diff;
                    
                    // Voxel Light
                    float3 color = max(i.light.x, i.light.y) * float3(1.0, 1.0, 1.0);

                    // Albedo with Light
                    //float3 color = (_TexBrightness + (1.0 - _TexBrightness) * light) * albedo;

                    //float3 color = i.worldNormal;
                    //color = float3(1.0, 1.0, 1.0) * light;

                    return float4(color.x, color.y, color.z, 1.0f);
                }
                ENDCG
            }
        }
    FallBack "Diffuse"
}
