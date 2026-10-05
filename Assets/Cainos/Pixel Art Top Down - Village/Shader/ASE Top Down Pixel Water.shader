// Made with Amplify Shader Editor v1.9.9.5
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "Cainos/Pixel Art Top Down - Village/Top Down Pixel Water"
{
	Properties
	{
		[HideInInspector] _EmissionColor("Emission Color", Color) = (1,1,1,1)
		_DeepColor( "Deep Color", Color ) = ( 0, 0, 0, 0 )
		_ShallowColor( "Shallow Color", Color ) = ( 0, 0, 0, 0 )
		_FoamColor( "Foam Color", Color ) = ( 0, 0, 0, 0 )
		_FoamTex( "Foam Tex", 2D ) = "white" {}
		[HideInInspector] _CameraOrthoSize( "Camera Ortho Size", Float ) = 0
		_UnderWaterMaskTex( "Under Water Mask Tex", 2D ) = "white" {}
		_UnderWaterColor( "Under Water Color", Color ) = ( 0, 0, 0, 0 )
		_TerrainBlurredTex( "Terrain Blurred Tex", 2D ) = "white" {}
		_RefractionNoiseScale( "Refraction Noise Scale", Float ) = 0
		_TerrainTex( "Terrain Tex", 2D ) = "white" {}
		_RefractionIntensity( "Refraction Intensity", Float ) = 0
		_FoamScale( "Foam Scale", Float ) = 0
		_FoamNoiseScale( "Foam Noise Scale", Float ) = 0
		_FoamMoveParams( "Foam Move Params", Vector ) = ( 1, 1, 1, 0 )
		_HighlightColor( "Highlight Color", Color ) = ( 0.4576806, 0.5109627, 0.5188679, 1 )

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

			#define ASE_NEEDS_FRAG_WORLD_POSITION
			#define ASE_NEEDS_VERT_NORMAL
			#define ASE_NEEDS_VERT_TANGENT


			sampler2D _TerrainBlurredTex;
			sampler2D _TerrainTex;
			sampler2D _UnderWaterMaskTex;
			sampler2D _FoamTex;
			CBUFFER_START( UnityPerMaterial )
			float4 _HighlightColor;
			float4 _DeepColor;
			float4 _ShallowColor;
			float4 _UnderWaterColor;
			float4 _FoamColor;
			float4 _FoamMoveParams;
			float _RefractionNoiseScale;
			float _RefractionIntensity;
			float _CameraOrthoSize;
			float _FoamNoiseScale;
			float _FoamScale;
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
				float4 ase_texcoord4 : TEXCOORD4;
				float4 ase_texcoord5 : TEXCOORD5;
				float4 ase_texcoord6 : TEXCOORD6;
				float4 ase_texcoord7 : TEXCOORD7;
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
			
			inline float4 ASE_ComputeGrabScreenPos( float4 pos )
			{
				#if UNITY_UV_STARTS_AT_TOP
				float scale = -1.0;
				#else
				float scale = 1.0;
				#endif
				float4 o = pos;
				o.y = pos.w * 0.5f;
				o.y = ( pos.y - o.y ) * _ProjectionParams.x * scale + o.y;
				return o;
			}
			
			inline float noise_randomValue (float2 uv) { return frac(sin(dot(uv, float2(12.9898, 78.233)))*43758.5453); }
			inline float noise_interpolate (float a, float b, float t) { return (1.0-t)*a + (t*b); }
			inline float valueNoise (float2 uv)
			{
				float2 i = floor(uv);
				float2 f = frac( uv );
				f = f* f * (3.0 - 2.0 * f);
				uv = abs( frac(uv) - 0.5);
				float2 c0 = i + float2( 0.0, 0.0 );
				float2 c1 = i + float2( 1.0, 0.0 );
				float2 c2 = i + float2( 0.0, 1.0 );
				float2 c3 = i + float2( 1.0, 1.0 );
				float r0 = noise_randomValue( c0 );
				float r1 = noise_randomValue( c1 );
				float r2 = noise_randomValue( c2 );
				float r3 = noise_randomValue( c3 );
				float bottomOfGrid = noise_interpolate( r0, r1, f.x );
				float topOfGrid = noise_interpolate( r2, r3, f.x );
				float t = noise_interpolate( bottomOfGrid, topOfGrid, f.y );
				return t;
			}
			
			float SimpleNoise(float2 UV)
			{
				float t = 0.0;
				float freq = pow( 2.0, float( 0 ) );
				float amp = pow( 0.5, float( 3 - 0 ) );
				t += valueNoise( UV/freq )*amp;
				freq = pow(2.0, float(1));
				amp = pow(0.5, float(3-1));
				t += valueNoise( UV/freq )*amp;
				freq = pow(2.0, float(2));
				amp = pow(0.5, float(3-2));
				t += valueNoise( UV/freq )*amp;
				return t;
			}
			
			inline float2 UnityVoronoiRandomVector( float2 UV, float offset )
			{
				float2x2 m = float2x2( 15.27, 47.63, 99.41, 89.98 );
				UV = frac( sin(mul(UV, m) ) * 46839.32 );
				return float2( sin(UV.y* +offset ) * 0.5 + 0.5, cos( UV.x* offset ) * 0.5 + 0.5 );
			}
			
			//x - Out y - Cells
			float3 UnityVoronoi( float2 UV, float AngleOffset, float CellDensity, inout float2 mr )
			{
				float2 g = floor( UV * CellDensity );
				float2 f = frac( UV * CellDensity );
				float3 res = float3( 8.0, 0.0, 0.0 );
			
				for( int y = -1; y <= 1; y++ )
				{
					for( int x = -1; x <= 1; x++ )
					{
						float2 lattice = float2( x, y );
						float2 offset = UnityVoronoiRandomVector( lattice + g, AngleOffset );
						float d = distance( lattice + offset, f );
			
						if( d < res.x )
						{
							mr = f - lattice - offset;
							res = float3( d, offset.x, offset.y );
						}
					}
				}
				return res;
			}
			
			float2 ASESafeNormalize(float2 inVec)
			{
				float dp3 = max(1.175494351e-38, dot(inVec, inVec));
				return inVec* rsqrt(dp3);
			}
			

			VertexOutput vert( VertexInput v  )
			{
				VertexOutput o = (VertexOutput)0;

				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_TRANSFER_INSTANCE_ID(v, o);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
				UNITY_SKINNED_VERTEX_COMPUTE(v);

				v.positionOS = UnityFlipSprite( v.positionOS, unity_SpriteProps.xy );

				float4 ase_positionCS = TransformObjectToHClip( ( v.positionOS ).xyz );
				float4 screenPos = ComputeScreenPos( ase_positionCS );
				o.ase_texcoord4 = screenPos;
				float3 ase_normalWS = TransformObjectToWorldNormal( v.normal );
				o.ase_texcoord5.xyz = ase_normalWS;
				float3 ase_tangentWS = TransformObjectToWorldDir( v.tangent.xyz );
				o.ase_texcoord6.xyz = ase_tangentWS;
				float ase_tangentSign = v.tangent.w * ( unity_WorldTransformParams.w >= 0.0 ? 1.0 : -1.0 );
				float3 ase_bitangentWS = cross( ase_normalWS, ase_tangentWS ) * ase_tangentSign;
				o.ase_texcoord7.xyz = ase_bitangentWS;
				
				
				//setting value to unused interpolator channels and avoid initialization warnings
				o.ase_texcoord5.w = 0;
				o.ase_texcoord6.w = 0;
				o.ase_texcoord7.w = 0;
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

				float2 appendResult393 = (float2(positionWS.x , positionWS.y));
				half2 pixelateduv392 = floor( appendResult393 * float2( 32.0, 32.0 ) + float2( 0,0 ) ) / float2( 32.0, 32.0 );
				float mulTime358 = _TimeParameters.x * -0.01;
				float2 temp_output_374_0 = ( float2( 1,20 ) * 0.85 );
				float simplePerlin2D353 = snoise( ( (pixelateduv392*0.5 + mulTime358) * temp_output_374_0 )*2.0 );
				simplePerlin2D353 = simplePerlin2D353*0.5 + 0.5;
				float mulTime362 = _TimeParameters.x * 0.01;
				float simplePerlin2D363 = snoise( ( temp_output_374_0 * (pixelateduv392*0.5 + mulTime362) )*1.0 );
				simplePerlin2D363 = simplePerlin2D363*0.5 + 0.5;
				float saferPower389 = abs( ( simplePerlin2D353 * simplePerlin2D363 ) );
				float temp_output_389_0 = pow( saferPower389 , 6.5 );
				float4 m_HighlightColor281 = ( _HighlightColor * _HighlightColor.a * step( 0.3 , temp_output_389_0 ) * temp_output_389_0 );
				float4 screenPos = IN.ase_texcoord4;
				float4 ase_grabScreenPos = ASE_ComputeGrabScreenPos( screenPos );
				float4 ase_grabScreenPosNorm = ase_grabScreenPos / ase_grabScreenPos.w;
				float3 temp_output_111_0_g1 = ddx( positionWS );
				float3 ase_normalWS = IN.ase_texcoord5.xyz;
				float3 temp_output_113_0_g1 = cross( ddy( positionWS ) , ase_normalWS );
				float dotResult115_g1 = dot( temp_output_111_0_g1 , temp_output_113_0_g1 );
				float2 appendResult154 = (float2(positionWS.x , positionWS.y));
				float simpleNoise138 = SimpleNoise( (appendResult154*1.0 + _TimeParameters.x) );
				float _RefractionNoiseScale151 = _RefractionNoiseScale;
				float2 uv145 = 0;
				float3 unityVoronoy145 = UnityVoronoi(( (appendResult154*float2( 1,2.75 ) + 0.0) + simpleNoise138 ),( _TimeParameters.x * 2.0 ),_RefractionNoiseScale151,uv145);
				float temp_output_20_0_g1 = unityVoronoy145.x;
				float3 normalizeResult130_g1 = normalize( ( ( abs( dotResult115_g1 ) * ase_normalWS ) - ( 0.01 * float3( 0.05,0.05,0.05 ) * sign( dotResult115_g1 ) * ( ( ddx( temp_output_20_0_g1 ) * temp_output_113_0_g1 ) + ( ddy( temp_output_20_0_g1 ) * cross( ase_normalWS , temp_output_111_0_g1 ) ) ) ) ) );
				float3 ase_tangentWS = IN.ase_texcoord6.xyz;
				float3 ase_bitangentWS = IN.ase_texcoord7.xyz;
				float3x3 ase_worldToTangent = float3x3( ase_tangentWS, ase_bitangentWS, ase_normalWS );
				float3 worldToTangentDir42_g1 = mul( ase_worldToTangent, normalizeResult130_g1 );
				float3 m_RefractionOffset162 = ( worldToTangentDir42_g1 * _RefractionIntensity );
				float m_TerrainAlphaBlur10 = tex2D( _TerrainBlurredTex, ( ase_grabScreenPosNorm + float4( m_RefractionOffset162 , 0.0 ) ).xy ).a;
				float4 lerpResult49 = lerp( _DeepColor , _ShallowColor , m_TerrainAlphaBlur10);
				float4 m_WaterColor51 = lerpResult49;
				float4 m_UnderWaterUV166 = ( ase_grabScreenPosNorm + float4( m_RefractionOffset162 , 0.0 ) );
				float4 break171 = m_UnderWaterUV166;
				float _CameraOthoSize86 = _CameraOrthoSize;
				float m_TileSizeYNormalized91 = ( 1.0 / ( _CameraOthoSize86 * 2.0 ) );
				float2 appendResult70 = (float2(break171.x , ( break171.y + ( m_TileSizeYNormalized91 * 0.4 ) )));
				float4 tex2DNode69 = tex2D( _TerrainTex, appendResult70 );
				float4 break168 = m_UnderWaterUV166;
				float2 appendResult97 = (float2(break168.x , ( break168.y + ( m_TileSizeYNormalized91 * 0.8 ) )));
				float4 lerpResult106 = lerp( tex2DNode69 , tex2D( _TerrainTex, appendResult97 ) , ( 1.0 - tex2DNode69.a ));
				float4 lerpResult131 = lerp( lerpResult106 , _UnderWaterColor , _UnderWaterColor.a);
				float4 break75 = lerpResult131;
				float clampResult137 = clamp( ( tex2D( _UnderWaterMaskTex, ase_grabScreenPosNorm.xy ).a * 2.0 ) , 0.0 , 1.0 );
				float saferPower133 = abs( clampResult137 );
				float m_UnderWaterMask130 = pow( saferPower133 , 1.4 );
				float4 appendResult76 = (float4(break75.r , break75.g , break75.b , ( break75.a * m_UnderWaterMask130 )));
				float4 m_UnderWaterColor74 = appendResult76;
				float4 lerpResult81 = lerp( m_WaterColor51 , m_UnderWaterColor74 , m_UnderWaterColor74.w);
				float4 _FoamColor59 = _FoamColor;
				float4 break195 = _FoamColor59;
				float _PixelSizeYNormalized224 = ( m_TileSizeYNormalized91 * 0.03125 );
				float4 appendResult13 = (float4(ase_grabScreenPosNorm.r , ( ase_grabScreenPosNorm.g + _PixelSizeYNormalized224 ) , 0.0 , 0.0));
				float m_ShoreEdgeAlpha175 = tex2D( _TerrainTex, appendResult13.xy ).a;
				float4 break316 = ase_grabScreenPosNorm;
				float4 appendResult317 = (float4(break316.x , ( break316.y + ( m_TileSizeYNormalized91 * 0.5 ) ) , 0.0 , 0.0));
				float saferPower328 = abs( tex2D( _UnderWaterMaskTex, appendResult317.xy ).a );
				float clampResult326 = clamp( ( pow( saferPower328 , 2.5 ) * 1.0 ) , 0.0 , 1.0 );
				float m_FoamMask319 = clampResult326;
				float2 appendResult205 = (float2(positionWS.x , positionWS.y));
				half2 pixelateduv236 = floor( appendResult205 * float2( 32.0, 32.0 ) + float2( 0,0 ) ) / float2( 32.0, 32.0 );
				float4 _FoamMoveParams243 = _FoamMoveParams;
				float4 break246 = _FoamMoveParams243;
				float2 appendResult247 = (float2(break246.x , break246.y));
				float2 normalizeResult248 = ASESafeNormalize( appendResult247 );
				float mulTime240 = _TimeParameters.x * break246.z;
				float2 m_FoamMoveOffset249 = ( normalizeResult248 * mulTime240 );
				float simpleNoise203 = SimpleNoise( (pixelateduv236*1.0 + _TimeParameters.x)*_FoamNoiseScale );
				float2 m_FoamUV207 = ( ( (pixelateduv236*float2( 1,2 ) + m_FoamMoveOffset249) + simpleNoise203 ) * ( _FoamScale * 0.1 ) );
				float temp_output_191_0 = ( m_FoamMask319 * tex2D( _FoamTex, m_FoamUV207 ).a );
				float m_FoamAlpha177 = ( step( 0.2 , temp_output_191_0 ) * temp_output_191_0 );
				float clampResult180 = clamp( ( m_ShoreEdgeAlpha175 + m_FoamAlpha177 ) , 0.0 , 1.0 );
				float4 appendResult194 = (float4(break195.r , break195.g , break195.b , ( break195.a * clampResult180 )));
				float4 m_FoamColor62 = appendResult194;
				float4 lerpResult64 = lerp( lerpResult81 , m_FoamColor62 , m_FoamColor62.w);
				float4 break338 = ( m_HighlightColor281 + lerpResult64 );
				float4 appendResult339 = (float4(break338.r , break338.g , break338.b , 1.0));
				
				float4 Color = appendResult339;
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

			#define ASE_NEEDS_WORLD_NORMAL
			#define ASE_NEEDS_FRAG_WORLD_NORMAL
			#define ASE_NEEDS_WORLD_TANGENT
			#define ASE_NEEDS_FRAG_WORLD_TANGENT
			#define ASE_NEEDS_WORLD_BITANGENT
			#define ASE_NEEDS_FRAG_WORLD_BITANGENT


			sampler2D _TerrainBlurredTex;
			sampler2D _TerrainTex;
			sampler2D _UnderWaterMaskTex;
			sampler2D _FoamTex;
			CBUFFER_START( UnityPerMaterial )
			float4 _HighlightColor;
			float4 _DeepColor;
			float4 _ShallowColor;
			float4 _UnderWaterColor;
			float4 _FoamColor;
			float4 _FoamMoveParams;
			float _RefractionNoiseScale;
			float _RefractionIntensity;
			float _CameraOrthoSize;
			float _FoamNoiseScale;
			float _FoamScale;
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
				float4 ase_texcoord6 : TEXCOORD6;
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
			
			inline float4 ASE_ComputeGrabScreenPos( float4 pos )
			{
				#if UNITY_UV_STARTS_AT_TOP
				float scale = -1.0;
				#else
				float scale = 1.0;
				#endif
				float4 o = pos;
				o.y = pos.w * 0.5f;
				o.y = ( pos.y - o.y ) * _ProjectionParams.x * scale + o.y;
				return o;
			}
			
			inline float noise_randomValue (float2 uv) { return frac(sin(dot(uv, float2(12.9898, 78.233)))*43758.5453); }
			inline float noise_interpolate (float a, float b, float t) { return (1.0-t)*a + (t*b); }
			inline float valueNoise (float2 uv)
			{
				float2 i = floor(uv);
				float2 f = frac( uv );
				f = f* f * (3.0 - 2.0 * f);
				uv = abs( frac(uv) - 0.5);
				float2 c0 = i + float2( 0.0, 0.0 );
				float2 c1 = i + float2( 1.0, 0.0 );
				float2 c2 = i + float2( 0.0, 1.0 );
				float2 c3 = i + float2( 1.0, 1.0 );
				float r0 = noise_randomValue( c0 );
				float r1 = noise_randomValue( c1 );
				float r2 = noise_randomValue( c2 );
				float r3 = noise_randomValue( c3 );
				float bottomOfGrid = noise_interpolate( r0, r1, f.x );
				float topOfGrid = noise_interpolate( r2, r3, f.x );
				float t = noise_interpolate( bottomOfGrid, topOfGrid, f.y );
				return t;
			}
			
			float SimpleNoise(float2 UV)
			{
				float t = 0.0;
				float freq = pow( 2.0, float( 0 ) );
				float amp = pow( 0.5, float( 3 - 0 ) );
				t += valueNoise( UV/freq )*amp;
				freq = pow(2.0, float(1));
				amp = pow(0.5, float(3-1));
				t += valueNoise( UV/freq )*amp;
				freq = pow(2.0, float(2));
				amp = pow(0.5, float(3-2));
				t += valueNoise( UV/freq )*amp;
				return t;
			}
			
			inline float2 UnityVoronoiRandomVector( float2 UV, float offset )
			{
				float2x2 m = float2x2( 15.27, 47.63, 99.41, 89.98 );
				UV = frac( sin(mul(UV, m) ) * 46839.32 );
				return float2( sin(UV.y* +offset ) * 0.5 + 0.5, cos( UV.x* offset ) * 0.5 + 0.5 );
			}
			
			//x - Out y - Cells
			float3 UnityVoronoi( float2 UV, float AngleOffset, float CellDensity, inout float2 mr )
			{
				float2 g = floor( UV * CellDensity );
				float2 f = frac( UV * CellDensity );
				float3 res = float3( 8.0, 0.0, 0.0 );
			
				for( int y = -1; y <= 1; y++ )
				{
					for( int x = -1; x <= 1; x++ )
					{
						float2 lattice = float2( x, y );
						float2 offset = UnityVoronoiRandomVector( lattice + g, AngleOffset );
						float d = distance( lattice + offset, f );
			
						if( d < res.x )
						{
							mr = f - lattice - offset;
							res = float3( d, offset.x, offset.y );
						}
					}
				}
				return res;
			}
			
			float2 ASESafeNormalize(float2 inVec)
			{
				float dp3 = max(1.175494351e-38, dot(inVec, inVec));
				return inVec* rsqrt(dp3);
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
				float4 ase_positionCS = TransformObjectToHClip( ( v.positionOS ).xyz );
				float4 screenPos = ComputeScreenPos( ase_positionCS );
				o.ase_texcoord6 = screenPos;
				
				
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

				float3 ase_positionWS = IN.ase_texcoord5.xyz;
				float2 appendResult393 = (float2(ase_positionWS.x , ase_positionWS.y));
				half2 pixelateduv392 = floor( appendResult393 * float2( 32.0, 32.0 ) + float2( 0,0 ) ) / float2( 32.0, 32.0 );
				float mulTime358 = _TimeParameters.x * -0.01;
				float2 temp_output_374_0 = ( float2( 1,20 ) * 0.85 );
				float simplePerlin2D353 = snoise( ( (pixelateduv392*0.5 + mulTime358) * temp_output_374_0 )*2.0 );
				simplePerlin2D353 = simplePerlin2D353*0.5 + 0.5;
				float mulTime362 = _TimeParameters.x * 0.01;
				float simplePerlin2D363 = snoise( ( temp_output_374_0 * (pixelateduv392*0.5 + mulTime362) )*1.0 );
				simplePerlin2D363 = simplePerlin2D363*0.5 + 0.5;
				float saferPower389 = abs( ( simplePerlin2D353 * simplePerlin2D363 ) );
				float temp_output_389_0 = pow( saferPower389 , 6.5 );
				float4 m_HighlightColor281 = ( _HighlightColor * _HighlightColor.a * step( 0.3 , temp_output_389_0 ) * temp_output_389_0 );
				float4 screenPos = IN.ase_texcoord6;
				float4 ase_grabScreenPos = ASE_ComputeGrabScreenPos( screenPos );
				float4 ase_grabScreenPosNorm = ase_grabScreenPos / ase_grabScreenPos.w;
				float3 temp_output_111_0_g1 = ddx( ase_positionWS );
				float3 temp_output_113_0_g1 = cross( ddy( ase_positionWS ) , IN.normalWS );
				float dotResult115_g1 = dot( temp_output_111_0_g1 , temp_output_113_0_g1 );
				float2 appendResult154 = (float2(ase_positionWS.x , ase_positionWS.y));
				float simpleNoise138 = SimpleNoise( (appendResult154*1.0 + _TimeParameters.x) );
				float _RefractionNoiseScale151 = _RefractionNoiseScale;
				float2 uv145 = 0;
				float3 unityVoronoy145 = UnityVoronoi(( (appendResult154*float2( 1,2.75 ) + 0.0) + simpleNoise138 ),( _TimeParameters.x * 2.0 ),_RefractionNoiseScale151,uv145);
				float temp_output_20_0_g1 = unityVoronoy145.x;
				float3 normalizeResult130_g1 = normalize( ( ( abs( dotResult115_g1 ) * IN.normalWS ) - ( 0.01 * float3( 0.05,0.05,0.05 ) * sign( dotResult115_g1 ) * ( ( ddx( temp_output_20_0_g1 ) * temp_output_113_0_g1 ) + ( ddy( temp_output_20_0_g1 ) * cross( IN.normalWS , temp_output_111_0_g1 ) ) ) ) ) );
				float3x3 ase_worldToTangent = float3x3( IN.tangentWS.xyz, IN.bitangentWS, IN.normalWS );
				float3 worldToTangentDir42_g1 = mul( ase_worldToTangent, normalizeResult130_g1 );
				float3 m_RefractionOffset162 = ( worldToTangentDir42_g1 * _RefractionIntensity );
				float m_TerrainAlphaBlur10 = tex2D( _TerrainBlurredTex, ( ase_grabScreenPosNorm + float4( m_RefractionOffset162 , 0.0 ) ).xy ).a;
				float4 lerpResult49 = lerp( _DeepColor , _ShallowColor , m_TerrainAlphaBlur10);
				float4 m_WaterColor51 = lerpResult49;
				float4 m_UnderWaterUV166 = ( ase_grabScreenPosNorm + float4( m_RefractionOffset162 , 0.0 ) );
				float4 break171 = m_UnderWaterUV166;
				float _CameraOthoSize86 = _CameraOrthoSize;
				float m_TileSizeYNormalized91 = ( 1.0 / ( _CameraOthoSize86 * 2.0 ) );
				float2 appendResult70 = (float2(break171.x , ( break171.y + ( m_TileSizeYNormalized91 * 0.4 ) )));
				float4 tex2DNode69 = tex2D( _TerrainTex, appendResult70 );
				float4 break168 = m_UnderWaterUV166;
				float2 appendResult97 = (float2(break168.x , ( break168.y + ( m_TileSizeYNormalized91 * 0.8 ) )));
				float4 lerpResult106 = lerp( tex2DNode69 , tex2D( _TerrainTex, appendResult97 ) , ( 1.0 - tex2DNode69.a ));
				float4 lerpResult131 = lerp( lerpResult106 , _UnderWaterColor , _UnderWaterColor.a);
				float4 break75 = lerpResult131;
				float clampResult137 = clamp( ( tex2D( _UnderWaterMaskTex, ase_grabScreenPosNorm.xy ).a * 2.0 ) , 0.0 , 1.0 );
				float saferPower133 = abs( clampResult137 );
				float m_UnderWaterMask130 = pow( saferPower133 , 1.4 );
				float4 appendResult76 = (float4(break75.r , break75.g , break75.b , ( break75.a * m_UnderWaterMask130 )));
				float4 m_UnderWaterColor74 = appendResult76;
				float4 lerpResult81 = lerp( m_WaterColor51 , m_UnderWaterColor74 , m_UnderWaterColor74.w);
				float4 _FoamColor59 = _FoamColor;
				float4 break195 = _FoamColor59;
				float _PixelSizeYNormalized224 = ( m_TileSizeYNormalized91 * 0.03125 );
				float4 appendResult13 = (float4(ase_grabScreenPosNorm.r , ( ase_grabScreenPosNorm.g + _PixelSizeYNormalized224 ) , 0.0 , 0.0));
				float m_ShoreEdgeAlpha175 = tex2D( _TerrainTex, appendResult13.xy ).a;
				float4 break316 = ase_grabScreenPosNorm;
				float4 appendResult317 = (float4(break316.x , ( break316.y + ( m_TileSizeYNormalized91 * 0.5 ) ) , 0.0 , 0.0));
				float saferPower328 = abs( tex2D( _UnderWaterMaskTex, appendResult317.xy ).a );
				float clampResult326 = clamp( ( pow( saferPower328 , 2.5 ) * 1.0 ) , 0.0 , 1.0 );
				float m_FoamMask319 = clampResult326;
				float2 appendResult205 = (float2(ase_positionWS.x , ase_positionWS.y));
				half2 pixelateduv236 = floor( appendResult205 * float2( 32.0, 32.0 ) + float2( 0,0 ) ) / float2( 32.0, 32.0 );
				float4 _FoamMoveParams243 = _FoamMoveParams;
				float4 break246 = _FoamMoveParams243;
				float2 appendResult247 = (float2(break246.x , break246.y));
				float2 normalizeResult248 = ASESafeNormalize( appendResult247 );
				float mulTime240 = _TimeParameters.x * break246.z;
				float2 m_FoamMoveOffset249 = ( normalizeResult248 * mulTime240 );
				float simpleNoise203 = SimpleNoise( (pixelateduv236*1.0 + _TimeParameters.x)*_FoamNoiseScale );
				float2 m_FoamUV207 = ( ( (pixelateduv236*float2( 1,2 ) + m_FoamMoveOffset249) + simpleNoise203 ) * ( _FoamScale * 0.1 ) );
				float temp_output_191_0 = ( m_FoamMask319 * tex2D( _FoamTex, m_FoamUV207 ).a );
				float m_FoamAlpha177 = ( step( 0.2 , temp_output_191_0 ) * temp_output_191_0 );
				float clampResult180 = clamp( ( m_ShoreEdgeAlpha175 + m_FoamAlpha177 ) , 0.0 , 1.0 );
				float4 appendResult194 = (float4(break195.r , break195.g , break195.b , ( break195.a * clampResult180 )));
				float4 m_FoamColor62 = appendResult194;
				float4 lerpResult64 = lerp( lerpResult81 , m_FoamColor62 , m_FoamColor62.w);
				float4 break338 = ( m_HighlightColor281 + lerpResult64 );
				float4 appendResult339 = (float4(break338.r , break338.g , break338.b , 1.0));
				
				float4 Color = appendResult339;
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

			#define ASE_NEEDS_FRAG_WORLD_POSITION
			#define ASE_NEEDS_VERT_NORMAL
			#define ASE_NEEDS_VERT_TANGENT


			sampler2D _TerrainBlurredTex;
			sampler2D _TerrainTex;
			sampler2D _UnderWaterMaskTex;
			sampler2D _FoamTex;
			CBUFFER_START( UnityPerMaterial )
			float4 _HighlightColor;
			float4 _DeepColor;
			float4 _ShallowColor;
			float4 _UnderWaterColor;
			float4 _FoamColor;
			float4 _FoamMoveParams;
			float _RefractionNoiseScale;
			float _RefractionIntensity;
			float _CameraOrthoSize;
			float _FoamNoiseScale;
			float _FoamScale;
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
				float4 ase_texcoord3 : TEXCOORD3;
				float4 ase_texcoord4 : TEXCOORD4;
				float4 ase_texcoord5 : TEXCOORD5;
				float4 ase_texcoord6 : TEXCOORD6;
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
			
			inline float4 ASE_ComputeGrabScreenPos( float4 pos )
			{
				#if UNITY_UV_STARTS_AT_TOP
				float scale = -1.0;
				#else
				float scale = 1.0;
				#endif
				float4 o = pos;
				o.y = pos.w * 0.5f;
				o.y = ( pos.y - o.y ) * _ProjectionParams.x * scale + o.y;
				return o;
			}
			
			inline float noise_randomValue (float2 uv) { return frac(sin(dot(uv, float2(12.9898, 78.233)))*43758.5453); }
			inline float noise_interpolate (float a, float b, float t) { return (1.0-t)*a + (t*b); }
			inline float valueNoise (float2 uv)
			{
				float2 i = floor(uv);
				float2 f = frac( uv );
				f = f* f * (3.0 - 2.0 * f);
				uv = abs( frac(uv) - 0.5);
				float2 c0 = i + float2( 0.0, 0.0 );
				float2 c1 = i + float2( 1.0, 0.0 );
				float2 c2 = i + float2( 0.0, 1.0 );
				float2 c3 = i + float2( 1.0, 1.0 );
				float r0 = noise_randomValue( c0 );
				float r1 = noise_randomValue( c1 );
				float r2 = noise_randomValue( c2 );
				float r3 = noise_randomValue( c3 );
				float bottomOfGrid = noise_interpolate( r0, r1, f.x );
				float topOfGrid = noise_interpolate( r2, r3, f.x );
				float t = noise_interpolate( bottomOfGrid, topOfGrid, f.y );
				return t;
			}
			
			float SimpleNoise(float2 UV)
			{
				float t = 0.0;
				float freq = pow( 2.0, float( 0 ) );
				float amp = pow( 0.5, float( 3 - 0 ) );
				t += valueNoise( UV/freq )*amp;
				freq = pow(2.0, float(1));
				amp = pow(0.5, float(3-1));
				t += valueNoise( UV/freq )*amp;
				freq = pow(2.0, float(2));
				amp = pow(0.5, float(3-2));
				t += valueNoise( UV/freq )*amp;
				return t;
			}
			
			inline float2 UnityVoronoiRandomVector( float2 UV, float offset )
			{
				float2x2 m = float2x2( 15.27, 47.63, 99.41, 89.98 );
				UV = frac( sin(mul(UV, m) ) * 46839.32 );
				return float2( sin(UV.y* +offset ) * 0.5 + 0.5, cos( UV.x* offset ) * 0.5 + 0.5 );
			}
			
			//x - Out y - Cells
			float3 UnityVoronoi( float2 UV, float AngleOffset, float CellDensity, inout float2 mr )
			{
				float2 g = floor( UV * CellDensity );
				float2 f = frac( UV * CellDensity );
				float3 res = float3( 8.0, 0.0, 0.0 );
			
				for( int y = -1; y <= 1; y++ )
				{
					for( int x = -1; x <= 1; x++ )
					{
						float2 lattice = float2( x, y );
						float2 offset = UnityVoronoiRandomVector( lattice + g, AngleOffset );
						float d = distance( lattice + offset, f );
			
						if( d < res.x )
						{
							mr = f - lattice - offset;
							res = float3( d, offset.x, offset.y );
						}
					}
				}
				return res;
			}
			
			float2 ASESafeNormalize(float2 inVec)
			{
				float dp3 = max(1.175494351e-38, dot(inVec, inVec));
				return inVec* rsqrt(dp3);
			}
			

			VertexOutput vert( VertexInput v  )
			{
				VertexOutput o = (VertexOutput)0;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_TRANSFER_INSTANCE_ID(v, o);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
				UNITY_SKINNED_VERTEX_COMPUTE( v );

				v.positionOS = UnityFlipSprite( v.positionOS, unity_SpriteProps.xy );

				float4 ase_positionCS = TransformObjectToHClip( ( v.positionOS ).xyz );
				float4 screenPos = ComputeScreenPos( ase_positionCS );
				o.ase_texcoord3 = screenPos;
				float3 ase_normalWS = TransformObjectToWorldNormal( v.normal );
				o.ase_texcoord4.xyz = ase_normalWS;
				float3 ase_tangentWS = TransformObjectToWorldDir( v.tangent.xyz );
				o.ase_texcoord5.xyz = ase_tangentWS;
				float ase_tangentSign = v.tangent.w * ( unity_WorldTransformParams.w >= 0.0 ? 1.0 : -1.0 );
				float3 ase_bitangentWS = cross( ase_normalWS, ase_tangentWS ) * ase_tangentSign;
				o.ase_texcoord6.xyz = ase_bitangentWS;
				
				
				//setting value to unused interpolator channels and avoid initialization warnings
				o.ase_texcoord4.w = 0;
				o.ase_texcoord5.w = 0;
				o.ase_texcoord6.w = 0;
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

				float2 appendResult393 = (float2(positionWS.x , positionWS.y));
				half2 pixelateduv392 = floor( appendResult393 * float2( 32.0, 32.0 ) + float2( 0,0 ) ) / float2( 32.0, 32.0 );
				float mulTime358 = _TimeParameters.x * -0.01;
				float2 temp_output_374_0 = ( float2( 1,20 ) * 0.85 );
				float simplePerlin2D353 = snoise( ( (pixelateduv392*0.5 + mulTime358) * temp_output_374_0 )*2.0 );
				simplePerlin2D353 = simplePerlin2D353*0.5 + 0.5;
				float mulTime362 = _TimeParameters.x * 0.01;
				float simplePerlin2D363 = snoise( ( temp_output_374_0 * (pixelateduv392*0.5 + mulTime362) )*1.0 );
				simplePerlin2D363 = simplePerlin2D363*0.5 + 0.5;
				float saferPower389 = abs( ( simplePerlin2D353 * simplePerlin2D363 ) );
				float temp_output_389_0 = pow( saferPower389 , 6.5 );
				float4 m_HighlightColor281 = ( _HighlightColor * _HighlightColor.a * step( 0.3 , temp_output_389_0 ) * temp_output_389_0 );
				float4 screenPos = IN.ase_texcoord3;
				float4 ase_grabScreenPos = ASE_ComputeGrabScreenPos( screenPos );
				float4 ase_grabScreenPosNorm = ase_grabScreenPos / ase_grabScreenPos.w;
				float3 temp_output_111_0_g1 = ddx( positionWS );
				float3 ase_normalWS = IN.ase_texcoord4.xyz;
				float3 temp_output_113_0_g1 = cross( ddy( positionWS ) , ase_normalWS );
				float dotResult115_g1 = dot( temp_output_111_0_g1 , temp_output_113_0_g1 );
				float2 appendResult154 = (float2(positionWS.x , positionWS.y));
				float simpleNoise138 = SimpleNoise( (appendResult154*1.0 + _TimeParameters.x) );
				float _RefractionNoiseScale151 = _RefractionNoiseScale;
				float2 uv145 = 0;
				float3 unityVoronoy145 = UnityVoronoi(( (appendResult154*float2( 1,2.75 ) + 0.0) + simpleNoise138 ),( _TimeParameters.x * 2.0 ),_RefractionNoiseScale151,uv145);
				float temp_output_20_0_g1 = unityVoronoy145.x;
				float3 normalizeResult130_g1 = normalize( ( ( abs( dotResult115_g1 ) * ase_normalWS ) - ( 0.01 * float3( 0.05,0.05,0.05 ) * sign( dotResult115_g1 ) * ( ( ddx( temp_output_20_0_g1 ) * temp_output_113_0_g1 ) + ( ddy( temp_output_20_0_g1 ) * cross( ase_normalWS , temp_output_111_0_g1 ) ) ) ) ) );
				float3 ase_tangentWS = IN.ase_texcoord5.xyz;
				float3 ase_bitangentWS = IN.ase_texcoord6.xyz;
				float3x3 ase_worldToTangent = float3x3( ase_tangentWS, ase_bitangentWS, ase_normalWS );
				float3 worldToTangentDir42_g1 = mul( ase_worldToTangent, normalizeResult130_g1 );
				float3 m_RefractionOffset162 = ( worldToTangentDir42_g1 * _RefractionIntensity );
				float m_TerrainAlphaBlur10 = tex2D( _TerrainBlurredTex, ( ase_grabScreenPosNorm + float4( m_RefractionOffset162 , 0.0 ) ).xy ).a;
				float4 lerpResult49 = lerp( _DeepColor , _ShallowColor , m_TerrainAlphaBlur10);
				float4 m_WaterColor51 = lerpResult49;
				float4 m_UnderWaterUV166 = ( ase_grabScreenPosNorm + float4( m_RefractionOffset162 , 0.0 ) );
				float4 break171 = m_UnderWaterUV166;
				float _CameraOthoSize86 = _CameraOrthoSize;
				float m_TileSizeYNormalized91 = ( 1.0 / ( _CameraOthoSize86 * 2.0 ) );
				float2 appendResult70 = (float2(break171.x , ( break171.y + ( m_TileSizeYNormalized91 * 0.4 ) )));
				float4 tex2DNode69 = tex2D( _TerrainTex, appendResult70 );
				float4 break168 = m_UnderWaterUV166;
				float2 appendResult97 = (float2(break168.x , ( break168.y + ( m_TileSizeYNormalized91 * 0.8 ) )));
				float4 lerpResult106 = lerp( tex2DNode69 , tex2D( _TerrainTex, appendResult97 ) , ( 1.0 - tex2DNode69.a ));
				float4 lerpResult131 = lerp( lerpResult106 , _UnderWaterColor , _UnderWaterColor.a);
				float4 break75 = lerpResult131;
				float clampResult137 = clamp( ( tex2D( _UnderWaterMaskTex, ase_grabScreenPosNorm.xy ).a * 2.0 ) , 0.0 , 1.0 );
				float saferPower133 = abs( clampResult137 );
				float m_UnderWaterMask130 = pow( saferPower133 , 1.4 );
				float4 appendResult76 = (float4(break75.r , break75.g , break75.b , ( break75.a * m_UnderWaterMask130 )));
				float4 m_UnderWaterColor74 = appendResult76;
				float4 lerpResult81 = lerp( m_WaterColor51 , m_UnderWaterColor74 , m_UnderWaterColor74.w);
				float4 _FoamColor59 = _FoamColor;
				float4 break195 = _FoamColor59;
				float _PixelSizeYNormalized224 = ( m_TileSizeYNormalized91 * 0.03125 );
				float4 appendResult13 = (float4(ase_grabScreenPosNorm.r , ( ase_grabScreenPosNorm.g + _PixelSizeYNormalized224 ) , 0.0 , 0.0));
				float m_ShoreEdgeAlpha175 = tex2D( _TerrainTex, appendResult13.xy ).a;
				float4 break316 = ase_grabScreenPosNorm;
				float4 appendResult317 = (float4(break316.x , ( break316.y + ( m_TileSizeYNormalized91 * 0.5 ) ) , 0.0 , 0.0));
				float saferPower328 = abs( tex2D( _UnderWaterMaskTex, appendResult317.xy ).a );
				float clampResult326 = clamp( ( pow( saferPower328 , 2.5 ) * 1.0 ) , 0.0 , 1.0 );
				float m_FoamMask319 = clampResult326;
				float2 appendResult205 = (float2(positionWS.x , positionWS.y));
				half2 pixelateduv236 = floor( appendResult205 * float2( 32.0, 32.0 ) + float2( 0,0 ) ) / float2( 32.0, 32.0 );
				float4 _FoamMoveParams243 = _FoamMoveParams;
				float4 break246 = _FoamMoveParams243;
				float2 appendResult247 = (float2(break246.x , break246.y));
				float2 normalizeResult248 = ASESafeNormalize( appendResult247 );
				float mulTime240 = _TimeParameters.x * break246.z;
				float2 m_FoamMoveOffset249 = ( normalizeResult248 * mulTime240 );
				float simpleNoise203 = SimpleNoise( (pixelateduv236*1.0 + _TimeParameters.x)*_FoamNoiseScale );
				float2 m_FoamUV207 = ( ( (pixelateduv236*float2( 1,2 ) + m_FoamMoveOffset249) + simpleNoise203 ) * ( _FoamScale * 0.1 ) );
				float temp_output_191_0 = ( m_FoamMask319 * tex2D( _FoamTex, m_FoamUV207 ).a );
				float m_FoamAlpha177 = ( step( 0.2 , temp_output_191_0 ) * temp_output_191_0 );
				float clampResult180 = clamp( ( m_ShoreEdgeAlpha175 + m_FoamAlpha177 ) , 0.0 , 1.0 );
				float4 appendResult194 = (float4(break195.r , break195.g , break195.b , ( break195.a * clampResult180 )));
				float4 m_FoamColor62 = appendResult194;
				float4 lerpResult64 = lerp( lerpResult81 , m_FoamColor62 , m_FoamColor62.w);
				float4 break338 = ( m_HighlightColor281 + lerpResult64 );
				float4 appendResult339 = (float4(break338.r , break338.g , break338.b , 1.0));
				
				float4 Color = appendResult339;

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

			#define ASE_NEEDS_VERT_NORMAL
			#define ASE_NEEDS_VERT_TANGENT


			sampler2D _TerrainBlurredTex;
			sampler2D _TerrainTex;
			sampler2D _UnderWaterMaskTex;
			sampler2D _FoamTex;
			CBUFFER_START( UnityPerMaterial )
			float4 _HighlightColor;
			float4 _DeepColor;
			float4 _ShallowColor;
			float4 _UnderWaterColor;
			float4 _FoamColor;
			float4 _FoamMoveParams;
			float _RefractionNoiseScale;
			float _RefractionIntensity;
			float _CameraOrthoSize;
			float _FoamNoiseScale;
			float _FoamScale;
			CBUFFER_END


            struct VertexInput
			{
				float3 positionOS : POSITION;
				float3 normal : NORMAL;
				float4 tangent : TANGENT;
				
				UNITY_SKINNED_VERTEX_INPUTS
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct VertexOutput
			{
				float4 positionCS : SV_POSITION;
				float4 ase_texcoord : TEXCOORD0;
				float4 ase_texcoord1 : TEXCOORD1;
				float4 ase_texcoord2 : TEXCOORD2;
				float4 ase_texcoord3 : TEXCOORD3;
				float4 ase_texcoord4 : TEXCOORD4;
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
			
			inline float4 ASE_ComputeGrabScreenPos( float4 pos )
			{
				#if UNITY_UV_STARTS_AT_TOP
				float scale = -1.0;
				#else
				float scale = 1.0;
				#endif
				float4 o = pos;
				o.y = pos.w * 0.5f;
				o.y = ( pos.y - o.y ) * _ProjectionParams.x * scale + o.y;
				return o;
			}
			
			inline float noise_randomValue (float2 uv) { return frac(sin(dot(uv, float2(12.9898, 78.233)))*43758.5453); }
			inline float noise_interpolate (float a, float b, float t) { return (1.0-t)*a + (t*b); }
			inline float valueNoise (float2 uv)
			{
				float2 i = floor(uv);
				float2 f = frac( uv );
				f = f* f * (3.0 - 2.0 * f);
				uv = abs( frac(uv) - 0.5);
				float2 c0 = i + float2( 0.0, 0.0 );
				float2 c1 = i + float2( 1.0, 0.0 );
				float2 c2 = i + float2( 0.0, 1.0 );
				float2 c3 = i + float2( 1.0, 1.0 );
				float r0 = noise_randomValue( c0 );
				float r1 = noise_randomValue( c1 );
				float r2 = noise_randomValue( c2 );
				float r3 = noise_randomValue( c3 );
				float bottomOfGrid = noise_interpolate( r0, r1, f.x );
				float topOfGrid = noise_interpolate( r2, r3, f.x );
				float t = noise_interpolate( bottomOfGrid, topOfGrid, f.y );
				return t;
			}
			
			float SimpleNoise(float2 UV)
			{
				float t = 0.0;
				float freq = pow( 2.0, float( 0 ) );
				float amp = pow( 0.5, float( 3 - 0 ) );
				t += valueNoise( UV/freq )*amp;
				freq = pow(2.0, float(1));
				amp = pow(0.5, float(3-1));
				t += valueNoise( UV/freq )*amp;
				freq = pow(2.0, float(2));
				amp = pow(0.5, float(3-2));
				t += valueNoise( UV/freq )*amp;
				return t;
			}
			
			inline float2 UnityVoronoiRandomVector( float2 UV, float offset )
			{
				float2x2 m = float2x2( 15.27, 47.63, 99.41, 89.98 );
				UV = frac( sin(mul(UV, m) ) * 46839.32 );
				return float2( sin(UV.y* +offset ) * 0.5 + 0.5, cos( UV.x* offset ) * 0.5 + 0.5 );
			}
			
			//x - Out y - Cells
			float3 UnityVoronoi( float2 UV, float AngleOffset, float CellDensity, inout float2 mr )
			{
				float2 g = floor( UV * CellDensity );
				float2 f = frac( UV * CellDensity );
				float3 res = float3( 8.0, 0.0, 0.0 );
			
				for( int y = -1; y <= 1; y++ )
				{
					for( int x = -1; x <= 1; x++ )
					{
						float2 lattice = float2( x, y );
						float2 offset = UnityVoronoiRandomVector( lattice + g, AngleOffset );
						float d = distance( lattice + offset, f );
			
						if( d < res.x )
						{
							mr = f - lattice - offset;
							res = float3( d, offset.x, offset.y );
						}
					}
				}
				return res;
			}
			
			float2 ASESafeNormalize(float2 inVec)
			{
				float dp3 = max(1.175494351e-38, dot(inVec, inVec));
				return inVec* rsqrt(dp3);
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
				o.ase_texcoord.xyz = ase_positionWS;
				float4 ase_positionCS = TransformObjectToHClip( ( v.positionOS ).xyz );
				float4 screenPos = ComputeScreenPos( ase_positionCS );
				o.ase_texcoord1 = screenPos;
				float3 ase_normalWS = TransformObjectToWorldNormal( v.normal );
				o.ase_texcoord2.xyz = ase_normalWS;
				float3 ase_tangentWS = TransformObjectToWorldDir( v.tangent.xyz );
				o.ase_texcoord3.xyz = ase_tangentWS;
				float ase_tangentSign = v.tangent.w * ( unity_WorldTransformParams.w >= 0.0 ? 1.0 : -1.0 );
				float3 ase_bitangentWS = cross( ase_normalWS, ase_tangentWS ) * ase_tangentSign;
				o.ase_texcoord4.xyz = ase_bitangentWS;
				
				
				//setting value to unused interpolator channels and avoid initialization warnings
				o.ase_texcoord.w = 0;
				o.ase_texcoord2.w = 0;
				o.ase_texcoord3.w = 0;
				o.ase_texcoord4.w = 0;
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
				float3 ase_positionWS = IN.ase_texcoord.xyz;
				float2 appendResult393 = (float2(ase_positionWS.x , ase_positionWS.y));
				half2 pixelateduv392 = floor( appendResult393 * float2( 32.0, 32.0 ) + float2( 0,0 ) ) / float2( 32.0, 32.0 );
				float mulTime358 = _TimeParameters.x * -0.01;
				float2 temp_output_374_0 = ( float2( 1,20 ) * 0.85 );
				float simplePerlin2D353 = snoise( ( (pixelateduv392*0.5 + mulTime358) * temp_output_374_0 )*2.0 );
				simplePerlin2D353 = simplePerlin2D353*0.5 + 0.5;
				float mulTime362 = _TimeParameters.x * 0.01;
				float simplePerlin2D363 = snoise( ( temp_output_374_0 * (pixelateduv392*0.5 + mulTime362) )*1.0 );
				simplePerlin2D363 = simplePerlin2D363*0.5 + 0.5;
				float saferPower389 = abs( ( simplePerlin2D353 * simplePerlin2D363 ) );
				float temp_output_389_0 = pow( saferPower389 , 6.5 );
				float4 m_HighlightColor281 = ( _HighlightColor * _HighlightColor.a * step( 0.3 , temp_output_389_0 ) * temp_output_389_0 );
				float4 screenPos = IN.ase_texcoord1;
				float4 ase_grabScreenPos = ASE_ComputeGrabScreenPos( screenPos );
				float4 ase_grabScreenPosNorm = ase_grabScreenPos / ase_grabScreenPos.w;
				float3 temp_output_111_0_g1 = ddx( ase_positionWS );
				float3 ase_normalWS = IN.ase_texcoord2.xyz;
				float3 temp_output_113_0_g1 = cross( ddy( ase_positionWS ) , ase_normalWS );
				float dotResult115_g1 = dot( temp_output_111_0_g1 , temp_output_113_0_g1 );
				float2 appendResult154 = (float2(ase_positionWS.x , ase_positionWS.y));
				float simpleNoise138 = SimpleNoise( (appendResult154*1.0 + _TimeParameters.x) );
				float _RefractionNoiseScale151 = _RefractionNoiseScale;
				float2 uv145 = 0;
				float3 unityVoronoy145 = UnityVoronoi(( (appendResult154*float2( 1,2.75 ) + 0.0) + simpleNoise138 ),( _TimeParameters.x * 2.0 ),_RefractionNoiseScale151,uv145);
				float temp_output_20_0_g1 = unityVoronoy145.x;
				float3 normalizeResult130_g1 = normalize( ( ( abs( dotResult115_g1 ) * ase_normalWS ) - ( 0.01 * float3( 0.05,0.05,0.05 ) * sign( dotResult115_g1 ) * ( ( ddx( temp_output_20_0_g1 ) * temp_output_113_0_g1 ) + ( ddy( temp_output_20_0_g1 ) * cross( ase_normalWS , temp_output_111_0_g1 ) ) ) ) ) );
				float3 ase_tangentWS = IN.ase_texcoord3.xyz;
				float3 ase_bitangentWS = IN.ase_texcoord4.xyz;
				float3x3 ase_worldToTangent = float3x3( ase_tangentWS, ase_bitangentWS, ase_normalWS );
				float3 worldToTangentDir42_g1 = mul( ase_worldToTangent, normalizeResult130_g1 );
				float3 m_RefractionOffset162 = ( worldToTangentDir42_g1 * _RefractionIntensity );
				float m_TerrainAlphaBlur10 = tex2D( _TerrainBlurredTex, ( ase_grabScreenPosNorm + float4( m_RefractionOffset162 , 0.0 ) ).xy ).a;
				float4 lerpResult49 = lerp( _DeepColor , _ShallowColor , m_TerrainAlphaBlur10);
				float4 m_WaterColor51 = lerpResult49;
				float4 m_UnderWaterUV166 = ( ase_grabScreenPosNorm + float4( m_RefractionOffset162 , 0.0 ) );
				float4 break171 = m_UnderWaterUV166;
				float _CameraOthoSize86 = _CameraOrthoSize;
				float m_TileSizeYNormalized91 = ( 1.0 / ( _CameraOthoSize86 * 2.0 ) );
				float2 appendResult70 = (float2(break171.x , ( break171.y + ( m_TileSizeYNormalized91 * 0.4 ) )));
				float4 tex2DNode69 = tex2D( _TerrainTex, appendResult70 );
				float4 break168 = m_UnderWaterUV166;
				float2 appendResult97 = (float2(break168.x , ( break168.y + ( m_TileSizeYNormalized91 * 0.8 ) )));
				float4 lerpResult106 = lerp( tex2DNode69 , tex2D( _TerrainTex, appendResult97 ) , ( 1.0 - tex2DNode69.a ));
				float4 lerpResult131 = lerp( lerpResult106 , _UnderWaterColor , _UnderWaterColor.a);
				float4 break75 = lerpResult131;
				float clampResult137 = clamp( ( tex2D( _UnderWaterMaskTex, ase_grabScreenPosNorm.xy ).a * 2.0 ) , 0.0 , 1.0 );
				float saferPower133 = abs( clampResult137 );
				float m_UnderWaterMask130 = pow( saferPower133 , 1.4 );
				float4 appendResult76 = (float4(break75.r , break75.g , break75.b , ( break75.a * m_UnderWaterMask130 )));
				float4 m_UnderWaterColor74 = appendResult76;
				float4 lerpResult81 = lerp( m_WaterColor51 , m_UnderWaterColor74 , m_UnderWaterColor74.w);
				float4 _FoamColor59 = _FoamColor;
				float4 break195 = _FoamColor59;
				float _PixelSizeYNormalized224 = ( m_TileSizeYNormalized91 * 0.03125 );
				float4 appendResult13 = (float4(ase_grabScreenPosNorm.r , ( ase_grabScreenPosNorm.g + _PixelSizeYNormalized224 ) , 0.0 , 0.0));
				float m_ShoreEdgeAlpha175 = tex2D( _TerrainTex, appendResult13.xy ).a;
				float4 break316 = ase_grabScreenPosNorm;
				float4 appendResult317 = (float4(break316.x , ( break316.y + ( m_TileSizeYNormalized91 * 0.5 ) ) , 0.0 , 0.0));
				float saferPower328 = abs( tex2D( _UnderWaterMaskTex, appendResult317.xy ).a );
				float clampResult326 = clamp( ( pow( saferPower328 , 2.5 ) * 1.0 ) , 0.0 , 1.0 );
				float m_FoamMask319 = clampResult326;
				float2 appendResult205 = (float2(ase_positionWS.x , ase_positionWS.y));
				half2 pixelateduv236 = floor( appendResult205 * float2( 32.0, 32.0 ) + float2( 0,0 ) ) / float2( 32.0, 32.0 );
				float4 _FoamMoveParams243 = _FoamMoveParams;
				float4 break246 = _FoamMoveParams243;
				float2 appendResult247 = (float2(break246.x , break246.y));
				float2 normalizeResult248 = ASESafeNormalize( appendResult247 );
				float mulTime240 = _TimeParameters.x * break246.z;
				float2 m_FoamMoveOffset249 = ( normalizeResult248 * mulTime240 );
				float simpleNoise203 = SimpleNoise( (pixelateduv236*1.0 + _TimeParameters.x)*_FoamNoiseScale );
				float2 m_FoamUV207 = ( ( (pixelateduv236*float2( 1,2 ) + m_FoamMoveOffset249) + simpleNoise203 ) * ( _FoamScale * 0.1 ) );
				float temp_output_191_0 = ( m_FoamMask319 * tex2D( _FoamTex, m_FoamUV207 ).a );
				float m_FoamAlpha177 = ( step( 0.2 , temp_output_191_0 ) * temp_output_191_0 );
				float clampResult180 = clamp( ( m_ShoreEdgeAlpha175 + m_FoamAlpha177 ) , 0.0 , 1.0 );
				float4 appendResult194 = (float4(break195.r , break195.g , break195.b , ( break195.a * clampResult180 )));
				float4 m_FoamColor62 = appendResult194;
				float4 lerpResult64 = lerp( lerpResult81 , m_FoamColor62 , m_FoamColor62.w);
				float4 break338 = ( m_HighlightColor281 + lerpResult64 );
				float4 appendResult339 = (float4(break338.r , break338.g , break338.b , 1.0));
				
				float4 Color = appendResult339;

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

        	#define ASE_NEEDS_VERT_NORMAL
        	#define ASE_NEEDS_VERT_TANGENT


			sampler2D _TerrainBlurredTex;
			sampler2D _TerrainTex;
			sampler2D _UnderWaterMaskTex;
			sampler2D _FoamTex;
			CBUFFER_START( UnityPerMaterial )
			float4 _HighlightColor;
			float4 _DeepColor;
			float4 _ShallowColor;
			float4 _UnderWaterColor;
			float4 _FoamColor;
			float4 _FoamMoveParams;
			float _RefractionNoiseScale;
			float _RefractionIntensity;
			float _CameraOrthoSize;
			float _FoamNoiseScale;
			float _FoamScale;
			CBUFFER_END


            struct VertexInput
			{
				float3 positionOS : POSITION;
				float3 normal : NORMAL;
				float4 tangent : TANGENT;
				
				UNITY_SKINNED_VERTEX_INPUTS
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct VertexOutput
			{
				float4 positionCS : SV_POSITION;
				float4 ase_texcoord : TEXCOORD0;
				float4 ase_texcoord1 : TEXCOORD1;
				float4 ase_texcoord2 : TEXCOORD2;
				float4 ase_texcoord3 : TEXCOORD3;
				float4 ase_texcoord4 : TEXCOORD4;
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
			
			inline float4 ASE_ComputeGrabScreenPos( float4 pos )
			{
				#if UNITY_UV_STARTS_AT_TOP
				float scale = -1.0;
				#else
				float scale = 1.0;
				#endif
				float4 o = pos;
				o.y = pos.w * 0.5f;
				o.y = ( pos.y - o.y ) * _ProjectionParams.x * scale + o.y;
				return o;
			}
			
			inline float noise_randomValue (float2 uv) { return frac(sin(dot(uv, float2(12.9898, 78.233)))*43758.5453); }
			inline float noise_interpolate (float a, float b, float t) { return (1.0-t)*a + (t*b); }
			inline float valueNoise (float2 uv)
			{
				float2 i = floor(uv);
				float2 f = frac( uv );
				f = f* f * (3.0 - 2.0 * f);
				uv = abs( frac(uv) - 0.5);
				float2 c0 = i + float2( 0.0, 0.0 );
				float2 c1 = i + float2( 1.0, 0.0 );
				float2 c2 = i + float2( 0.0, 1.0 );
				float2 c3 = i + float2( 1.0, 1.0 );
				float r0 = noise_randomValue( c0 );
				float r1 = noise_randomValue( c1 );
				float r2 = noise_randomValue( c2 );
				float r3 = noise_randomValue( c3 );
				float bottomOfGrid = noise_interpolate( r0, r1, f.x );
				float topOfGrid = noise_interpolate( r2, r3, f.x );
				float t = noise_interpolate( bottomOfGrid, topOfGrid, f.y );
				return t;
			}
			
			float SimpleNoise(float2 UV)
			{
				float t = 0.0;
				float freq = pow( 2.0, float( 0 ) );
				float amp = pow( 0.5, float( 3 - 0 ) );
				t += valueNoise( UV/freq )*amp;
				freq = pow(2.0, float(1));
				amp = pow(0.5, float(3-1));
				t += valueNoise( UV/freq )*amp;
				freq = pow(2.0, float(2));
				amp = pow(0.5, float(3-2));
				t += valueNoise( UV/freq )*amp;
				return t;
			}
			
			inline float2 UnityVoronoiRandomVector( float2 UV, float offset )
			{
				float2x2 m = float2x2( 15.27, 47.63, 99.41, 89.98 );
				UV = frac( sin(mul(UV, m) ) * 46839.32 );
				return float2( sin(UV.y* +offset ) * 0.5 + 0.5, cos( UV.x* offset ) * 0.5 + 0.5 );
			}
			
			//x - Out y - Cells
			float3 UnityVoronoi( float2 UV, float AngleOffset, float CellDensity, inout float2 mr )
			{
				float2 g = floor( UV * CellDensity );
				float2 f = frac( UV * CellDensity );
				float3 res = float3( 8.0, 0.0, 0.0 );
			
				for( int y = -1; y <= 1; y++ )
				{
					for( int x = -1; x <= 1; x++ )
					{
						float2 lattice = float2( x, y );
						float2 offset = UnityVoronoiRandomVector( lattice + g, AngleOffset );
						float d = distance( lattice + offset, f );
			
						if( d < res.x )
						{
							mr = f - lattice - offset;
							res = float3( d, offset.x, offset.y );
						}
					}
				}
				return res;
			}
			
			float2 ASESafeNormalize(float2 inVec)
			{
				float dp3 = max(1.175494351e-38, dot(inVec, inVec));
				return inVec* rsqrt(dp3);
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
				o.ase_texcoord.xyz = ase_positionWS;
				float4 ase_positionCS = TransformObjectToHClip( ( v.positionOS ).xyz );
				float4 screenPos = ComputeScreenPos( ase_positionCS );
				o.ase_texcoord1 = screenPos;
				float3 ase_normalWS = TransformObjectToWorldNormal( v.normal );
				o.ase_texcoord2.xyz = ase_normalWS;
				float3 ase_tangentWS = TransformObjectToWorldDir( v.tangent.xyz );
				o.ase_texcoord3.xyz = ase_tangentWS;
				float ase_tangentSign = v.tangent.w * ( unity_WorldTransformParams.w >= 0.0 ? 1.0 : -1.0 );
				float3 ase_bitangentWS = cross( ase_normalWS, ase_tangentWS ) * ase_tangentSign;
				o.ase_texcoord4.xyz = ase_bitangentWS;
				
				
				//setting value to unused interpolator channels and avoid initialization warnings
				o.ase_texcoord.w = 0;
				o.ase_texcoord2.w = 0;
				o.ase_texcoord3.w = 0;
				o.ase_texcoord4.w = 0;
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
				float3 ase_positionWS = IN.ase_texcoord.xyz;
				float2 appendResult393 = (float2(ase_positionWS.x , ase_positionWS.y));
				half2 pixelateduv392 = floor( appendResult393 * float2( 32.0, 32.0 ) + float2( 0,0 ) ) / float2( 32.0, 32.0 );
				float mulTime358 = _TimeParameters.x * -0.01;
				float2 temp_output_374_0 = ( float2( 1,20 ) * 0.85 );
				float simplePerlin2D353 = snoise( ( (pixelateduv392*0.5 + mulTime358) * temp_output_374_0 )*2.0 );
				simplePerlin2D353 = simplePerlin2D353*0.5 + 0.5;
				float mulTime362 = _TimeParameters.x * 0.01;
				float simplePerlin2D363 = snoise( ( temp_output_374_0 * (pixelateduv392*0.5 + mulTime362) )*1.0 );
				simplePerlin2D363 = simplePerlin2D363*0.5 + 0.5;
				float saferPower389 = abs( ( simplePerlin2D353 * simplePerlin2D363 ) );
				float temp_output_389_0 = pow( saferPower389 , 6.5 );
				float4 m_HighlightColor281 = ( _HighlightColor * _HighlightColor.a * step( 0.3 , temp_output_389_0 ) * temp_output_389_0 );
				float4 screenPos = IN.ase_texcoord1;
				float4 ase_grabScreenPos = ASE_ComputeGrabScreenPos( screenPos );
				float4 ase_grabScreenPosNorm = ase_grabScreenPos / ase_grabScreenPos.w;
				float3 temp_output_111_0_g1 = ddx( ase_positionWS );
				float3 ase_normalWS = IN.ase_texcoord2.xyz;
				float3 temp_output_113_0_g1 = cross( ddy( ase_positionWS ) , ase_normalWS );
				float dotResult115_g1 = dot( temp_output_111_0_g1 , temp_output_113_0_g1 );
				float2 appendResult154 = (float2(ase_positionWS.x , ase_positionWS.y));
				float simpleNoise138 = SimpleNoise( (appendResult154*1.0 + _TimeParameters.x) );
				float _RefractionNoiseScale151 = _RefractionNoiseScale;
				float2 uv145 = 0;
				float3 unityVoronoy145 = UnityVoronoi(( (appendResult154*float2( 1,2.75 ) + 0.0) + simpleNoise138 ),( _TimeParameters.x * 2.0 ),_RefractionNoiseScale151,uv145);
				float temp_output_20_0_g1 = unityVoronoy145.x;
				float3 normalizeResult130_g1 = normalize( ( ( abs( dotResult115_g1 ) * ase_normalWS ) - ( 0.01 * float3( 0.05,0.05,0.05 ) * sign( dotResult115_g1 ) * ( ( ddx( temp_output_20_0_g1 ) * temp_output_113_0_g1 ) + ( ddy( temp_output_20_0_g1 ) * cross( ase_normalWS , temp_output_111_0_g1 ) ) ) ) ) );
				float3 ase_tangentWS = IN.ase_texcoord3.xyz;
				float3 ase_bitangentWS = IN.ase_texcoord4.xyz;
				float3x3 ase_worldToTangent = float3x3( ase_tangentWS, ase_bitangentWS, ase_normalWS );
				float3 worldToTangentDir42_g1 = mul( ase_worldToTangent, normalizeResult130_g1 );
				float3 m_RefractionOffset162 = ( worldToTangentDir42_g1 * _RefractionIntensity );
				float m_TerrainAlphaBlur10 = tex2D( _TerrainBlurredTex, ( ase_grabScreenPosNorm + float4( m_RefractionOffset162 , 0.0 ) ).xy ).a;
				float4 lerpResult49 = lerp( _DeepColor , _ShallowColor , m_TerrainAlphaBlur10);
				float4 m_WaterColor51 = lerpResult49;
				float4 m_UnderWaterUV166 = ( ase_grabScreenPosNorm + float4( m_RefractionOffset162 , 0.0 ) );
				float4 break171 = m_UnderWaterUV166;
				float _CameraOthoSize86 = _CameraOrthoSize;
				float m_TileSizeYNormalized91 = ( 1.0 / ( _CameraOthoSize86 * 2.0 ) );
				float2 appendResult70 = (float2(break171.x , ( break171.y + ( m_TileSizeYNormalized91 * 0.4 ) )));
				float4 tex2DNode69 = tex2D( _TerrainTex, appendResult70 );
				float4 break168 = m_UnderWaterUV166;
				float2 appendResult97 = (float2(break168.x , ( break168.y + ( m_TileSizeYNormalized91 * 0.8 ) )));
				float4 lerpResult106 = lerp( tex2DNode69 , tex2D( _TerrainTex, appendResult97 ) , ( 1.0 - tex2DNode69.a ));
				float4 lerpResult131 = lerp( lerpResult106 , _UnderWaterColor , _UnderWaterColor.a);
				float4 break75 = lerpResult131;
				float clampResult137 = clamp( ( tex2D( _UnderWaterMaskTex, ase_grabScreenPosNorm.xy ).a * 2.0 ) , 0.0 , 1.0 );
				float saferPower133 = abs( clampResult137 );
				float m_UnderWaterMask130 = pow( saferPower133 , 1.4 );
				float4 appendResult76 = (float4(break75.r , break75.g , break75.b , ( break75.a * m_UnderWaterMask130 )));
				float4 m_UnderWaterColor74 = appendResult76;
				float4 lerpResult81 = lerp( m_WaterColor51 , m_UnderWaterColor74 , m_UnderWaterColor74.w);
				float4 _FoamColor59 = _FoamColor;
				float4 break195 = _FoamColor59;
				float _PixelSizeYNormalized224 = ( m_TileSizeYNormalized91 * 0.03125 );
				float4 appendResult13 = (float4(ase_grabScreenPosNorm.r , ( ase_grabScreenPosNorm.g + _PixelSizeYNormalized224 ) , 0.0 , 0.0));
				float m_ShoreEdgeAlpha175 = tex2D( _TerrainTex, appendResult13.xy ).a;
				float4 break316 = ase_grabScreenPosNorm;
				float4 appendResult317 = (float4(break316.x , ( break316.y + ( m_TileSizeYNormalized91 * 0.5 ) ) , 0.0 , 0.0));
				float saferPower328 = abs( tex2D( _UnderWaterMaskTex, appendResult317.xy ).a );
				float clampResult326 = clamp( ( pow( saferPower328 , 2.5 ) * 1.0 ) , 0.0 , 1.0 );
				float m_FoamMask319 = clampResult326;
				float2 appendResult205 = (float2(ase_positionWS.x , ase_positionWS.y));
				half2 pixelateduv236 = floor( appendResult205 * float2( 32.0, 32.0 ) + float2( 0,0 ) ) / float2( 32.0, 32.0 );
				float4 _FoamMoveParams243 = _FoamMoveParams;
				float4 break246 = _FoamMoveParams243;
				float2 appendResult247 = (float2(break246.x , break246.y));
				float2 normalizeResult248 = ASESafeNormalize( appendResult247 );
				float mulTime240 = _TimeParameters.x * break246.z;
				float2 m_FoamMoveOffset249 = ( normalizeResult248 * mulTime240 );
				float simpleNoise203 = SimpleNoise( (pixelateduv236*1.0 + _TimeParameters.x)*_FoamNoiseScale );
				float2 m_FoamUV207 = ( ( (pixelateduv236*float2( 1,2 ) + m_FoamMoveOffset249) + simpleNoise203 ) * ( _FoamScale * 0.1 ) );
				float temp_output_191_0 = ( m_FoamMask319 * tex2D( _FoamTex, m_FoamUV207 ).a );
				float m_FoamAlpha177 = ( step( 0.2 , temp_output_191_0 ) * temp_output_191_0 );
				float clampResult180 = clamp( ( m_ShoreEdgeAlpha175 + m_FoamAlpha177 ) , 0.0 , 1.0 );
				float4 appendResult194 = (float4(break195.r , break195.g , break195.b , ( break195.a * clampResult180 )));
				float4 m_FoamColor62 = appendResult194;
				float4 lerpResult64 = lerp( lerpResult81 , m_FoamColor62 , m_FoamColor62.w);
				float4 break338 = ( m_HighlightColor281 + lerpResult64 );
				float4 appendResult339 = (float4(break338.r , break338.g , break338.b , 1.0));
				
				float4 Color = appendResult339;
				half4 outColor = unity_SelectionID;
				return outColor;
			}

            ENDHLSL
        }
		
	}
	
	FallBack "Hidden/Shader Graph/FallbackError"
	
	Fallback "Hidden/InternalErrorShader"
}
/*ASEBEGIN
Version=19905
Node;AmplifyShaderEditor.CommentaryNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;87;-6127.528,-4058.984;Inherit;False;4763.217;1423.746;Comment;29;91;151;148;216;86;215;85;59;90;89;58;217;219;220;221;222;223;224;225;226;227;228;231;243;245;376;377;378;379;Params;1,1,1,1;0;0
Node;AmplifyShaderEditor.Vector4Node, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;245;-6064,-3344;Inherit;False;Property;_FoamMoveParams;Foam Move Params;14;0;Create;True;0;0;0;False;0;False;1,1,1,0;0.5,-1,0.3,0;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.CommentaryNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;134;-6112,-2448;Inherit;False;5003.717;2060.292;Comment;56;205;204;207;203;201;199;206;202;200;183;208;192;191;177;172;62;197;176;178;179;180;194;61;195;175;55;135;15;13;12;210;212;213;236;238;240;244;246;247;248;249;250;260;261;267;312;313;314;316;317;318;319;321;326;327;328;Foam Color;1,1,1,1;0;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;243;-5800.224,-3354.307;Inherit;False;_FoamMoveParams;-1;True;1;0;FLOAT4;0,0,0,0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;244;-5952,-1360;Inherit;False;243;_FoamMoveParams;1;0;OBJECT;;False;1;FLOAT4;0
Node;AmplifyShaderEditor.BreakToComponentsNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;246;-5712,-1344;Inherit;False;FLOAT4;1;0;FLOAT4;0,0,0,0;False;16;FLOAT;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT;5;FLOAT;6;FLOAT;7;FLOAT;8;FLOAT;9;FLOAT;10;FLOAT;11;FLOAT;12;FLOAT;13;FLOAT;14;FLOAT;15
Node;AmplifyShaderEditor.DynamicAppendNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;247;-5536,-1408;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.NormalizeNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;248;-5392,-1408;Inherit;False;True;1;0;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;314;-4528,-2096;Inherit;False;91;m_TileSizeYNormalized;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleTimeNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;240;-5552,-1216;Inherit;False;1;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.GrabScreenPosition, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;313;-4624,-2368;Inherit;False;0;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.WorldPosInputsNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;204;-6016,-1008;Inherit;False;0;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;238;-5232,-1344;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;321;-4208,-2112;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0.5;False;1;FLOAT;0
Node;AmplifyShaderEditor.BreakToComponentsNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;316;-4352,-2352;Inherit;True;FLOAT4;1;0;FLOAT4;0,0,0,0;False;16;FLOAT;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT;5;FLOAT;6;FLOAT;7;FLOAT;8;FLOAT;9;FLOAT;10;FLOAT;11;FLOAT;12;FLOAT;13;FLOAT;14;FLOAT;15
Node;AmplifyShaderEditor.DynamicAppendNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;205;-5824,-992;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;249;-5056,-1344;Inherit;False;m_FoamMoveOffset;-1;True;1;0;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleTimeNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;201;-5696,-672;Inherit;False;1;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;318;-4064,-2224;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.CommentaryNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;150;-6037.061,2314.732;Inherit;False;3165.792;1500.984;Comment;16;140;152;145;141;138;147;146;142;144;139;143;154;159;160;161;162;Refraction Offset;1,1,1,1;0;0
Node;AmplifyShaderEditor.TFHCPixelate, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;236;-5648,-992;Inherit;False;4;0;FLOAT2;0,0;False;1;FLOAT;32;False;2;FLOAT;32;False;3;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;250;-5360,-832;Inherit;False;249;m_FoamMoveOffset;1;0;OBJECT;;False;1;FLOAT2;0
Node;AmplifyShaderEditor.ScaleAndOffsetNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;200;-5440,-720;Inherit;False;3;0;FLOAT2;0,0;False;1;FLOAT;1;False;2;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.Vector2Node, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;199;-5344,-976;Inherit;False;Constant;_Vector1;Vector 0;8;0;Create;True;0;0;0;False;0;False;1,2;0,0;0;3;FLOAT2;0;FLOAT;1;FLOAT;2
Node;AmplifyShaderEditor.DynamicAppendNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;317;-3872,-2304;Inherit;False;FLOAT4;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;213;-5424,-592;Inherit;False;Property;_FoamNoiseScale;Foam Noise Scale;13;0;Create;True;0;0;0;False;0;False;0;2;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;85;-6096,-3760;Inherit;False;Property;_CameraOrthoSize;Camera Ortho Size;4;1;[HideInInspector];Create;True;0;0;0;False;0;False;0;7.056077;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.WorldPosInputsNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;140;-5781.465,2411.609;Inherit;False;0;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.SimpleTimeNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;141;-5300.617,2756.606;Inherit;False;1;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;183;-4832,-720;Inherit;False;Property;_FoamScale;Foam Scale;12;0;Create;True;0;0;0;False;0;False;0;1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.ScaleAndOffsetNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;202;-5088,-992;Inherit;False;3;0;FLOAT2;0,0;False;1;FLOAT2;1,0;False;2;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SamplerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;312;-3728,-2336;Inherit;True;Property;_UnderWaterMaskTex1;Under Water Mask Tex;5;0;Fetch;True;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Instance;116;Auto;Texture2D;False;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.NoiseGeneratorNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;203;-5216,-688;Inherit;True;Simple;True;False;2;0;FLOAT2;0,0;False;1;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;86;-5837.222,-3755.178;Inherit;False;_CameraOthoSize;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.DynamicAppendNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;154;-5589.194,2441.568;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;148;-6072.088,-3504.435;Inherit;False;Property;_RefractionNoiseScale;Refraction Noise Scale;9;0;Create;True;0;0;0;False;0;False;0;1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.Vector2Node, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;143;-5086.544,2499.665;Inherit;False;Constant;_Vector0;Vector 0;8;0;Create;True;0;0;0;False;0;False;1,2.75;0,0;0;3;FLOAT2;0;FLOAT;1;FLOAT;2
Node;AmplifyShaderEditor.ScaleAndOffsetNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;139;-5112.261,2643.285;Inherit;False;3;0;FLOAT2;0,0;False;1;FLOAT;1;False;2;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleAddOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;206;-4768,-992;Inherit;True;2;2;0;FLOAT2;0,0;False;1;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;212;-4640,-736;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0.1;False;1;FLOAT;0
Node;AmplifyShaderEditor.PowerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;328;-3392,-2224;Inherit;False;True;2;0;FLOAT;0;False;1;FLOAT;2.5;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;89;-5584.813,-3754.314;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;2;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleTimeNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;146;-4599.422,2817.787;Inherit;False;1;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;151;-5825.229,-3502.801;Inherit;False;_RefractionNoiseScale;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.ScaleAndOffsetNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;142;-4847.506,2425.545;Inherit;False;3;0;FLOAT2;0,0;False;1;FLOAT2;1,0;False;2;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.NoiseGeneratorNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;138;-4883.412,2732.528;Inherit;True;Simple;True;False;2;0;FLOAT2;0,0;False;1;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.CommentaryNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;84;-6024.855,-44.33174;Inherit;False;3007.577;2080.566;Comment;33;171;169;167;168;166;165;164;163;122;137;129;125;116;117;100;99;133;130;131;93;92;75;74;83;76;106;107;95;69;98;97;72;70;Under Water Color;1,1,1,1;0;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;222;-6096,-2976;Inherit;False;91;m_TileSizeYNormalized;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;210;-4512,-992;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;327;-3248,-2224;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleDivideOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;90;-5422.042,-3755.398;Inherit;False;2;0;FLOAT;1;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;147;-4427.325,2817.997;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;2;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;144;-4601.058,2573.787;Inherit;True;2;2;0;FLOAT2;0,0;False;1;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;152;-4528,2944;Inherit;False;151;_RefractionNoiseScale;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;165;-5921.28,1417.593;Inherit;False;162;m_RefractionOffset;1;0;OBJECT;;False;1;FLOAT3;0
Node;AmplifyShaderEditor.GrabScreenPosition, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;163;-5924.016,1202.657;Inherit;False;0;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;223;-5781.125,-2980.968;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0.03125;False;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;207;-4352,-992;Inherit;False;m_FoamUV;-1;True;1;0;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.ClampOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;326;-3088,-2224;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;91;-5264,-3744;Inherit;False;m_TileSizeYNormalized;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.VoronoiNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;145;-4213.513,2747.865;Inherit;True;0;0;1;0;1;False;1;True;False;False;4;0;FLOAT2;0,0;False;1;FLOAT;0;False;2;FLOAT;1;False;3;FLOAT;0;False;3;FLOAT;0;FLOAT2;1;FLOAT2;2
Node;AmplifyShaderEditor.SimpleAddOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;164;-5637.733,1290.507;Inherit;False;2;2;0;FLOAT4;0,0,0,0;False;1;FLOAT3;0,0,0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;224;-5594.38,-2973.058;Inherit;False;_PixelSizeYNormalized;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;208;-4000,-1520;Inherit;False;207;m_FoamUV;1;0;OBJECT;;False;1;FLOAT2;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;319;-2928,-2224;Inherit;False;m_FoamMask;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.FunctionNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;159;-3974.95,3047.376;Inherit;True;Normal From Height;-1;;1;1942fe2c5f1a1f94881a33d532e4afeb;0;2;20;FLOAT;0;False;110;FLOAT;0.01;False;2;FLOAT3;40;FLOAT3;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;161;-3954.838,3353.906;Inherit;False;Property;_RefractionIntensity;Refraction Intensity;11;0;Create;True;0;0;0;False;0;False;0;7;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;166;-5220.608,1292.958;Inherit;False;m_UnderWaterUV;-1;True;1;0;FLOAT4;0,0,0,0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;92;-5779.423,273.6111;Inherit;False;91;m_TileSizeYNormalized;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;99;-5594.277,799.7927;Inherit;False;91;m_TileSizeYNormalized;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;135;-6080,-2144;Inherit;False;224;_PixelSizeYNormalized;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;192;-3664,-1696;Inherit;False;319;m_FoamMask;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;172;-3792,-1552;Inherit;True;Property;_FoamTex;Foam Tex;3;0;Create;True;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;False;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.CommentaryNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;352;-6260.587,4330.161;Inherit;False;4021.435;2260.351;Comment;45;369;358;367;353;357;355;361;366;362;371;364;363;360;281;303;294;310;296;276;297;298;307;306;305;304;279;300;283;301;284;289;299;293;291;292;286;372;373;374;375;382;389;391;392;393;Highlight;1,1,1,1;0;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;160;-3637.773,3207.694;Inherit;False;2;2;0;FLOAT3;0,0,0;False;1;FLOAT;0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;169;-5757.895,92.65213;Inherit;False;166;m_UnderWaterUV;1;0;OBJECT;;False;1;FLOAT4;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;93;-5465.646,274.7411;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0.4;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;167;-5721.27,568.9236;Inherit;False;166;m_UnderWaterUV;1;0;OBJECT;;False;1;FLOAT4;0
Node;AmplifyShaderEditor.GrabScreenPosition, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;12;-6064,-2368;Inherit;False;0;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;100;-5331.846,789.5881;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0.8;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;15;-5776,-2240;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0.01;False;1;FLOAT;0
Node;AmplifyShaderEditor.GrabScreenPosition, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;117;-4987.274,958.015;Inherit;False;0;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;191;-3408,-1664;Inherit;False;2;2;0;FLOAT;1;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.WorldPosInputsNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;391;-5434.405,5933.041;Inherit;False;0;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;162;-3468.431,3209.99;Inherit;False;m_RefractionOffset;-1;True;1;0;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.BreakToComponentsNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;171;-5444.686,99.27269;Inherit;False;FLOAT4;1;0;FLOAT4;0,0,0,0;False;16;FLOAT;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT;5;FLOAT;6;FLOAT;7;FLOAT;8;FLOAT;9;FLOAT;10;FLOAT;11;FLOAT;12;FLOAT;13;FLOAT;14;FLOAT;15
Node;AmplifyShaderEditor.SimpleAddOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;72;-5186.399,187.703;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0.1;False;1;FLOAT;0
Node;AmplifyShaderEditor.BreakToComponentsNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;168;-5487.123,577.8992;Inherit;False;FLOAT4;1;0;FLOAT4;0,0,0,0;False;16;FLOAT;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT;5;FLOAT;6;FLOAT;7;FLOAT;8;FLOAT;9;FLOAT;10;FLOAT;11;FLOAT;12;FLOAT;13;FLOAT;14;FLOAT;15
Node;AmplifyShaderEditor.SimpleAddOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;98;-5152.091,648.9301;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0.1;False;1;FLOAT;0
Node;AmplifyShaderEditor.DynamicAppendNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;13;-5632,-2304;Inherit;False;FLOAT4;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;129;-4610.791,1166.135;Inherit;False;Constant;_Float0;Float 0;10;0;Create;True;0;0;0;False;0;False;2;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;116;-4723.53,938.0374;Inherit;True;Property;_UnderWaterMaskTex;Under Water Mask Tex;5;0;Create;True;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;False;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.StepOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;260;-3232,-1760;Inherit;False;2;0;FLOAT;0.2;False;1;FLOAT;0.3;False;1;FLOAT;0
Node;AmplifyShaderEditor.CommentaryNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;268;-2593.846,2352.885;Inherit;False;1736.609;1437.952;Comment;10;48;50;47;51;49;10;53;341;342;343;Water Color;1,1,1,1;0;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;366;-5002.074,6274.854;Inherit;False;Constant;_Float4;Float 3;23;0;Create;True;0;0;0;False;0;False;0.01;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.DynamicAppendNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;393;-5226.474,5955.356;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;382;-4997.873,5716.653;Inherit;False;Constant;_Float6;Float 3;23;0;Create;True;0;0;0;False;0;False;-0.01;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.DynamicAppendNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;70;-5062.648,87.77451;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.DynamicAppendNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;97;-4997.34,564.0009;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SamplerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;55;-5440,-2336;Inherit;True;Property;_TerrainTex;Terrain Tex;10;0;Create;True;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;False;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;125;-4408.948,1034.772;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;261;-3072,-1680;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.GrabScreenPosition, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;342;-2529.144,2465.491;Inherit;False;0;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;343;-2528.823,2649.886;Inherit;False;162;m_RefractionOffset;1;0;OBJECT;;False;1;FLOAT3;0
Node;AmplifyShaderEditor.SimpleTimeNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;362;-4827.023,6283.524;Inherit;False;1;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleTimeNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;358;-4816.34,5713.612;Inherit;False;1;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.TFHCPixelate, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;392;-5053.1,5939.247;Inherit;False;4;0;FLOAT2;0,0;False;1;FLOAT;32;False;2;FLOAT;32;False;3;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.Vector2Node, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;369;-4675.716,5860.244;Inherit;False;Constant;_Vector2;Vector 2;23;0;Create;True;0;0;0;False;0;False;1,20;0,0;0;3;FLOAT2;0;FLOAT;1;FLOAT;2
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;375;-4675.043,5996.433;Inherit;False;Constant;_Float5;Float 5;23;0;Create;True;0;0;0;False;0;False;0.85;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;69;-4822.454,83.82516;Inherit;True;Property;_TerrainTex1;Terrain Tex;10;0;Create;True;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Instance;55;Auto;Texture2D;False;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.OneMinusNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;107;-4508.837,392.0445;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;95;-4811.909,553.7621;Inherit;True;Property;_TerrainTex2;Terrain Tex;10;0;Create;True;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Instance;55;Auto;Texture2D;False;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;175;-5136,-2224;Inherit;False;m_ShoreEdgeAlpha;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.ClampOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;137;-4263.849,1035.852;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;177;-2928,-1680;Inherit;False;m_FoamAlpha;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;341;-2235.011,2559.43;Inherit;False;2;2;0;FLOAT4;0,0,0,0;False;1;FLOAT3;0,0,0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.ScaleAndOffsetNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;360;-4577.706,6179.856;Inherit;False;3;0;FLOAT2;0,0;False;1;FLOAT;0.5;False;2;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.ScaleAndOffsetNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;355;-4578.477,5628.937;Inherit;False;3;0;FLOAT2;0,0;False;1;FLOAT;0.5;False;2;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;374;-4480,5920;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.LerpOp, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;106;-4332.727,346.2124;Inherit;False;3;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;2;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.ColorNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;58;-6065.008,-4008.983;Inherit;False;Property;_FoamColor;Foam Color;2;0;Create;True;0;0;0;False;0;False;0,0,0,0;0.6734603,0.8867924,0.8867924,0.5882353;True;True;0;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.ColorNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;122;-4369.292,511.0796;Inherit;False;Property;_UnderWaterColor;Under Water Color;7;0;Create;True;0;0;0;False;0;False;0,0,0,0;0.3725491,1,0.9803625,0.1686275;True;True;0;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.PowerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;133;-4106.109,1031.969;Inherit;False;True;2;0;FLOAT;0;False;1;FLOAT;1.4;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;176;-3888,-1056;Inherit;False;175;m_ShoreEdgeAlpha;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;178;-3872,-976;Inherit;False;177;m_FoamAlpha;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;53;-1952,2512;Inherit;True;Property;_TerrainBlurredTex;Terrain Blurred Tex;8;0;Create;True;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;False;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;371;-4358.03,6103.126;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;367;-4366.916,5676.356;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;357;-4178.244,5715.816;Inherit;False;Constant;_Float1;Float 1;23;0;Create;True;0;0;0;False;0;False;2;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;361;-4224.265,6224.582;Inherit;False;Constant;_Float2;Float 1;23;0;Create;True;0;0;0;False;0;False;1;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;59;-5774.757,-4001.042;Inherit;False;_FoamColor;-1;True;1;0;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.LerpOp, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;131;-4095.708,402.358;Inherit;False;3;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;2;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;130;-3936.736,1029.218;Inherit;False;m_UnderWaterMask;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;179;-3648,-1024;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;10;-1599.833,2574.929;Inherit;False;m_TerrainAlphaBlur;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.NoiseGeneratorNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;353;-4028.288,5557.981;Inherit;False;Simplex2D;True;False;2;0;FLOAT2;0,0;False;1;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.NoiseGeneratorNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;363;-4037.682,6145.996;Inherit;False;Simplex2D;True;False;2;0;FLOAT2;0,0;False;1;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;61;-3472,-1200;Inherit;False;59;_FoamColor;1;0;OBJECT;;False;1;COLOR;0
Node;AmplifyShaderEditor.BreakToComponentsNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;75;-3765.94,393.6132;Inherit;False;COLOR;1;0;COLOR;0,0,0,0;False;16;FLOAT;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT;5;FLOAT;6;FLOAT;7;FLOAT;8;FLOAT;9;FLOAT;10;FLOAT;11;FLOAT;12;FLOAT;13;FLOAT;14;FLOAT;15
Node;AmplifyShaderEditor.ClampOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;180;-3440,-1024;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;83;-3632.805,550.2073;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;364;-3806.034,5709.663;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;50;-2429.965,3464.465;Inherit;False;10;m_TerrainAlphaBlur;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.ColorNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;48;-2432,3248;Inherit;False;Property;_ShallowColor;Shallow Color;1;0;Create;True;0;0;0;False;0;False;0,0,0,0;0.3333333,0.509804,0.4698643,1;True;True;0;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.ColorNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;47;-2430.193,3058.711;Inherit;False;Property;_DeepColor;Deep Color;0;0;Create;True;0;0;0;False;0;False;0,0,0,0;0.1960784,0.3529412,0.3921569,1;True;True;0;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.BreakToComponentsNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;195;-3280,-1184;Inherit;False;COLOR;1;0;COLOR;0,0,0,0;False;16;FLOAT;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT;5;FLOAT;6;FLOAT;7;FLOAT;8;FLOAT;9;FLOAT;10;FLOAT;11;FLOAT;12;FLOAT;13;FLOAT;14;FLOAT;15
Node;AmplifyShaderEditor.DynamicAppendNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;76;-3488.81,454.2067;Inherit;False;FLOAT4;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;197;-3168,-992;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.PowerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;389;-3672.832,5706.527;Inherit;False;True;2;0;FLOAT;0;False;1;FLOAT;6.5;False;1;FLOAT;0
Node;AmplifyShaderEditor.LerpOp, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;49;-2128.259,3237.969;Inherit;True;3;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;2;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.DynamicAppendNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;194;-3008,-1088;Inherit;False;FLOAT4;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;74;-3301.993,478.84;Inherit;False;m_UnderWaterColor;-1;True;1;0;FLOAT4;0,0,0,0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.StepOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;372;-3503.629,5582.557;Inherit;False;2;0;FLOAT;0.3;False;1;FLOAT;0.2;False;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;51;-1853.43,3232.003;Inherit;False;m_WaterColor;-1;True;1;0;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.ColorNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;310;-3616,5360;Inherit;False;Property;_HighlightColor;Highlight Color;22;0;Create;True;0;0;0;False;0;False;0.4576806,0.5109627,0.5188679,1;0,0,0,0;True;True;0;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;62;-2848,-1072;Inherit;False;m_FoamColor;-1;True;1;0;FLOAT4;0,0,0,0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;80;-561.7582,-249.5212;Inherit;False;74;m_UnderWaterColor;1;0;OBJECT;;False;1;FLOAT4;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;373;-3305.969,5557.472;Inherit;False;4;4;0;COLOR;0,0,0,0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.BreakToComponentsNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;82;-300.8017,-170.739;Inherit;False;FLOAT4;1;0;FLOAT4;0,0,0,0;False;16;FLOAT;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT;5;FLOAT;6;FLOAT;7;FLOAT;8;FLOAT;9;FLOAT;10;FLOAT;11;FLOAT;12;FLOAT;13;FLOAT;14;FLOAT;15
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;11;-529.528,-350.4601;Inherit;False;51;m_WaterColor;1;0;OBJECT;;False;1;COLOR;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;57;-329.2193,2.334095;Inherit;False;62;m_FoamColor;1;0;OBJECT;;False;1;FLOAT4;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;281;-2966.715,5551.89;Inherit;False;m_HighlightColor;-1;True;1;0;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.BreakToComponentsNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;198;-114.4763,111.4316;Inherit;False;FLOAT4;1;0;FLOAT4;0,0,0,0;False;16;FLOAT;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT;5;FLOAT;6;FLOAT;7;FLOAT;8;FLOAT;9;FLOAT;10;FLOAT;11;FLOAT;12;FLOAT;13;FLOAT;14;FLOAT;15
Node;AmplifyShaderEditor.LerpOp, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;81;-148.5866,-285.2456;Inherit;False;3;0;FLOAT4;0,0,0,0;False;1;FLOAT4;0,0,0,0;False;2;FLOAT;0.5;False;1;FLOAT4;0
Node;AmplifyShaderEditor.LerpOp, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;64;23.44168,-53.90888;Inherit;False;3;0;FLOAT4;0,0,0,0;False;1;FLOAT4;0,0,0,0;False;2;FLOAT;0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;282;48,-208;Inherit;False;281;m_HighlightColor;1;0;OBJECT;;False;1;COLOR;0
Node;AmplifyShaderEditor.SimpleAddOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;311;246.6957,-120.4266;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;FLOAT4;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.BreakToComponentsNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;338;406.9925,-105.498;Inherit;False;COLOR;1;0;COLOR;0,0,0,0;False;16;FLOAT;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT;5;FLOAT;6;FLOAT;7;FLOAT;8;FLOAT;9;FLOAT;10;FLOAT;11;FLOAT;12;FLOAT;13;FLOAT;14;FLOAT;15
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;216;-5849.616,-3635.526;Inherit;False;_CameraAspect;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleDivideOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;225;-5308.566,-3144.437;Inherit;False;2;0;FLOAT;1;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;221;-5595.282,-3128.485;Inherit;False;_PixelSizeXNormalized;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;267;-4864,-560;Inherit;False;m_FoamNoise;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleDivideOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;227;-5310.393,-2989.399;Inherit;False;2;0;FLOAT;1;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;220;-5779.527,-3122.748;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0.03125;False;1;FLOAT;0
Node;AmplifyShaderEditor.DynamicAppendNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;339;540.8914,-101.5976;Inherit;False;FLOAT4;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;1;False;1;FLOAT4;0
Node;AmplifyShaderEditor.DotProductOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;286;-3563.886,4564.32;Inherit;False;2;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;291;-3436.112,4570.501;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.BlendNormalsNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;299;-4206.377,4696.157;Inherit;False;0;3;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.Vector3Node, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;289;-3760.726,4687.571;Inherit;False;Property;_LightDir;Light Dir;19;0;Create;True;0;0;0;False;0;False;0.5,1,1;0.5,1,1;0;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;284;-5427.521,4457.163;Inherit;False;Property;_WaveScale1;Wave Scale 1;17;0;Create;True;0;0;0;False;0;False;1;1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;301;-5458.146,4861.744;Inherit;False;Property;_WaveScale2;Wave Scale 2;18;0;Create;True;0;0;0;False;0;False;1;1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;283;-5201.292,4602.188;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;300;-5196.479,4810.039;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;305;-5498.723,4685.491;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.PannerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;298;-5000.32,4845.293;Inherit;False;3;0;FLOAT2;0,0;False;2;FLOAT2;-0.01,0.05;False;1;FLOAT;0.5;False;1;FLOAT2;0
Node;AmplifyShaderEditor.PannerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;297;-4988.375,4645.079;Inherit;False;3;0;FLOAT2;0,0;False;2;FLOAT2;0.03,-0.1;False;1;FLOAT;1;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SamplerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;276;-4636,4544.994;Inherit;True;Property;_WaveNormal1;WaveNormal1;15;0;Create;True;0;0;0;False;0;False;-1;None;None;True;0;False;bump;Auto;True;Object;-1;Auto;Texture2D;False;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.SamplerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;296;-4627.101,4784.15;Inherit;True;Property;_WaveNormal2;Wave Normal 2;16;0;Create;True;0;0;0;False;0;False;-1;None;None;True;0;False;bump;Auto;True;Object;-1;Auto;Texture2D;False;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;293;-3541.403,4754.474;Inherit;False;Property;_WaveSpecularPower;Wave Specular Power;20;0;Create;True;0;0;0;False;0;False;1;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.StepOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;294;-3140.75,4562.577;Inherit;False;2;0;FLOAT;0.3;False;1;FLOAT;0.2;False;1;FLOAT;0
Node;AmplifyShaderEditor.DynamicAppendNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;376;-5844.689,-2792.605;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.WorldPosInputsNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;378;-6051.853,-2820.53;Inherit;False;0;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.TFHCPixelate, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;377;-5676.008,-2798.44;Inherit;False;4;0;FLOAT2;0,0;False;1;FLOAT;32;False;2;FLOAT;32;False;3;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;228;-5117.571,-2987.01;Inherit;False;_PixelCountY;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;379;-5445.382,-2790.969;Inherit;False;m_WorldPosPixelated;-1;True;1;0;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.PowerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;292;-3307.857,4663.783;Inherit;False;False;2;0;FLOAT;0;False;1;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;307;-2897.338,4524.019;Inherit;False;3;3;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.Vector2Node, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;306;-5668.931,4775.521;Inherit;False;Property;_WaveRatio;Wave Ratio;21;0;Create;True;0;0;0;False;0;False;1,1;1,1;0;3;FLOAT2;0;FLOAT;1;FLOAT;2
Node;AmplifyShaderEditor.WorldPosInputsNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;279;-6210.587,4563.605;Inherit;False;0;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.TFHCPixelate, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;304;-5731.812,4584.579;Inherit;False;4;0;FLOAT2;0,0;False;1;FLOAT;32;False;2;FLOAT;32;False;3;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.DynamicAppendNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;303;-6002.656,4585.921;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;217;-6080,-3120;Inherit;False;219;m_TileSizeXNormalized;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;226;-5120,-3136;Inherit;False;_PixelCountX;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;219;-4784,-3648;Inherit;False;m_TileSizeXNormalized;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleDivideOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;231;-4960,-3648;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;215;-6096,-3632;Inherit;False;Property;_CameraAspect;Camera Aspect;6;1;[HideInInspector];Create;True;0;0;0;False;0;False;0;2.109162;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;2;0,0;Float;False;False;-1;2;AmplifyShaderEditor.MaterialInspector;0;16;New Amplify Shader;199187dac283dbe4a8cb1ea611d70c58;True;Sprite Normal;0;1;Sprite Normal;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;2;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;5;RenderPipeline=UniversalPipeline;RenderType=Transparent=RenderType;Queue=Transparent=Queue=0;UniversalMaterialType=Lit;ShaderGraphShader=true;True;0;True;14;all;0;False;True;2;5;False;;10;False;;3;1;False;;10;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;True;True;True;True;0;False;;False;False;False;False;False;False;False;True;False;0;True;;255;False;;255;False;;7;False;;3;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;True;2;False;;True;3;False;;True;True;0;False;;0;False;;True;1;LightMode=NormalsRendering;False;False;0;Hidden/InternalErrorShader;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;3;0,0;Float;False;False;-1;2;AmplifyShaderEditor.MaterialInspector;0;16;New Amplify Shader;199187dac283dbe4a8cb1ea611d70c58;True;Sprite Forward;0;2;Sprite Forward;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;2;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;5;RenderPipeline=UniversalPipeline;RenderType=Transparent=RenderType;Queue=Transparent=Queue=0;UniversalMaterialType=Lit;ShaderGraphShader=true;True;0;True;14;all;0;False;True;2;5;False;;10;False;;3;1;False;;10;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;True;True;True;True;0;False;;False;False;False;False;False;False;False;True;False;0;True;;255;False;;255;False;;7;False;;3;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;True;2;False;;True;3;False;;True;True;0;False;;0;False;;True;1;LightMode=UniversalForward;False;False;0;Hidden/InternalErrorShader;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;4;0,0;Float;False;False;-1;2;AmplifyShaderEditor.MaterialInspector;0;16;New Amplify Shader;199187dac283dbe4a8cb1ea611d70c58;True;SceneSelectionPass;0;3;SceneSelectionPass;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;2;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;5;RenderPipeline=UniversalPipeline;RenderType=Transparent=RenderType;Queue=Transparent=Queue=0;UniversalMaterialType=Lit;ShaderGraphShader=true;True;0;True;14;all;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;2;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;1;LightMode=SceneSelectionPass;False;False;0;Hidden/InternalErrorShader;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;5;0,0;Float;False;False;-1;2;AmplifyShaderEditor.MaterialInspector;0;16;New Amplify Shader;199187dac283dbe4a8cb1ea611d70c58;True;ScenePickingPass;0;4;ScenePickingPass;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;2;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;5;RenderPipeline=UniversalPipeline;RenderType=Transparent=RenderType;Queue=Transparent=Queue=0;UniversalMaterialType=Lit;ShaderGraphShader=true;True;0;True;14;all;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;2;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;1;LightMode=Picking;False;False;0;Hidden/InternalErrorShader;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;1;700.4026,-102.7066;Float;False;True;-1;2;;0;18;Cainos/Pixel Art Top Down - Village/Top Down Pixel Water;199187dac283dbe4a8cb1ea611d70c58;True;Sprite Lit;0;0;Sprite Lit;6;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;2;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;5;RenderPipeline=UniversalPipeline;RenderType=Transparent=RenderType;Queue=Transparent=Queue=0;UniversalMaterialType=Lit;ShaderGraphShader=true;True;0;True;14;all;0;False;True;2;5;False;;10;False;;3;1;False;;10;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;True;True;True;True;0;False;;False;False;False;False;False;False;True;True;False;0;True;_StencilReference;255;False;;255;False;;7;False;;3;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;True;2;False;;True;3;False;;True;True;0;False;;0;False;;True;1;LightMode=Universal2D;False;False;0;Hidden/InternalErrorShader;0;0;Standard;3;Vertex Position;1;0;Debug Display;0;0;External Alpha;0;0;0;5;True;True;True;True;True;False;;False;0
WireConnection;243;0;245;0
WireConnection;246;0;244;0
WireConnection;247;0;246;0
WireConnection;247;1;246;1
WireConnection;248;0;247;0
WireConnection;240;0;246;2
WireConnection;238;0;248;0
WireConnection;238;1;240;0
WireConnection;321;0;314;0
WireConnection;316;0;313;0
WireConnection;205;0;204;1
WireConnection;205;1;204;2
WireConnection;249;0;238;0
WireConnection;318;0;316;1
WireConnection;318;1;321;0
WireConnection;236;0;205;0
WireConnection;200;0;236;0
WireConnection;200;2;201;0
WireConnection;317;0;316;0
WireConnection;317;1;318;0
WireConnection;202;0;236;0
WireConnection;202;1;199;0
WireConnection;202;2;250;0
WireConnection;312;1;317;0
WireConnection;203;0;200;0
WireConnection;203;1;213;0
WireConnection;86;0;85;0
WireConnection;154;0;140;1
WireConnection;154;1;140;2
WireConnection;139;0;154;0
WireConnection;139;2;141;0
WireConnection;206;0;202;0
WireConnection;206;1;203;0
WireConnection;212;0;183;0
WireConnection;328;0;312;4
WireConnection;89;0;86;0
WireConnection;151;0;148;0
WireConnection;142;0;154;0
WireConnection;142;1;143;0
WireConnection;138;0;139;0
WireConnection;210;0;206;0
WireConnection;210;1;212;0
WireConnection;327;0;328;0
WireConnection;90;1;89;0
WireConnection;147;0;146;0
WireConnection;144;0;142;0
WireConnection;144;1;138;0
WireConnection;223;0;222;0
WireConnection;207;0;210;0
WireConnection;326;0;327;0
WireConnection;91;0;90;0
WireConnection;145;0;144;0
WireConnection;145;1;147;0
WireConnection;145;2;152;0
WireConnection;164;0;163;0
WireConnection;164;1;165;0
WireConnection;224;0;223;0
WireConnection;319;0;326;0
WireConnection;159;20;145;0
WireConnection;166;0;164;0
WireConnection;172;1;208;0
WireConnection;160;0;159;40
WireConnection;160;1;161;0
WireConnection;93;0;92;0
WireConnection;100;0;99;0
WireConnection;15;0;12;2
WireConnection;15;1;135;0
WireConnection;191;0;192;0
WireConnection;191;1;172;4
WireConnection;162;0;160;0
WireConnection;171;0;169;0
WireConnection;72;0;171;1
WireConnection;72;1;93;0
WireConnection;168;0;167;0
WireConnection;98;0;168;1
WireConnection;98;1;100;0
WireConnection;13;0;12;1
WireConnection;13;1;15;0
WireConnection;116;1;117;0
WireConnection;260;1;191;0
WireConnection;393;0;391;1
WireConnection;393;1;391;2
WireConnection;70;0;171;0
WireConnection;70;1;72;0
WireConnection;97;0;168;0
WireConnection;97;1;98;0
WireConnection;55;1;13;0
WireConnection;125;0;116;4
WireConnection;125;1;129;0
WireConnection;261;0;260;0
WireConnection;261;1;191;0
WireConnection;362;0;366;0
WireConnection;358;0;382;0
WireConnection;392;0;393;0
WireConnection;69;1;70;0
WireConnection;107;0;69;4
WireConnection;95;1;97;0
WireConnection;175;0;55;4
WireConnection;137;0;125;0
WireConnection;177;0;261;0
WireConnection;341;0;342;0
WireConnection;341;1;343;0
WireConnection;360;0;392;0
WireConnection;360;2;362;0
WireConnection;355;0;392;0
WireConnection;355;2;358;0
WireConnection;374;0;369;0
WireConnection;374;1;375;0
WireConnection;106;0;69;0
WireConnection;106;1;95;0
WireConnection;106;2;107;0
WireConnection;133;0;137;0
WireConnection;53;1;341;0
WireConnection;371;0;374;0
WireConnection;371;1;360;0
WireConnection;367;0;355;0
WireConnection;367;1;374;0
WireConnection;59;0;58;0
WireConnection;131;0;106;0
WireConnection;131;1;122;0
WireConnection;131;2;122;4
WireConnection;130;0;133;0
WireConnection;179;0;176;0
WireConnection;179;1;178;0
WireConnection;10;0;53;4
WireConnection;353;0;367;0
WireConnection;353;1;357;0
WireConnection;363;0;371;0
WireConnection;363;1;361;0
WireConnection;75;0;131;0
WireConnection;180;0;179;0
WireConnection;83;0;75;3
WireConnection;83;1;130;0
WireConnection;364;0;353;0
WireConnection;364;1;363;0
WireConnection;195;0;61;0
WireConnection;76;0;75;0
WireConnection;76;1;75;1
WireConnection;76;2;75;2
WireConnection;76;3;83;0
WireConnection;197;0;195;3
WireConnection;197;1;180;0
WireConnection;389;0;364;0
WireConnection;49;0;47;0
WireConnection;49;1;48;0
WireConnection;49;2;50;0
WireConnection;194;0;195;0
WireConnection;194;1;195;1
WireConnection;194;2;195;2
WireConnection;194;3;197;0
WireConnection;74;0;76;0
WireConnection;372;1;389;0
WireConnection;51;0;49;0
WireConnection;62;0;194;0
WireConnection;373;0;310;0
WireConnection;373;1;310;4
WireConnection;373;2;372;0
WireConnection;373;3;389;0
WireConnection;82;0;80;0
WireConnection;281;0;373;0
WireConnection;198;0;57;0
WireConnection;81;0;11;0
WireConnection;81;1;80;0
WireConnection;81;2;82;3
WireConnection;64;0;81;0
WireConnection;64;1;57;0
WireConnection;64;2;198;3
WireConnection;311;0;282;0
WireConnection;311;1;64;0
WireConnection;338;0;311;0
WireConnection;216;0;215;0
WireConnection;225;1;221;0
WireConnection;221;0;220;0
WireConnection;267;0;203;0
WireConnection;227;1;224;0
WireConnection;220;0;217;0
WireConnection;339;0;338;0
WireConnection;339;1;338;1
WireConnection;339;2;338;2
WireConnection;286;0;299;0
WireConnection;286;1;289;0
WireConnection;291;0;286;0
WireConnection;299;0;276;0
WireConnection;299;1;296;0
WireConnection;283;0;284;0
WireConnection;283;1;305;0
WireConnection;300;0;305;0
WireConnection;300;1;301;0
WireConnection;305;0;304;0
WireConnection;305;1;306;0
WireConnection;298;0;300;0
WireConnection;297;0;283;0
WireConnection;276;1;297;0
WireConnection;296;1;298;0
WireConnection;294;1;292;0
WireConnection;376;0;378;1
WireConnection;376;1;378;2
WireConnection;377;0;376;0
WireConnection;228;0;227;0
WireConnection;379;0;377;0
WireConnection;292;0;291;0
WireConnection;292;1;293;0
WireConnection;307;1;294;0
WireConnection;307;2;292;0
WireConnection;304;0;303;0
WireConnection;303;0;279;2
WireConnection;303;1;279;1
WireConnection;226;0;225;0
WireConnection;219;0;231;0
WireConnection;231;0;91;0
WireConnection;231;1;216;0
WireConnection;1;1;339;0
ASEEND*/
//CHKSM=5F91FC8A4F680ACF3AD7A1A35C968C85ABFDC7CA