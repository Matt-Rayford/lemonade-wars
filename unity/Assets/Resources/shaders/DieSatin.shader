// Lit shader for the 3D sale die: Lambert diffuse from two fixed directional
// lights, a soft Blinn-Phong highlight (satin, not mirror) and a faint rim, all
// computed per pixel from the mesh's smooth normals so the rounded edges catch the
// light. No scene lights, so it renders the same under the URP 2D renderer.
Shader "LemonadeWars/DieSatin"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _KeyDir ("Key light direction", Vector) = (-0.45, 0.8, -0.4, 0)
        _FillDir ("Fill light direction", Vector) = (0.7, 0.1, -0.3, 0)
    }
    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" }
        Cull Back
        ZWrite On

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _KeyDir;
            float4 _FillDir;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.worldNormal);
                float3 v = normalize(_WorldSpaceCameraPos - i.worldPos);
                float3 key = normalize(_KeyDir.xyz);
                float3 fill = normalize(_FillDir.xyz);
                float3 albedo = tex2D(_MainTex, i.uv).rgb;

                float diffuse = 0.38 + 0.52 * saturate(dot(n, key)) + 0.14 * saturate(dot(n, fill));
                float keySpec = pow(saturate(dot(n, normalize(key + v))), 32.0) * 0.45;
                float fillSpec = pow(saturate(dot(n, normalize(fill + v))), 16.0) * 0.12;
                float rim = pow(1.0 - saturate(dot(n, v)), 3.0) * 0.18;

                float3 color = albedo * diffuse + (keySpec + fillSpec) + rim * albedo;
                return fixed4(color, 1.0);
            }
            ENDCG
        }
    }
}
