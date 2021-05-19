Shader "ClusterLit"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            #include "ClusterLib.cginc"

            struct v2f
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 screenPos  : TEXCOORD2;
            };


            struct PointLight {
                float4 position; // xyz: world position, w: radius
                float4 color;
            };

            StructuredBuffer<PointLight> _GlobalLightList;
            StructuredBuffer<uint2> _ClusterLightOffsetList;
            StructuredBuffer<uint> _LightIndexList;

            half4 _Color;

            v2f vert (appdata_full v)
            {
                v2f o;
                o.positionCS = UnityObjectToClipPos(v.vertex);
                o.positionWS = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.normalWS = UnityObjectToWorldNormal(v.normal);
                o.screenPos = ComputeScreenPos(o.positionCS);
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                float2 screenPos = (i.screenPos.xy / i.screenPos.w) * _ClusterScreenParams.xy;
                uint3 clusterIndex = ComputeClusterIndex3D(screenPos, i.screenPos.w);
                uint clusterId = CaculateClusterId(clusterIndex);
                uint2 offset = _ClusterLightOffsetList[clusterId];

                // return clusterIndex.z == 126;

                half3 albedo = _Color.rgb;
                float3 normalWS = normalize(i.normalWS);

                half3 color = 0;
                for (uint k = 0; k < offset.y; k++)
                {
                    uint lightIndex = _LightIndexList[offset.x + k];
                    PointLight light = _GlobalLightList[lightIndex];

                    float3 lightDir = light.position.xyz - i.positionWS;
                    float dis = length(lightDir);
                    lightDir = normalize(lightDir);
                    half disFac = saturate((light.position.w - dis) / light.position.w);
                    disFac *= disFac;

                    half3 lightColor = light.color.rgb * disFac;
                    half3 diffuse = albedo * lightColor * saturate(dot(lightDir, normalWS));

                    float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - i.positionWS);
                    float3 h = normalize(lightDir + viewDir);
                    float hDotN = saturate(dot(h, normalWS));
                    half3 specular = lightColor * pow(hDotN, 128);

                    color += diffuse + specular;
                }

                return half4(color, 1.0);
            }
            ENDCG
        }
    }
    Fallback "VertexLit"
}
