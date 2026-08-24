Shader "Unlit/BlurShader"
{
	Properties
	{
	   [PerRendererData]  _MainTex("Texture", 2D) = "white" {}
	   _BlurSize("Blur Size", Float) = 1.0
			_Color("Color", Color) = (1,1,1,1)
	_MainTex("Normalmap", 2D) = "bump" {}
  _Magnitude("Magnitude", Range(0,1)) = 0.05
	}
		SubShader
	   {
		   Tags {"Queue" = "Transparent" "RenderType" = "Transparent" }
		   LOD 100
		   ZWrite Off
		   Blend SrcAlpha OneMinusSrcAlpha

		   Pass
		   {
			   CGPROGRAM
			   #pragma vertex vert
			   #pragma fragment frag
			   // make fog work
			   #pragma multi_compile_fog

			   #include "UnityCG.cginc"

			   struct appdata
			   {
				   float4 vertex : POSITION;
				   float2 uv : TEXCOORD0;
			   };

			   struct v2f
			   {
				   float4 pos : SV_POSITION;
				   half2 uv[5]: TEXCOORD0;
			   };

			   struct vertexOutput {
				   float4 vertex : SV_POSITION;
				   float4 uvgrab : TEXCOORD0;
				   float2 uvmain : TEXCOORD2;
				   UNITY_FOG_COORDS(3)
			   };

			   sampler2D _MainTex;
			   float4 _MainTex_ST;
			   half4 _MainTex_TexelSize;
			   float _BlurSize;

			   v2f vert(appdata v)
			   {
				   v2f o;
				   o.pos = UnityObjectToClipPos(v.vertex);

				   //o.uv = TRANSFORM_TEX(v.uv, _MainTex);
				   half2 uv = v.uv;

				   o.uv[0] = uv;
				   o.uv[1] = uv + float2(_MainTex_TexelSize.x * 1.0, 0.0) * _BlurSize;
				   o.uv[2] = uv - float2(_MainTex_TexelSize.x * 1.0, 0.0) * _BlurSize;
				   o.uv[3] = uv + float2(_MainTex_TexelSize.x * 2.0, 0.0) * _BlurSize;
				   o.uv[4] = uv - float2(_MainTex_TexelSize.x * 2.0, 0.0) * _BlurSize;

				   return o;
			   }

			   fixed4 frag(v2f i) : SV_Target {
				   float weight[3] = {0.4026, 0.2442, 0.0545};

				   fixed3 sum = tex2D(_MainTex, i.uv[0]).rgb * weight[0];
				   fixed  a = tex2D(_MainTex, i.uv[0]).a;

				   for (int it = 1; it < 3; it++) {
					   sum += tex2D(_MainTex, i.uv[it * 2 - 1]).rgb * weight[it];
					   sum += tex2D(_MainTex, i.uv[it * 2]).rgb * weight[it];
				   }

				   return fixed4(sum, a);


			   }

			   ENDCG
		   }
	   }
}