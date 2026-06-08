Shader "PCDLib/VertexColor Quad ConstSize"
{
	Properties
	{
		_PointSize("Point Pixel Size", Range(0.1, 30.0)) = 2
	}

	CGINCLUDE

		#pragma vertex vert
		#pragma fragment frag
		#include "UnityCG.cginc" 

		float _PointSize;

		struct appdata
		{
			float3 vertex	: POSITION;
			fixed4 color	: COLOR0;
		};

	ENDCG


	SubShader
	{
		Pass
		{
			Tags{ "RenderType" = "Opaque" }
			LOD 200

			CGPROGRAM

			#pragma target 4.0
			#pragma geometry geom

			struct geom_input
			{
				float4	pos		: POSITION;
				fixed4	color	: COLOR0;
			};

			struct frag_input
			{
				float4	pos		: POSITION;
				fixed4	color	: COLOR0;
			};

			geom_input vert(appdata v)
			{
				geom_input output;
				output.pos = UnityObjectToClipPos(v.vertex);
				output.color = v.color;
				return output;
			}

			[maxvertexcount(4)]
			void geom(point geom_input p[1], inout TriangleStream<frag_input> triStream)
			{
				float4 pos = p[0].pos;

				frag_input input;
				input.color = p[0].color;

				float s = _PointSize * pos.w;
				float w = s * (_ScreenParams.z - 1.0f);
				float h = s * (_ScreenParams.w - 1.0f);

#if defined(SHADER_API_D3D11) || defined(SHADER_API_VULKAN)

				input.pos = pos + float4(w, -h, 0.0f, 0.0f);
				triStream.Append(input);

				input.pos = pos + float4(w, h, 0.0f, 0.0f);
				triStream.Append(input);

				input.pos = pos + float4(-w, -h, 0.0f, 0.0f);
				triStream.Append(input);

				input.pos = pos + float4(-w, h, 0.0f, 0.0f);
				triStream.Append(input);

#else
				input.pos = pos + float4(w, h, 0.0f, 0.0f);
				triStream.Append(input);

				input.pos = pos + float4(w, -h, 0.0f, 0.0f);
				triStream.Append(input);

				input.pos = pos + float4(-w, h, 0.0f, 0.0f);
				triStream.Append(input);

				input.pos = pos + float4(-w, -h, 0.0f, 0.0f);
				triStream.Append(input);
#endif
			}

			fixed4 frag(frag_input input) : COLOR
			{
				return input.color;
			}

			ENDCG
		}
	}

	SubShader
	{
		Pass
		{
			Tags{ "RenderType" = "Opaque" }
			LOD 200

			CGPROGRAM

			struct frag_input
			{
				float4	pos		: SV_POSITION;
				fixed4	color	: COLOR0;
				float	size	: PSIZE;
			};

			frag_input vert(appdata v)
			{
				frag_input output;
				output.pos = UnityObjectToClipPos(v.vertex);
				output.color = v.color;
				output.size = _PointSize;
				return output;
			}

			fixed4 frag(frag_input input) : COLOR
			{
				return input.color;
			}

			ENDCG
		}
	}
}