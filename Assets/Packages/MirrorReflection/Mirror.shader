// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

// refrence http://wiki.unity3d.com/index.php/MirrorReflection4

Shader "MirrorReflection/Mirror"
{
	Properties
	{
		_Color("Color", Color) = (0,0,0,0)
		[HideInInspector] _ReflectionTex("", 2D) = "white" {}
	}
		SubShader
	{
		Tags { "RenderType" = "Opaque" }
		LOD 100

		Pass {
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			struct v2f
			{
				float4 refl : TEXCOORD0;
				float4 pos : SV_POSITION;
			};

			v2f vert(float4 pos : POSITION, float2 uv : TEXCOORD0)
			{
				v2f o;
				o.pos = UnityObjectToClipPos(pos);
				o.refl = ComputeScreenPos(o.pos);
				return o;
			}

			float4 _Color;
			sampler2D _ReflectionTex;

			fixed4 frag(v2f i) : SV_Target
			{
				fixed4 refl = tex2Dproj(_ReflectionTex, UNITY_PROJ_COORD(i.refl));
				return refl * (_Color * _Color.a);
			}
			ENDCG
		}
	}
}