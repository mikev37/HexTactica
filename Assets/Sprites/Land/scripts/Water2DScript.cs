// 2016 - Damien Mayance (@Valryon)
// Source: https://github.com/valryon/water2d-unity/
using UnityEngine;

/// <summary>
/// Water surface script (update the shader properties).
/// </summary>
public class Water2DScript : MonoBehaviour
{
  public Vector2 speed = new Vector2(0.01f, 0f);
	Vector3 pos;
	private Renderer rend;
  private Material mat;
	float rando;
	void Awake()
  {
		pos = transform.position;
		rend = GetComponent<Renderer>();
		mat = rend.material;
		rando = transform.localPosition.x % 1;
		//mat.mainTextureScale = Vector2.one * 3;
		
	}

  void LateUpdate()
  {
	//Vector2 scroll = (Mathf.PingPong(Time.time+rando, 2)-1) * speed;// Time.deltaTime * speed;
		Vector2 scroll = Time.deltaTime * speed;

		//mat.mainTextureOffset += scroll;
		mat.SetTextureOffset("_BumpTex", mat.GetTextureOffset("_BumpTex")+scroll);
		//mat.mainTextureOffset =  Vector2.zero;
  }
}