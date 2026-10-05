// バズモードオーバーレイ用UIシェーダー。
// パチンコ演出の定番表現をUI Imageで再現する:
// - シャインスイープ: 光の帯が斜めに走る（金枠の「キラッ」）
// - 色相サイクル: テクスチャの色相が時間で回転する（虹枠のホログラフィック表現）
// UI/Default をベースにしており、Mask / RectMask2D / CanvasGroup に対応する。
Shader "UI/BuzzFrame"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        [Header(Rainbow Hue Cycle)]
        _HueShiftSpeed ("Hue Shift Speed (cycles per sec)", Float) = 0
        _HueShiftStrength ("Hue Shift Strength", Range(0,1)) = 1

        [Header(Shine Sweep)]
        _ShineStrength ("Shine Strength", Range(0,2)) = 0
        _ShineSpeed ("Shine Speed (sweeps per sec)", Float) = 0.4
        _ShineWidth ("Shine Width", Range(0.01,1)) = 0.15
        _ShineAngle ("Shine Diagonal (0=vertical band)", Range(0,2)) = 1

        [Header(UI Mask Support)]
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;

            float _HueShiftSpeed;
            float _HueShiftStrength;
            float _ShineStrength;
            float _ShineSpeed;
            float _ShineWidth;
            float _ShineAngle;

            float3 rgb2hsv(float3 c)
            {
                float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
                float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
                float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
                float d = q.x - min(q.w, q.y);
                float e = 1.0e-10;
                return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
            }

            float3 hsv2rgb(float3 c)
            {
                float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
                float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
                return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
            }

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                half4 color = (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd) * IN.color;

                // 虹の色相サイクル（虹枠用）
                if (_HueShiftSpeed != 0)
                {
                    float3 hsv = rgb2hsv(color.rgb);
                    float shifted = frac(hsv.x + _Time.y * _HueShiftSpeed);
                    hsv.x = lerp(hsv.x, shifted, _HueShiftStrength);
                    color.rgb = hsv2rgb(hsv);
                }

                // シャインスイープ（光の帯が斜めに走る）
                if (_ShineStrength > 0)
                {
                    float axis = (IN.texcoord.x + IN.texcoord.y * _ShineAngle) / (1.0 + _ShineAngle);
                    float range = 1.0 + _ShineWidth * 2.0;
                    float pos = frac(_Time.y * _ShineSpeed) * range - _ShineWidth;
                    float band = 1.0 - saturate(abs(axis - pos) / _ShineWidth);
                    band *= band;
                    color.rgb += band * _ShineStrength * color.a;
                }

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
