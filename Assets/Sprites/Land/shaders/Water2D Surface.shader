// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

Shader "Custom/Water2D Surface" {
Properties {
  _Color("Color", Color) = (1,1,1,1)
	[PerRendererData] _MainTex("Sprite Texture", 2D) = "white" {}
	_BumpTex ("Normalmap", 2D) = "bump" {}
  _Magnitude("Magnitude", Range(0,1)) = 0.05

  [PerRendererData] _AlphaTex("External Alpha", 2D) = "white" {}
  [PerRendererData] _EnableExternalAlpha("Enable External Alpha", Float) = 0
	 _RendererColor("RendererColor", Color) = (1,1,1,1)
   _Flip("Flip", Vector) = (1,1,1,1)
}

Category {

	Tags { "Queue"="Transparent" }

	SubShader {

		// This pass grabs the screen behind the object into a texture.
		// We can access the result in the next pass as _BackgroundTex
		GrabPass { "_BackgroundTex" }
		
		// Main pass: Take the texture grabbed above and use the bumpmap to perturb it
		// on to the screen
		Pass {
			Name "BASE"
			
      CGPROGRAM
      #pragma vertex vert
      #pragma fragment frag 
      #include "UnityCG.cginc"

      struct vertexInput {
	      float4 vertex : POSITION;
	      float2 texcoord: TEXCOORD0;
      };

      struct vertexOutput {
	      float4 vertex : SV_POSITION;
	      float4 uvgrab : TEXCOORD0;
		    float2 uvmain : TEXCOORD3;
				float2 ucmain : TEXCOORD2;
	      UNITY_FOG_COORDS(4)
      };

      fixed4 _Color;
      sampler2D _BumpTex;
	    sampler2D _MainTex;
      float4 _BumpTex_ST;
			float4 _MainTex_ST;
      float _BumpAmt;
      float4 _BumpMap_ST;
      float  _Magnitude;

      vertexOutput vert (vertexInput v)
      {
	      vertexOutput o;
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.ucmain = TRANSFORM_TEX( v.texcoord, _MainTex );
				
	      #if UNITY_UV_STARTS_AT_TOP
	      float scale = -1.0;
	      #else
	      float scale = 1.0;
	      #endif
	      o.uvgrab.xy = (float2(o.vertex.x, o.vertex.y*scale) + o.vertex.w) * 0.5;
	      o.uvgrab.zw = o.vertex.zw;
	      o.uvmain = TRANSFORM_TEX( v.texcoord, _BumpTex );
		    
				UNITY_TRANSFER_FOG(o,o.vertex);
	      return o;
      }

      sampler2D _BackgroundTex;
      float4 _GrabTexture_TexelSize;
      sampler2D _BumpMap;



      half4 frag (vertexOutput i) : SV_Target
      {
	      // Calculate perturbed coordinates
        half4 bump = tex2D(_BumpTex, i.uvmain);
				half4 color = tex2D(_MainTex, i.ucmain);//tex2Dproj(_MainTex, UNITY_PROJ_COORD(i.uvgrab));
        half2 distortion = UnpackNormal(bump).rg;
        i.uvgrab.xy += distortion * _Magnitude;
	
        // Get pixel in GrabTexture, rendered in previous pass
	      half4 col = tex2Dproj(_BackgroundTex, UNITY_PROJ_COORD(i.uvgrab));
				
				col += color;

        // Apply color
	      col *= _Color;

	      return col;
      }
      ENDCG
		}
	}
}

}
