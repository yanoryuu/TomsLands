// スプライトの不透明部分にステンシル値を書き込むだけの不可視シェーダー（ColorMask 0）。
// 遮蔽シルエット用:
//   遮蔽物の双子 = _StencilRef 2 / プレイヤーのマーク = _StencilRef 1
// 透明キューは奥→手前順に描かれるため、最後に書かれた値で
// 「その画素を最後に覆ったのがプレイヤーか遮蔽物か」を判定できる。
Shader "TomsLands/SpriteStencilWrite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _StencilRef ("Stencil Ref", Float) = 2
        _AlphaCutoff ("Alpha Cutoff", Range(0,1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "TransparentCutout"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ColorMask 0

        Stencil
        {
            Ref [_StencilRef]
            Comp Always
            Pass Replace
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _AlphaCutoff;

            struct appdata_t
            {
                float4 vertex : POSITION;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 texcoord : TEXCOORD0;
            };

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = v.texcoord;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.texcoord);
                clip(c.a - _AlphaCutoff); // 透明部分にはステンシルを書かない
                return fixed4(0, 0, 0, 0);
            }
            ENDCG
        }
    }
}
