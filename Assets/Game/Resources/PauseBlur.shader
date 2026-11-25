Shader "Hidden/Broken Heart/Pause Blur"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _BlurAxis ("Blur axis", Vector) = (0, 0, 0, 0)
        _FlipY ("Flip source vertically", Float) = 0
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float4 _BlurAxis;
            float _FlipY;

            half4 frag(v2f_img input) : SV_Target
            {
                float2 stepUV = abs(_MainTex_TexelSize.xy) * _BlurAxis.xy;
                float2 uv = float2(input.uv.x, lerp(input.uv.y, 1 - input.uv.y, _FlipY));
                return tex2D(_MainTex, uv) * 0.375
                     + (tex2D(_MainTex, uv + stepUV) + tex2D(_MainTex, uv - stepUV)) * 0.25
                     + (tex2D(_MainTex, uv + 2 * stepUV) + tex2D(_MainTex, uv - 2 * stepUV)) * 0.0625;
            }
            ENDCG
        }
    }
}
