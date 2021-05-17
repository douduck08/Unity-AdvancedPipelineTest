Shader "Hidden/ClusterDebugActive"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _DisplayScale ("", Vector) = (1, 1, 1, 1)
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

            #pragma multi_compile_instancing
            #pragma instancing_options procedural:setup
            
            #include "UnityCG.cginc"
            #include "ClusterLib.cginc"

            struct v2f
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };


            half4 _Color;
            float4 _DisplayScale;
            float4x4 _InvViewMatrix;

            StructuredBuffer<uint> _ActiveClusterIds;

            void setup() {}

            v2f vert (appdata_full v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                float3 center = 0;
                float3 size = 0;
#ifdef PROCEDURAL_INSTANCING_ON
                uint id = _ActiveClusterIds[unity_InstanceID];
                CaculateClusterAabb(id, center, size);
#endif

                float3 positionVS = center + v.vertex.xyz * size * _DisplayScale.xyz;
                float3 positionWS = mul(_InvViewMatrix, float4(positionVS, 1.0)).xyz;
                o.positionCS = UnityWorldToClipPos(positionWS);
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);

                half4 col = _Color;
                return col;
            }
            ENDCG
        }
    }
}
