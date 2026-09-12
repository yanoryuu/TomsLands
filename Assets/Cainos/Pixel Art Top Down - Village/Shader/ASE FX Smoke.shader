// Made with Amplify Shader Editor v1.9.9.5
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "Cainos/Pixel Art Top Down - Village/FX Smoke"
{
	Properties
	{
		[HideInInspector] _EmissionColor("Emission Color", Color) = (1,1,1,1)
		_PixelSizeX( "Pixel Size X", Float ) = 32
		_PixelSizeY( "Pixel Size Y", Float ) = 64
		_MainTex( "Main Tex", 2D ) = "white" {}
		_MainTexSpeed( "Main Tex Speed", Float ) = 1
		_MainTexPower( "Main Tex Power", Float ) = 1
		_RampTex( "Ramp Tex", 2D ) = "white" {}
		_RampPower( "Ramp Power", Float ) = 1
		_NoiseTexSpeed( "Noise Tex Speed", Vector ) = ( -0.1, 0, 0, 0 )
		_NoisePower( "Noise Power", Float ) = 1
		_NoiseScale( "Noise Scale", Float ) = 1
		_VerticalFadePower( "Vertical Fade Power", Float ) = 2
		_AlphaClip( "Alpha Clip", Float ) = 0.5
		[HideInInspector] _texcoord( "", 2D ) = "white" {}

		[HideInInspector][NoScaleOffset] unity_Lightmaps("unity_Lightmaps", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset] unity_LightmapsInd("unity_LightmapsInd", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset] unity_ShadowMasks("unity_ShadowMasks", 2DArray) = "" {}
	}

	SubShader
	{
		LOD 0

		

		Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" "UniversalMaterialType"="Lit" "ShaderGraphShader"="true" }

		Cull Off

		HLSLINCLUDE
		#pragma target 2.0
		#pragma prefer_hlslcc gles
		// ensure rendering platforms toggle list is visible

		#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
		#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Filtering.hlsl"

		ENDHLSL

		
		Pass
		{
			Name "Sprite Lit"
            Tags { "LightMode"="Universal2D" }

			Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
			ZTest LEqual
			ZWrite Off
			Offset 0 , 0
			ColorMask RGBA
			

			HLSLPROGRAM

			#define ASE_VERSION 19905
			#define ASE_SRP_VERSION 170004


			#pragma vertex vert
			#pragma fragment frag

			#pragma multi_compile _ DEBUG_DISPLAY SKINNED_SPRITE

			#pragma multi_compile _ USE_SHAPE_LIGHT_TYPE_0
			#pragma multi_compile _ USE_SHAPE_LIGHT_TYPE_1
			#pragma multi_compile _ USE_SHAPE_LIGHT_TYPE_2
			#pragma multi_compile _ USE_SHAPE_LIGHT_TYPE_3

            #define _SURFACE_TYPE_TRANSPARENT 1
            #define ATTRIBUTES_NEED_NORMAL
            #define ATTRIBUTES_NEED_TANGENT
            #define ATTRIBUTES_NEED_TEXCOORD0
            #define ATTRIBUTES_NEED_COLOR
            #define FEATURES_GRAPH_VERTEX_NORMAL_OUTPUT
            #define FEATURES_GRAPH_VERTEX_TANGENT_OUTPUT
            #define VARYINGS_NEED_POSITION_WS
            #define VARYINGS_NEED_TEXCOORD0
            #define VARYINGS_NEED_COLOR
            #define VARYINGS_NEED_SCREENPOSITION
            #define FEATURES_GRAPH_VERTEX

			#define SHADERPASS SHADERPASS_SPRITELIT

			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/LightingUtility.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			#include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/SurfaceData2D.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Debug/Debugging2D.hlsl"

			#if USE_SHAPE_LIGHT_TYPE_0
			SHAPE_LIGHT(0)
			#endif

			#if USE_SHAPE_LIGHT_TYPE_1
			SHAPE_LIGHT(1)
			#endif

			#if USE_SHAPE_LIGHT_TYPE_2
			SHAPE_LIGHT(2)
			#endif

			#if USE_SHAPE_LIGHT_TYPE_3
			SHAPE_LIGHT(3)
			#endif

			#include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/CombinedShapeLightShared.hlsl"

			#define ASE_NEEDS_TEXTURE_COORDINATES0
			#define ASE_NEEDS_FRAG_TEXTURE_COORDINATES0
			#define ASE_NEEDS_FRAG_WORLD_POSITION


			sampler2D _RampTex;
			sampler2D _MainTex;
			CBUFFER_START( UnityPerMaterial )
			float4 _RampTex_ST;
			float2 _NoiseTexSpeed;
			float _RampPower;
			float _PixelSizeX;
			float _PixelSizeY;
			float _MainTexSpeed;
			float _MainTexPower;
			float _VerticalFadePower;
			float _NoiseScale;
			float _NoisePower;
			float _AlphaClip;
			CBUFFER_END


			struct VertexInput
			{
				float3 positionOS : POSITION;
				float3 normal : NORMAL;
				float4 tangent : TANGENT;
				float4 uv0 : TEXCOORD0;
				float4 color : COLOR;
				
				UNITY_SKINNED_VERTEX_INPUTS
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct VertexOutput
			{
				float4 positionCS : SV_POSITION;
				float4 texCoord0 : TEXCOORD0;
				float4 color : TEXCOORD1;
				float4 screenPosition : TEXCOORD2;
				float3 positionWS : TEXCOORD3;
				float4 ase_color : COLOR;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			#if ETC1_EXTERNAL_ALPHA
				TEXTURE2D(_AlphaTex); SAMPLER(sampler_AlphaTex);
				float _EnableAlphaTexture;
			#endif

			float3 mod2D289( float3 x ) { return x - floor( x * ( 1.0 / 289.0 ) ) * 289.0; }
			float2 mod2D289( float2 x ) { return x - floor( x * ( 1.0 / 289.0 ) ) * 289.0; }
			float3 permute( float3 x ) { return mod2D289( ( ( x * 34.0 ) + 1.0 ) * x ); }
			float snoise( float2 v )
			{
				const float4 C = float4( 0.211324865405187, 0.366025403784439, -0.577350269189626, 0.024390243902439 );
				float2 i = floor( v + dot( v, C.yy ) );
				float2 x0 = v - i + dot( i, C.xx );
				float2 i1;
				i1 = ( x0.x > x0.y ) ? float2( 1.0, 0.0 ) : float2( 0.0, 1.0 );
				float4 x12 = x0.xyxy + C.xxzz;
				x12.xy -= i1;
				i = mod2D289( i );
				float3 p = permute( permute( i.y + float3( 0.0, i1.y, 1.0 ) ) + i.x + float3( 0.0, i1.x, 1.0 ) );
				float3 m = max( 0.5 - float3( dot( x0, x0 ), dot( x12.xy, x12.xy ), dot( x12.zw, x12.zw ) ), 0.0 );
				m = m * m;
				m = m * m;
				float3 x = 2.0 * frac( p * C.www ) - 1.0;
				float3 h = abs( x ) - 0.5;
				float3 ox = floor( x + 0.5 );
				float3 a0 = x - ox;
				m *= 1.79284291400159 - 0.85373472095314 * ( a0 * a0 + h * h );
				float3 g;
				g.x = a0.x * x0.x + h.x * x0.y;
				g.yz = a0.yz * x12.xz + h.yz * x12.yw;
				return 130.0 * dot( m, g );
			}
			

			VertexOutput vert( VertexInput v  )
			{
				VertexOutput o = (VertexOutput)0;

				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_TRANSFER_INSTANCE_ID(v, o);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
				UNITY_SKINNED_VERTEX_COMPUTE(v);

				v.positionOS = UnityFlipSprite( v.positionOS, unity_SpriteProps.xy );

				o.ase_color = v.color;
				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = v.positionOS;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif
				float3 vertexValue = defaultVertexValue;
				#ifdef ASE_ABSOLUTE_VERTEX_POS
					v.positionOS = vertexValue;
				#else
					v.positionOS += vertexValue;
				#endif
				v.normal = v.normal;
				v.tangent.xyz = v.tangent.xyz;

				VertexPositionInputs vertexInput = GetVertexPositionInputs(v.positionOS);

				o.positionCS = vertexInput.positionCS;
				o.positionWS = vertexInput.positionWS;
				o.texCoord0 = v.uv0;
				o.color = v.color;
				o.screenPosition = vertexInput.positionNDC;
				return o;
			}

			half4 frag( VertexOutput IN  ) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(IN);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

				float4 positionCS = IN.positionCS;
				float3 positionWS = IN.positionWS;

				float2 uv_RampTex = IN.texCoord0.xy * _RampTex_ST.xy + _RampTex_ST.zw;
				float4 saferPower153 = abs( tex2D( _RampTex, uv_RampTex ) );
				float4 temp_cast_0 = (_RampPower).xxxx;
				float4 FinalColor148 = ( pow( saferPower153 , temp_cast_0 ) * IN.ase_color );
				float2 texCoord122 = IN.texCoord0.xy * float2( 1,1 ) + float2( 0,0 );
				float PixelSizeX133 = _PixelSizeX;
				float PixelSizeY134 = _PixelSizeY;
				half2 pixelateduv135 = floor( texCoord122 * float2( PixelSizeX133, PixelSizeY134 ) + float2( 0,0 ) ) / float2( PixelSizeX133, PixelSizeY134 );
				float2 appendResult124 = (float2(0.0 , ( _TimeParameters.x * ( _MainTexSpeed * -1.0 ) )));
				float4 saferPower129 = abs( tex2D( _MainTex, ( pixelateduv135 + appendResult124 ) ) );
				float4 temp_cast_1 = (_MainTexPower).xxxx;
				float4 MainTexAlpha34 = pow( saferPower129 , temp_cast_1 );
				float2 texCoord28 = IN.texCoord0.xy * float2( 1,1 ) + float2( 0,0 );
				float saferPower41 = abs( texCoord28.y );
				float VerticalFade32 = ( 1.0 - pow( saferPower41 , _VerticalFadePower ) );
				float2 appendResult191 = (float2(positionWS.x , positionWS.y));
				half2 pixelateduv101 = floor( appendResult191 * float2( 32.0, 32.0 ) + float2( 0,0 ) ) / float2( 32.0, 32.0 );
				float simplePerlin2D38 = snoise( ( pixelateduv101 + ( _TimeParameters.x * _NoiseTexSpeed ) )*_NoiseScale );
				simplePerlin2D38 = simplePerlin2D38*0.5 + 0.5;
				float NoiseAlpha119 = pow( simplePerlin2D38 , _NoisePower );
				float4 temp_cast_2 = (_AlphaClip).xxxx;
				clip( ( MainTexAlpha34 * VerticalFade32 * NoiseAlpha119 ) - temp_cast_2);
				
				float4 Color = FinalColor148;
				float4 Mask = float4(1,1,1,1);
				float3 Normal = float3( 0, 0, 1 );

				#if ETC1_EXTERNAL_ALPHA
					float4 alpha = SAMPLE_TEXTURE2D(_AlphaTex, sampler_AlphaTex, IN.texCoord0.xy);
					Color.a = lerp( Color.a, alpha.r, _EnableAlphaTexture);
				#endif

				SurfaceData2D surfaceData;
				InitializeSurfaceData(Color.rgb, Color.a, Mask, surfaceData);
				InputData2D inputData;
				InitializeInputData(IN.texCoord0.xy, half2(IN.screenPosition.xy / IN.screenPosition.w), inputData);
				SETUP_DEBUG_DATA_2D(inputData, positionWS, positionCS);
				return CombinedShapeLightShared(surfaceData, inputData);

				Color *= IN.color;
			}

			ENDHLSL
		}

		
		Pass
		{
			
            Name "Sprite Normal"
            Tags { "LightMode"="NormalsRendering" }

			Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
			ZTest LEqual
			ZWrite Off
			Offset 0 , 0
			ColorMask RGBA
			

			HLSLPROGRAM

			#define ASE_VERSION 19905
			#define ASE_SRP_VERSION 170004


			#pragma vertex vert
			#pragma fragment frag

			#pragma multi_compile _ SKINNED_SPRITE

			#define _SURFACE_TYPE_TRANSPARENT 1
            #define ATTRIBUTES_NEED_NORMAL
            #define ATTRIBUTES_NEED_TANGENT
            #define FEATURES_GRAPH_VERTEX_NORMAL_OUTPUT
            #define FEATURES_GRAPH_VERTEX_TANGENT_OUTPUT
            #define VARYINGS_NEED_NORMAL_WS
            #define VARYINGS_NEED_TANGENT_WS
            #define FEATURES_GRAPH_VERTEX

			#define SHADERPASS SHADERPASS_SPRITENORMAL

			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/NormalsRenderingShared.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			#define ASE_NEEDS_TEXTURE_COORDINATES0
			#define ASE_NEEDS_FRAG_TEXTURE_COORDINATES0
			#define ASE_NEEDS_FRAG_COLOR


			sampler2D _RampTex;
			sampler2D _MainTex;
			CBUFFER_START( UnityPerMaterial )
			float4 _RampTex_ST;
			float2 _NoiseTexSpeed;
			float _RampPower;
			float _PixelSizeX;
			float _PixelSizeY;
			float _MainTexSpeed;
			float _MainTexPower;
			float _VerticalFadePower;
			float _NoiseScale;
			float _NoisePower;
			float _AlphaClip;
			CBUFFER_END


			struct VertexInput
			{
				float3 positionOS : POSITION;
				float3 normal : NORMAL;
				float4 tangent : TANGENT;
				float4 uv0 : TEXCOORD0;
				float4 color : COLOR;
				
				UNITY_SKINNED_VERTEX_INPUTS
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct VertexOutput
			{
				float4 positionCS : SV_POSITION;
				float4 texCoord0 : TEXCOORD0;
				float4 color : TEXCOORD1;
				float3 normalWS : TEXCOORD2;
				float4 tangentWS : TEXCOORD3;
				float3 bitangentWS : TEXCOORD4;
				float4 ase_texcoord5 : TEXCOORD5;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			float3 mod2D289( float3 x ) { return x - floor( x * ( 1.0 / 289.0 ) ) * 289.0; }
			float2 mod2D289( float2 x ) { return x - floor( x * ( 1.0 / 289.0 ) ) * 289.0; }
			float3 permute( float3 x ) { return mod2D289( ( ( x * 34.0 ) + 1.0 ) * x ); }
			float snoise( float2 v )
			{
				const float4 C = float4( 0.211324865405187, 0.366025403784439, -0.577350269189626, 0.024390243902439 );
				float2 i = floor( v + dot( v, C.yy ) );
				float2 x0 = v - i + dot( i, C.xx );
				float2 i1;
				i1 = ( x0.x > x0.y ) ? float2( 1.0, 0.0 ) : float2( 0.0, 1.0 );
				float4 x12 = x0.xyxy + C.xxzz;
				x12.xy -= i1;
				i = mod2D289( i );
				float3 p = permute( permute( i.y + float3( 0.0, i1.y, 1.0 ) ) + i.x + float3( 0.0, i1.x, 1.0 ) );
				float3 m = max( 0.5 - float3( dot( x0, x0 ), dot( x12.xy, x12.xy ), dot( x12.zw, x12.zw ) ), 0.0 );
				m = m * m;
				m = m * m;
				float3 x = 2.0 * frac( p * C.www ) - 1.0;
				float3 h = abs( x ) - 0.5;
				float3 ox = floor( x + 0.5 );
				float3 a0 = x - ox;
				m *= 1.79284291400159 - 0.85373472095314 * ( a0 * a0 + h * h );
				float3 g;
				g.x = a0.x * x0.x + h.x * x0.y;
				g.yz = a0.yz * x12.xz + h.yz * x12.yw;
				return 130.0 * dot( m, g );
			}
			

			VertexOutput vert( VertexInput v  )
			{
				VertexOutput o = (VertexOutput)0;

				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_TRANSFER_INSTANCE_ID(v, o);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
				UNITY_SKINNED_VERTEX_COMPUTE(v);

				v.positionOS = UnityFlipSprite( v.positionOS, unity_SpriteProps.xy );

				float3 ase_positionWS = TransformObjectToWorld( ( v.positionOS ).xyz );
				o.ase_texcoord5.xyz = ase_positionWS;
				
				
				//setting value to unused interpolator channels and avoid initialization warnings
				o.ase_texcoord5.w = 0;
				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = v.positionOS;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif
				float3 vertexValue = defaultVertexValue;
				#ifdef ASE_ABSOLUTE_VERTEX_POS
					v.positionOS = vertexValue;
				#else
					v.positionOS += vertexValue;
				#endif
				v.normal = v.normal;
				v.tangent.xyz = v.tangent.xyz;

				VertexPositionInputs vertexInput = GetVertexPositionInputs(v.positionOS);

				o.texCoord0 = v.uv0;
				o.color = v.color;
				o.positionCS = vertexInput.positionCS;

				float3 normalWS = TransformObjectToWorldNormal(v.normal);
				o.normalWS = -GetViewForwardDir();
				float4 tangentWS = float4( TransformObjectToWorldDir(v.tangent.xyz), v.tangent.w);
				o.tangentWS = normalize(tangentWS);
				half crossSign = (tangentWS.w > 0.0 ? 1.0 : -1.0) * GetOddNegativeScale();
				o.bitangentWS = crossSign * cross(normalWS, tangentWS.xyz) * tangentWS.w;
				return o;
			}

			half4 frag( VertexOutput IN  ) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(IN);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

				float2 uv_RampTex = IN.texCoord0.xy * _RampTex_ST.xy + _RampTex_ST.zw;
				float4 saferPower153 = abs( tex2D( _RampTex, uv_RampTex ) );
				float4 temp_cast_0 = (_RampPower).xxxx;
				float4 FinalColor148 = ( pow( saferPower153 , temp_cast_0 ) * IN.color );
				float2 texCoord122 = IN.texCoord0.xy * float2( 1,1 ) + float2( 0,0 );
				float PixelSizeX133 = _PixelSizeX;
				float PixelSizeY134 = _PixelSizeY;
				half2 pixelateduv135 = floor( texCoord122 * float2( PixelSizeX133, PixelSizeY134 ) + float2( 0,0 ) ) / float2( PixelSizeX133, PixelSizeY134 );
				float2 appendResult124 = (float2(0.0 , ( _TimeParameters.x * ( _MainTexSpeed * -1.0 ) )));
				float4 saferPower129 = abs( tex2D( _MainTex, ( pixelateduv135 + appendResult124 ) ) );
				float4 temp_cast_1 = (_MainTexPower).xxxx;
				float4 MainTexAlpha34 = pow( saferPower129 , temp_cast_1 );
				float2 texCoord28 = IN.texCoord0.xy * float2( 1,1 ) + float2( 0,0 );
				float saferPower41 = abs( texCoord28.y );
				float VerticalFade32 = ( 1.0 - pow( saferPower41 , _VerticalFadePower ) );
				float3 ase_positionWS = IN.ase_texcoord5.xyz;
				float2 appendResult191 = (float2(ase_positionWS.x , ase_positionWS.y));
				half2 pixelateduv101 = floor( appendResult191 * float2( 32.0, 32.0 ) + float2( 0,0 ) ) / float2( 32.0, 32.0 );
				float simplePerlin2D38 = snoise( ( pixelateduv101 + ( _TimeParameters.x * _NoiseTexSpeed ) )*_NoiseScale );
				simplePerlin2D38 = simplePerlin2D38*0.5 + 0.5;
				float NoiseAlpha119 = pow( simplePerlin2D38 , _NoisePower );
				float4 temp_cast_2 = (_AlphaClip).xxxx;
				clip( ( MainTexAlpha34 * VerticalFade32 * NoiseAlpha119 ) - temp_cast_2);
				
				float4 Color = FinalColor148;
				float3 Normal = float3( 0, 0, 1 );

				Color *= IN.color;

				return NormalsRenderingShared(Color, Normal, IN.tangentWS.xyz, IN.bitangentWS, IN.normalWS);
			}

			ENDHLSL
		}

		
		Pass
		{
			
            Name "Sprite Forward"
            Tags { "LightMode"="UniversalForward" }

			Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
			ZTest LEqual
			ZWrite Off
			Offset 0 , 0
			ColorMask RGBA
			

			HLSLPROGRAM

			#define ASE_VERSION 19905
			#define ASE_SRP_VERSION 170004


			#pragma vertex vert
			#pragma fragment frag

			#pragma multi_compile _ SKINNED_SPRITE

            #define _SURFACE_TYPE_TRANSPARENT 1
            #define ATTRIBUTES_NEED_NORMAL
            #define ATTRIBUTES_NEED_TANGENT
            #define ATTRIBUTES_NEED_TEXCOORD0
            #define ATTRIBUTES_NEED_COLOR
            #define FEATURES_GRAPH_VERTEX_NORMAL_OUTPUT
            #define FEATURES_GRAPH_VERTEX_TANGENT_OUTPUT
            #define VARYINGS_NEED_POSITION_WS
            #define VARYINGS_NEED_TEXCOORD0
            #define VARYINGS_NEED_COLOR
            #define FEATURES_GRAPH_VERTEX

			#define SHADERPASS SHADERPASS_SPRITEFORWARD

			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			#include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/SurfaceData2D.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Debug/Debugging2D.hlsl"

			#define ASE_NEEDS_TEXTURE_COORDINATES0
			#define ASE_NEEDS_FRAG_TEXTURE_COORDINATES0
			#define ASE_NEEDS_FRAG_COLOR
			#define ASE_NEEDS_FRAG_WORLD_POSITION


			sampler2D _RampTex;
			sampler2D _MainTex;
			CBUFFER_START( UnityPerMaterial )
			float4 _RampTex_ST;
			float2 _NoiseTexSpeed;
			float _RampPower;
			float _PixelSizeX;
			float _PixelSizeY;
			float _MainTexSpeed;
			float _MainTexPower;
			float _VerticalFadePower;
			float _NoiseScale;
			float _NoisePower;
			float _AlphaClip;
			CBUFFER_END


			struct VertexInput
			{
				float3 positionOS : POSITION;
				float3 normal : NORMAL;
				float4 tangent : TANGENT;
				float4 uv0 : TEXCOORD0;
				float4 color : COLOR;
				
				UNITY_SKINNED_VERTEX_INPUTS
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct VertexOutput
			{
				float4 positionCS : SV_POSITION;
				float4 texCoord0 : TEXCOORD0;
				float4 color : TEXCOORD1;
				float3 positionWS : TEXCOORD2;
				
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			#if ETC1_EXTERNAL_ALPHA
				TEXTURE2D( _AlphaTex ); SAMPLER( sampler_AlphaTex );
				float _EnableAlphaTexture;
			#endif

			float3 mod2D289( float3 x ) { return x - floor( x * ( 1.0 / 289.0 ) ) * 289.0; }
			float2 mod2D289( float2 x ) { return x - floor( x * ( 1.0 / 289.0 ) ) * 289.0; }
			float3 permute( float3 x ) { return mod2D289( ( ( x * 34.0 ) + 1.0 ) * x ); }
			float snoise( float2 v )
			{
				const float4 C = float4( 0.211324865405187, 0.366025403784439, -0.577350269189626, 0.024390243902439 );
				float2 i = floor( v + dot( v, C.yy ) );
				float2 x0 = v - i + dot( i, C.xx );
				float2 i1;
				i1 = ( x0.x > x0.y ) ? float2( 1.0, 0.0 ) : float2( 0.0, 1.0 );
				float4 x12 = x0.xyxy + C.xxzz;
				x12.xy -= i1;
				i = mod2D289( i );
				float3 p = permute( permute( i.y + float3( 0.0, i1.y, 1.0 ) ) + i.x + float3( 0.0, i1.x, 1.0 ) );
				float3 m = max( 0.5 - float3( dot( x0, x0 ), dot( x12.xy, x12.xy ), dot( x12.zw, x12.zw ) ), 0.0 );
				m = m * m;
				m = m * m;
				float3 x = 2.0 * frac( p * C.www ) - 1.0;
				float3 h = abs( x ) - 0.5;
				float3 ox = floor( x + 0.5 );
				float3 a0 = x - ox;
				m *= 1.79284291400159 - 0.85373472095314 * ( a0 * a0 + h * h );
				float3 g;
				g.x = a0.x * x0.x + h.x * x0.y;
				g.yz = a0.yz * x12.xz + h.yz * x12.yw;
				return 130.0 * dot( m, g );
			}
			

			VertexOutput vert( VertexInput v  )
			{
				VertexOutput o = (VertexOutput)0;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_TRANSFER_INSTANCE_ID(v, o);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
				UNITY_SKINNED_VERTEX_COMPUTE( v );

				v.positionOS = UnityFlipSprite( v.positionOS, unity_SpriteProps.xy );

				
				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = v.positionOS;
				#else
					float3 defaultVertexValue = float3( 0, 0, 0 );
				#endif
				float3 vertexValue = defaultVertexValue;
				#ifdef ASE_ABSOLUTE_VERTEX_POS
					v.positionOS = vertexValue;
				#else
					v.positionOS += vertexValue;
				#endif
				v.normal = v.normal;
				v.tangent.xyz = v.tangent.xyz;

				VertexPositionInputs vertexInput = GetVertexPositionInputs(v.positionOS);

				o.positionCS = vertexInput.positionCS;
				o.positionWS = vertexInput.positionWS;
				o.texCoord0 = v.uv0;
				o.color = v.color;

				return o;
			}

			half4 frag( VertexOutput IN  ) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(IN);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

				float4 positionCS = IN.positionCS;
				float3 positionWS = IN.positionWS;

				float2 uv_RampTex = IN.texCoord0.xy * _RampTex_ST.xy + _RampTex_ST.zw;
				float4 saferPower153 = abs( tex2D( _RampTex, uv_RampTex ) );
				float4 temp_cast_0 = (_RampPower).xxxx;
				float4 FinalColor148 = ( pow( saferPower153 , temp_cast_0 ) * IN.color );
				float2 texCoord122 = IN.texCoord0.xy * float2( 1,1 ) + float2( 0,0 );
				float PixelSizeX133 = _PixelSizeX;
				float PixelSizeY134 = _PixelSizeY;
				half2 pixelateduv135 = floor( texCoord122 * float2( PixelSizeX133, PixelSizeY134 ) + float2( 0,0 ) ) / float2( PixelSizeX133, PixelSizeY134 );
				float2 appendResult124 = (float2(0.0 , ( _TimeParameters.x * ( _MainTexSpeed * -1.0 ) )));
				float4 saferPower129 = abs( tex2D( _MainTex, ( pixelateduv135 + appendResult124 ) ) );
				float4 temp_cast_1 = (_MainTexPower).xxxx;
				float4 MainTexAlpha34 = pow( saferPower129 , temp_cast_1 );
				float2 texCoord28 = IN.texCoord0.xy * float2( 1,1 ) + float2( 0,0 );
				float saferPower41 = abs( texCoord28.y );
				float VerticalFade32 = ( 1.0 - pow( saferPower41 , _VerticalFadePower ) );
				float2 appendResult191 = (float2(positionWS.x , positionWS.y));
				half2 pixelateduv101 = floor( appendResult191 * float2( 32.0, 32.0 ) + float2( 0,0 ) ) / float2( 32.0, 32.0 );
				float simplePerlin2D38 = snoise( ( pixelateduv101 + ( _TimeParameters.x * _NoiseTexSpeed ) )*_NoiseScale );
				simplePerlin2D38 = simplePerlin2D38*0.5 + 0.5;
				float NoiseAlpha119 = pow( simplePerlin2D38 , _NoisePower );
				float4 temp_cast_2 = (_AlphaClip).xxxx;
				clip( ( MainTexAlpha34 * VerticalFade32 * NoiseAlpha119 ) - temp_cast_2);
				
				float4 Color = FinalColor148;

				#if defined(DEBUG_DISPLAY)
					SurfaceData2D surfaceData;
					InitializeSurfaceData(Color.rgb, Color.a, surfaceData);
					InputData2D inputData;
					InitializeInputData(positionWS.xy, half2(IN.texCoord0.xy), inputData);
					half4 debugColor = 0;

					SETUP_DEBUG_DATA_2D(inputData, positionWS, positionCS);

					if (CanDebugOverrideOutputColor(surfaceData, inputData, debugColor))
					{
						return debugColor;
					}
				#endif

				#if ETC1_EXTERNAL_ALPHA
					float4 alpha = SAMPLE_TEXTURE2D( _AlphaTex, sampler_AlphaTex, IN.texCoord0.xy );
					Color.a = lerp( Color.a, alpha.r, _EnableAlphaTexture );
				#endif

				Color *= IN.color;
				return Color;
			}

			ENDHLSL
		}
		
        Pass
        {
			
            Name "SceneSelectionPass"
            Tags { "LightMode"="SceneSelectionPass" }

            Cull Off

            HLSLPROGRAM

			#define ASE_VERSION 19905
			#define ASE_SRP_VERSION 170004


			#pragma vertex vert
			#pragma fragment frag

			#pragma multi_compile _ DEBUG_DISPLAY SKINNED_SPRITE

            #define _SURFACE_TYPE_TRANSPARENT 1
            #define ATTRIBUTES_NEED_NORMAL
            #define ATTRIBUTES_NEED_TANGENT
            #define FEATURES_GRAPH_VERTEX_NORMAL_OUTPUT
            #define FEATURES_GRAPH_VERTEX_TANGENT_OUTPUT
            #define FEATURES_GRAPH_VERTEX

            #define SHADERPASS SHADERPASS_DEPTHONLY
			#define SCENESELECTIONPASS 1

			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			#define ASE_NEEDS_TEXTURE_COORDINATES0
			#define ASE_NEEDS_FRAG_TEXTURE_COORDINATES0


			sampler2D _RampTex;
			sampler2D _MainTex;
			CBUFFER_START( UnityPerMaterial )
			float4 _RampTex_ST;
			float2 _NoiseTexSpeed;
			float _RampPower;
			float _PixelSizeX;
			float _PixelSizeY;
			float _MainTexSpeed;
			float _MainTexPower;
			float _VerticalFadePower;
			float _NoiseScale;
			float _NoisePower;
			float _AlphaClip;
			CBUFFER_END


            struct VertexInput
			{
				float3 positionOS : POSITION;
				float3 normal : NORMAL;
				float4 tangent : TANGENT;
				float4 ase_texcoord : TEXCOORD0;
				float4 ase_color : COLOR;
				UNITY_SKINNED_VERTEX_INPUTS
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct VertexOutput
			{
				float4 positionCS : SV_POSITION;
				float4 ase_texcoord : TEXCOORD0;
				float4 ase_color : COLOR;
				float4 ase_texcoord1 : TEXCOORD1;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

            int _ObjectId;
            int _PassValue;

			float3 mod2D289( float3 x ) { return x - floor( x * ( 1.0 / 289.0 ) ) * 289.0; }
			float2 mod2D289( float2 x ) { return x - floor( x * ( 1.0 / 289.0 ) ) * 289.0; }
			float3 permute( float3 x ) { return mod2D289( ( ( x * 34.0 ) + 1.0 ) * x ); }
			float snoise( float2 v )
			{
				const float4 C = float4( 0.211324865405187, 0.366025403784439, -0.577350269189626, 0.024390243902439 );
				float2 i = floor( v + dot( v, C.yy ) );
				float2 x0 = v - i + dot( i, C.xx );
				float2 i1;
				i1 = ( x0.x > x0.y ) ? float2( 1.0, 0.0 ) : float2( 0.0, 1.0 );
				float4 x12 = x0.xyxy + C.xxzz;
				x12.xy -= i1;
				i = mod2D289( i );
				float3 p = permute( permute( i.y + float3( 0.0, i1.y, 1.0 ) ) + i.x + float3( 0.0, i1.x, 1.0 ) );
				float3 m = max( 0.5 - float3( dot( x0, x0 ), dot( x12.xy, x12.xy ), dot( x12.zw, x12.zw ) ), 0.0 );
				m = m * m;
				m = m * m;
				float3 x = 2.0 * frac( p * C.www ) - 1.0;
				float3 h = abs( x ) - 0.5;
				float3 ox = floor( x + 0.5 );
				float3 a0 = x - ox;
				m *= 1.79284291400159 - 0.85373472095314 * ( a0 * a0 + h * h );
				float3 g;
				g.x = a0.x * x0.x + h.x * x0.y;
				g.yz = a0.yz * x12.xz + h.yz * x12.yw;
				return 130.0 * dot( m, g );
			}
			

			VertexOutput vert(VertexInput v )
			{
				VertexOutput o = (VertexOutput)0;

				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_TRANSFER_INSTANCE_ID(v, o);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
				UNITY_SKINNED_VERTEX_COMPUTE(v);

				v.positionOS = UnityFlipSprite( v.positionOS, unity_SpriteProps.xy );

				float3 ase_positionWS = TransformObjectToWorld( ( v.positionOS ).xyz );
				o.ase_texcoord1.xyz = ase_positionWS;
				
				o.ase_texcoord.xy = v.ase_texcoord.xy;
				o.ase_color = v.ase_color;
				
				//setting value to unused interpolator channels and avoid initialization warnings
				o.ase_texcoord.zw = 0;
				o.ase_texcoord1.w = 0;
				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = v.positionOS;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif
				float3 vertexValue = defaultVertexValue;
				#ifdef ASE_ABSOLUTE_VERTEX_POS
					v.positionOS = vertexValue;
				#else
					v.positionOS += vertexValue;
				#endif

				VertexPositionInputs vertexInput = GetVertexPositionInputs(v.positionOS);
				float3 positionWS = TransformObjectToWorld(v.positionOS);
				o.positionCS = TransformWorldToHClip(positionWS);

				return o;
			}

			half4 frag(VertexOutput IN) : SV_TARGET
			{
				float2 uv_RampTex = IN.ase_texcoord.xy * _RampTex_ST.xy + _RampTex_ST.zw;
				float4 saferPower153 = abs( tex2D( _RampTex, uv_RampTex ) );
				float4 temp_cast_0 = (_RampPower).xxxx;
				float4 FinalColor148 = ( pow( saferPower153 , temp_cast_0 ) * IN.ase_color );
				float2 texCoord122 = IN.ase_texcoord.xy * float2( 1,1 ) + float2( 0,0 );
				float PixelSizeX133 = _PixelSizeX;
				float PixelSizeY134 = _PixelSizeY;
				half2 pixelateduv135 = floor( texCoord122 * float2( PixelSizeX133, PixelSizeY134 ) + float2( 0,0 ) ) / float2( PixelSizeX133, PixelSizeY134 );
				float2 appendResult124 = (float2(0.0 , ( _TimeParameters.x * ( _MainTexSpeed * -1.0 ) )));
				float4 saferPower129 = abs( tex2D( _MainTex, ( pixelateduv135 + appendResult124 ) ) );
				float4 temp_cast_1 = (_MainTexPower).xxxx;
				float4 MainTexAlpha34 = pow( saferPower129 , temp_cast_1 );
				float2 texCoord28 = IN.ase_texcoord.xy * float2( 1,1 ) + float2( 0,0 );
				float saferPower41 = abs( texCoord28.y );
				float VerticalFade32 = ( 1.0 - pow( saferPower41 , _VerticalFadePower ) );
				float3 ase_positionWS = IN.ase_texcoord1.xyz;
				float2 appendResult191 = (float2(ase_positionWS.x , ase_positionWS.y));
				half2 pixelateduv101 = floor( appendResult191 * float2( 32.0, 32.0 ) + float2( 0,0 ) ) / float2( 32.0, 32.0 );
				float simplePerlin2D38 = snoise( ( pixelateduv101 + ( _TimeParameters.x * _NoiseTexSpeed ) )*_NoiseScale );
				simplePerlin2D38 = simplePerlin2D38*0.5 + 0.5;
				float NoiseAlpha119 = pow( simplePerlin2D38 , _NoisePower );
				float4 temp_cast_2 = (_AlphaClip).xxxx;
				clip( ( MainTexAlpha34 * VerticalFade32 * NoiseAlpha119 ) - temp_cast_2);
				
				float4 Color = FinalColor148;

				half4 outColor = half4(_ObjectId, _PassValue, 1.0, 1.0);
				return outColor;
			}

            ENDHLSL
        }

		
        Pass
        {
			
            Name "ScenePickingPass"
            Tags { "LightMode"="Picking" }

			Cull Off

            HLSLPROGRAM

			#define ASE_VERSION 19905
			#define ASE_SRP_VERSION 170004


			#pragma vertex vert
			#pragma fragment frag

			#pragma multi_compile _ DEBUG_DISPLAY SKINNED_SPRITE

            #define _SURFACE_TYPE_TRANSPARENT 1
            #define ATTRIBUTES_NEED_NORMAL
            #define ATTRIBUTES_NEED_TANGENT
            #define FEATURES_GRAPH_VERTEX_NORMAL_OUTPUT
            #define FEATURES_GRAPH_VERTEX_TANGENT_OUTPUT
            #define FEATURES_GRAPH_VERTEX

            #define SHADERPASS SHADERPASS_DEPTHONLY
			#define SCENEPICKINGPASS 1

			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

        	#define ASE_NEEDS_TEXTURE_COORDINATES0
        	#define ASE_NEEDS_FRAG_TEXTURE_COORDINATES0


			sampler2D _RampTex;
			sampler2D _MainTex;
			CBUFFER_START( UnityPerMaterial )
			float4 _RampTex_ST;
			float2 _NoiseTexSpeed;
			float _RampPower;
			float _PixelSizeX;
			float _PixelSizeY;
			float _MainTexSpeed;
			float _MainTexPower;
			float _VerticalFadePower;
			float _NoiseScale;
			float _NoisePower;
			float _AlphaClip;
			CBUFFER_END


            struct VertexInput
			{
				float3 positionOS : POSITION;
				float3 normal : NORMAL;
				float4 tangent : TANGENT;
				float4 ase_texcoord : TEXCOORD0;
				float4 ase_color : COLOR;
				UNITY_SKINNED_VERTEX_INPUTS
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct VertexOutput
			{
				float4 positionCS : SV_POSITION;
				float4 ase_texcoord : TEXCOORD0;
				float4 ase_color : COLOR;
				float4 ase_texcoord1 : TEXCOORD1;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

            float4 _SelectionID;

			float3 mod2D289( float3 x ) { return x - floor( x * ( 1.0 / 289.0 ) ) * 289.0; }
			float2 mod2D289( float2 x ) { return x - floor( x * ( 1.0 / 289.0 ) ) * 289.0; }
			float3 permute( float3 x ) { return mod2D289( ( ( x * 34.0 ) + 1.0 ) * x ); }
			float snoise( float2 v )
			{
				const float4 C = float4( 0.211324865405187, 0.366025403784439, -0.577350269189626, 0.024390243902439 );
				float2 i = floor( v + dot( v, C.yy ) );
				float2 x0 = v - i + dot( i, C.xx );
				float2 i1;
				i1 = ( x0.x > x0.y ) ? float2( 1.0, 0.0 ) : float2( 0.0, 1.0 );
				float4 x12 = x0.xyxy + C.xxzz;
				x12.xy -= i1;
				i = mod2D289( i );
				float3 p = permute( permute( i.y + float3( 0.0, i1.y, 1.0 ) ) + i.x + float3( 0.0, i1.x, 1.0 ) );
				float3 m = max( 0.5 - float3( dot( x0, x0 ), dot( x12.xy, x12.xy ), dot( x12.zw, x12.zw ) ), 0.0 );
				m = m * m;
				m = m * m;
				float3 x = 2.0 * frac( p * C.www ) - 1.0;
				float3 h = abs( x ) - 0.5;
				float3 ox = floor( x + 0.5 );
				float3 a0 = x - ox;
				m *= 1.79284291400159 - 0.85373472095314 * ( a0 * a0 + h * h );
				float3 g;
				g.x = a0.x * x0.x + h.x * x0.y;
				g.yz = a0.yz * x12.xz + h.yz * x12.yw;
				return 130.0 * dot( m, g );
			}
			

			VertexOutput vert(VertexInput v  )
			{
				VertexOutput o = (VertexOutput)0;

				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_TRANSFER_INSTANCE_ID(v, o);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
				UNITY_SKINNED_VERTEX_COMPUTE(v);

				v.positionOS = UnityFlipSprite( v.positionOS, unity_SpriteProps.xy );

				float3 ase_positionWS = TransformObjectToWorld( ( v.positionOS ).xyz );
				o.ase_texcoord1.xyz = ase_positionWS;
				
				o.ase_texcoord.xy = v.ase_texcoord.xy;
				o.ase_color = v.ase_color;
				
				//setting value to unused interpolator channels and avoid initialization warnings
				o.ase_texcoord.zw = 0;
				o.ase_texcoord1.w = 0;
				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = v.positionOS;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif
				float3 vertexValue = defaultVertexValue;
				#ifdef ASE_ABSOLUTE_VERTEX_POS
					v.positionOS = vertexValue;
				#else
					v.positionOS += vertexValue;
				#endif

				VertexPositionInputs vertexInput = GetVertexPositionInputs(v.positionOS);
				float3 positionWS = TransformObjectToWorld(v.positionOS);
				o.positionCS = TransformWorldToHClip(positionWS);

				return o;
			}

			half4 frag(VertexOutput IN ) : SV_TARGET
			{
				float2 uv_RampTex = IN.ase_texcoord.xy * _RampTex_ST.xy + _RampTex_ST.zw;
				float4 saferPower153 = abs( tex2D( _RampTex, uv_RampTex ) );
				float4 temp_cast_0 = (_RampPower).xxxx;
				float4 FinalColor148 = ( pow( saferPower153 , temp_cast_0 ) * IN.ase_color );
				float2 texCoord122 = IN.ase_texcoord.xy * float2( 1,1 ) + float2( 0,0 );
				float PixelSizeX133 = _PixelSizeX;
				float PixelSizeY134 = _PixelSizeY;
				half2 pixelateduv135 = floor( texCoord122 * float2( PixelSizeX133, PixelSizeY134 ) + float2( 0,0 ) ) / float2( PixelSizeX133, PixelSizeY134 );
				float2 appendResult124 = (float2(0.0 , ( _TimeParameters.x * ( _MainTexSpeed * -1.0 ) )));
				float4 saferPower129 = abs( tex2D( _MainTex, ( pixelateduv135 + appendResult124 ) ) );
				float4 temp_cast_1 = (_MainTexPower).xxxx;
				float4 MainTexAlpha34 = pow( saferPower129 , temp_cast_1 );
				float2 texCoord28 = IN.ase_texcoord.xy * float2( 1,1 ) + float2( 0,0 );
				float saferPower41 = abs( texCoord28.y );
				float VerticalFade32 = ( 1.0 - pow( saferPower41 , _VerticalFadePower ) );
				float3 ase_positionWS = IN.ase_texcoord1.xyz;
				float2 appendResult191 = (float2(ase_positionWS.x , ase_positionWS.y));
				half2 pixelateduv101 = floor( appendResult191 * float2( 32.0, 32.0 ) + float2( 0,0 ) ) / float2( 32.0, 32.0 );
				float simplePerlin2D38 = snoise( ( pixelateduv101 + ( _TimeParameters.x * _NoiseTexSpeed ) )*_NoiseScale );
				simplePerlin2D38 = simplePerlin2D38*0.5 + 0.5;
				float NoiseAlpha119 = pow( simplePerlin2D38 , _NoisePower );
				float4 temp_cast_2 = (_AlphaClip).xxxx;
				clip( ( MainTexAlpha34 * VerticalFade32 * NoiseAlpha119 ) - temp_cast_2);
				
				float4 Color = FinalColor148;
				half4 outColor = unity_SelectionID;
				return outColor;
			}

            ENDHLSL
        }
		
	}
	CustomEditor "AmplifyShaderEditor.MaterialInspector"
	FallBack "Hidden/Shader Graph/FallbackError"
	
	Fallback "Hidden/InternalErrorShader"
}
/*ASEBEGIN
Version=19905
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;131;-2436.204,-1022.021;Inherit;False;Property;_PixelSizeX;Pixel Size X;0;0;Create;True;0;0;0;False;0;False;32;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;132;-2447.077,-904.2691;Inherit;False;Property;_PixelSizeY;Pixel Size Y;1;0;Create;True;0;0;0;False;0;False;64;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;126;-2565.861,-146.5044;Inherit;False;Property;_MainTexSpeed;Main Tex Speed;3;0;Create;True;0;0;0;False;0;False;1;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;134;-2250.109,-905.4691;Inherit;False;PixelSizeY;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;133;-2239.898,-1022.951;Inherit;False;PixelSizeX;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleTimeNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;123;-2531.778,-250.2834;Inherit;False;1;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;127;-2367.001,-147.7563;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;-1;False;1;FLOAT;0
Node;AmplifyShaderEditor.WorldPosInputsNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;190;-3163.208,1330.809;Inherit;False;0;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.Vector2Node, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;46;-3106.565,1654.07;Inherit;False;Property;_NoiseTexSpeed;Noise Tex Speed;7;0;Create;True;0;0;0;False;0;False;-0.1,0;0,2;0;3;FLOAT2;0;FLOAT;1;FLOAT;2
Node;AmplifyShaderEditor.SimpleTimeNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;43;-3093.342,1569.317;Inherit;False;1;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;136;-2408.966,-446.9891;Inherit;False;133;PixelSizeX;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;137;-2408.489,-365.6192;Inherit;False;134;PixelSizeY;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;125;-2242.979,-253.2948;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.TextureCoordinatesNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;122;-2456.828,-584.5887;Inherit;False;0;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.DynamicAppendNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;191;-2970.412,1341.47;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.TextureCoordinatesNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;28;-2793.551,722.0574;Inherit;False;0;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.DynamicAppendNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;124;-2102.302,-274.4934;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.TFHCPixelate, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;135;-2206.521,-580.4277;Inherit;False;4;0;FLOAT2;0,0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.TFHCPixelate, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;101;-2813.354,1345.524;Inherit;False;4;0;FLOAT2;0,0;False;1;FLOAT;32;False;2;FLOAT;32;False;3;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;44;-2886.429,1581.212;Inherit;True;2;2;0;FLOAT;0;False;1;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.BreakToComponentsNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;29;-2515.492,723.789;Inherit;False;FLOAT2;1;0;FLOAT2;0,0;False;16;FLOAT;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT;5;FLOAT;6;FLOAT;7;FLOAT;8;FLOAT;9;FLOAT;10;FLOAT;11;FLOAT;12;FLOAT;13;FLOAT;14;FLOAT;15
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;50;-2580.907,833.5568;Inherit;False;Property;_VerticalFadePower;Vertical Fade Power;10;0;Create;True;0;0;0;False;0;False;2;5;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;171;-1954.755,-431.6284;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleAddOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;103;-2590.962,1399.683;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;193;-2390.796,1608.516;Inherit;False;Property;_NoiseScale;Noise Scale;9;0;Create;True;0;0;0;False;0;False;1;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;15;-1805.953,-497.0108;Inherit;True;Property;_MainTex;Main Tex;2;0;Create;True;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;False;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.SamplerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;147;-2071.618,-31.1125;Inherit;True;Property;_RampTex;Ramp Tex;5;0;Create;True;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;False;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;154;-1952.387,179.4195;Inherit;False;Property;_RampPower;Ramp Power;6;0;Create;True;0;0;0;False;0;False;1;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;130;-1686.804,-277.841;Inherit;False;Property;_MainTexPower;Main Tex Power;4;0;Create;True;0;0;0;False;0;False;1;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.PowerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;41;-2335.582,726.612;Inherit;True;True;2;0;FLOAT;0;False;1;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;156;-2103.083,1650.13;Inherit;False;Property;_NoisePower;Noise Power;8;0;Create;True;0;0;0;False;0;False;1;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.NoiseGeneratorNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;38;-2179.851,1383.815;Inherit;True;Simplex2D;True;False;2;0;FLOAT2;0,0;False;1;FLOAT;3;False;1;FLOAT;0
Node;AmplifyShaderEditor.VertexColorNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;150;-1789.222,208.3922;Inherit;False;0;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.PowerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;153;-1710.201,38.30293;Inherit;False;True;2;0;COLOR;0,0,0,0;False;1;FLOAT;1;False;1;COLOR;0
Node;AmplifyShaderEditor.PowerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;129;-1494.858,-369.841;Inherit;False;True;2;0;COLOR;0,0,0,0;False;1;FLOAT;1;False;1;COLOR;0
Node;AmplifyShaderEditor.OneMinusNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;40;-2084.799,726.612;Inherit;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.PowerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;155;-1898.963,1582.089;Inherit;False;False;2;0;FLOAT;0;False;1;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;34;-1390.655,-474.8796;Inherit;False;MainTexAlpha;-1;True;1;0;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;151;-1553.31,76.77698;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;32;-1873.544,732.0134;Inherit;False;VerticalFade;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;119;-1727.59,1391.986;Inherit;False;NoiseAlpha;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;33;-526.0042,399.9357;Inherit;False;32;VerticalFade;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;148;-1387.112,90.23099;Inherit;False;FinalColor;-1;True;1;0;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;35;-527.5416,289.2814;Inherit;False;34;MainTexAlpha;1;0;OBJECT;;False;1;COLOR;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;120;-521.0384,497.1593;Inherit;False;119;NoiseAlpha;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;116;-286.6084,369.1349;Inherit;False;3;3;0;COLOR;0,0,0,0;False;1;FLOAT;0;False;2;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;115;-216.8457,561.6535;Inherit;False;Property;_AlphaClip;Alpha Clip;11;0;Create;True;0;0;0;False;0;False;0.5;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;152;-515.0389,126.9899;Inherit;False;148;FinalColor;1;0;OBJECT;;False;1;COLOR;0
Node;AmplifyShaderEditor.ClipNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;114;18.16949,172.4248;Inherit;False;3;0;COLOR;0,0,0,0;False;1;COLOR;0.9,0,0,0;False;2;COLOR;0.9,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;108;117.3155,97.40141;Float;False;False;-1;2;AmplifyShaderEditor.MaterialInspector;0;1;New Amplify Shader;199187dac283dbe4a8cb1ea611d70c58;True;Sprite Normal;0;1;Sprite Normal;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;2;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;5;RenderPipeline=UniversalPipeline;RenderType=Transparent=RenderType;Queue=Transparent=Queue=0;UniversalMaterialType=Lit;ShaderGraphShader=true;True;0;True;14;all;0;False;True;2;5;False;;10;False;;3;1;False;;10;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;True;True;True;True;0;False;;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;True;2;False;;True;3;False;;True;True;0;False;;0;False;;True;1;LightMode=NormalsRendering;False;False;0;Hidden/InternalErrorShader;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;109;117.3155,97.40141;Float;False;False;-1;2;AmplifyShaderEditor.MaterialInspector;0;1;New Amplify Shader;199187dac283dbe4a8cb1ea611d70c58;True;Sprite Forward;0;2;Sprite Forward;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;2;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;5;RenderPipeline=UniversalPipeline;RenderType=Transparent=RenderType;Queue=Transparent=Queue=0;UniversalMaterialType=Lit;ShaderGraphShader=true;True;0;True;14;all;0;False;True;2;5;False;;10;False;;3;1;False;;10;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;True;True;True;True;0;False;;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;True;2;False;;True;3;False;;True;True;0;False;;0;False;;True;1;LightMode=UniversalForward;False;False;0;Hidden/InternalErrorShader;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;110;117.3155,97.40141;Float;False;False;-1;2;AmplifyShaderEditor.MaterialInspector;0;1;New Amplify Shader;199187dac283dbe4a8cb1ea611d70c58;True;SceneSelectionPass;0;3;SceneSelectionPass;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;2;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;5;RenderPipeline=UniversalPipeline;RenderType=Transparent=RenderType;Queue=Transparent=Queue=0;UniversalMaterialType=Lit;ShaderGraphShader=true;True;0;True;14;all;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;2;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;1;LightMode=SceneSelectionPass;False;False;0;Hidden/InternalErrorShader;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;111;117.3155,97.40141;Float;False;False;-1;2;AmplifyShaderEditor.MaterialInspector;0;1;New Amplify Shader;199187dac283dbe4a8cb1ea611d70c58;True;ScenePickingPass;0;4;ScenePickingPass;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;2;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;5;RenderPipeline=UniversalPipeline;RenderType=Transparent=RenderType;Queue=Transparent=Queue=0;UniversalMaterialType=Lit;ShaderGraphShader=true;True;0;True;14;all;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;2;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;1;LightMode=Picking;False;False;0;Hidden/InternalErrorShader;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;107;294.1099,192.0321;Float;False;True;-1;2;AmplifyShaderEditor.MaterialInspector;0;18;Cainos/Pixel Art Top Down - Village/FX Smoke;199187dac283dbe4a8cb1ea611d70c58;True;Sprite Lit;0;0;Sprite Lit;6;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;2;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;5;RenderPipeline=UniversalPipeline;RenderType=Transparent=RenderType;Queue=Transparent=Queue=0;UniversalMaterialType=Lit;ShaderGraphShader=true;True;0;True;14;all;0;False;True;2;5;False;;10;False;;3;1;False;;10;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;True;True;True;True;0;False;;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;True;2;False;;True;3;False;;True;True;0;False;;0;False;;True;1;LightMode=Universal2D;False;False;0;Hidden/InternalErrorShader;0;0;Standard;3;Vertex Position;1;0;Debug Display;0;0;External Alpha;0;0;0;5;True;True;True;True;True;False;;False;0
WireConnection;134;0;132;0
WireConnection;133;0;131;0
WireConnection;127;0;126;0
WireConnection;125;0;123;0
WireConnection;125;1;127;0
WireConnection;191;0;190;1
WireConnection;191;1;190;2
WireConnection;124;1;125;0
WireConnection;135;0;122;0
WireConnection;135;1;136;0
WireConnection;135;2;137;0
WireConnection;101;0;191;0
WireConnection;44;0;43;0
WireConnection;44;1;46;0
WireConnection;29;0;28;0
WireConnection;171;0;135;0
WireConnection;171;1;124;0
WireConnection;103;0;101;0
WireConnection;103;1;44;0
WireConnection;15;1;171;0
WireConnection;41;0;29;1
WireConnection;41;1;50;0
WireConnection;38;0;103;0
WireConnection;38;1;193;0
WireConnection;153;0;147;0
WireConnection;153;1;154;0
WireConnection;129;0;15;0
WireConnection;129;1;130;0
WireConnection;40;0;41;0
WireConnection;155;0;38;0
WireConnection;155;1;156;0
WireConnection;34;0;129;0
WireConnection;151;0;153;0
WireConnection;151;1;150;0
WireConnection;32;0;40;0
WireConnection;119;0;155;0
WireConnection;148;0;151;0
WireConnection;116;0;35;0
WireConnection;116;1;33;0
WireConnection;116;2;120;0
WireConnection;114;0;152;0
WireConnection;114;1;116;0
WireConnection;114;2;115;0
WireConnection;107;1;114;0
ASEEND*/
//CHKSM=38AF94E468DD57771C649CF00DC378144CE13879