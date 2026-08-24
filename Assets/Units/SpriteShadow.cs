using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SpriteShadow : MonoBehaviour
{
    public Sprite sprite;
    public Color color = Color.black;
    public float distance;
    public int renderQueue;

    private GameObject shadowGo;
    private GameObject southGo, northGo, eastGo, westGo;
    private SpriteRenderer southR, northR, eastR, westR;

    // Start is called before the first frame update
    void Start()
    {
        if(!sprite)
            sprite = GetComponent<SpriteRenderer>().sprite;
        shadowGo = new GameObject("Shadow");
        shadowGo.transform.localScale = transform.localScale;
        shadowGo.transform.position = transform.position;
        shadowGo.transform.parent = transform;
        southGo = new GameObject("ShadowSouthObj");
        southGo.transform.localScale = transform.localScale;
        southGo.transform.position = shadowGo.transform.position;
        southGo.transform.parent = shadowGo.transform;
        southGo.transform.position += (Vector3)Vector2.down * distance;
        southR = southGo.AddComponent<SpriteRenderer>();
        southR.sprite = sprite;
        southR.color = color;
        southR.rendererPriority = renderQueue;
        northGo = new GameObject("ShadowNorthObj");
        northGo.transform.localScale = transform.localScale;
        northGo.transform.position = shadowGo.transform.position;
        northGo.transform.parent = shadowGo.transform;
        northGo.transform.position += (Vector3)Vector2.up * distance;
        northR = northGo.AddComponent<SpriteRenderer>();
        northR.sprite = sprite;
        northR.color = color;
        northR.rendererPriority = renderQueue;
        westGo = new GameObject("ShadowWestObj");
        westGo.transform.localScale = transform.localScale;
        westGo.transform.position = shadowGo.transform.position;
        westGo.transform.parent = shadowGo.transform;
        westGo.transform.position += (Vector3)Vector2.left * distance;
        westR = westGo.AddComponent<SpriteRenderer>();
        westR.sprite = sprite;
        westR.color = color;
        westR.rendererPriority = renderQueue;
        eastGo = new GameObject("ShadowEastObj");
        eastGo.transform.localScale = transform.localScale;
        eastGo.transform.position = shadowGo.transform.position;
        eastGo.transform.parent = shadowGo.transform;
        eastGo.transform.position += (Vector3)Vector2.right * distance;
        eastR = eastGo.AddComponent<SpriteRenderer>();
        eastR.sprite = sprite;
        eastR.color = color;
        eastR.rendererPriority = renderQueue;

    }

    // Update is called once per frame
    void Update()
    {

        southGo.transform.position = shadowGo.transform.position + (Vector3)Vector2.down * distance;
        southR.sprite = sprite;
        southR.color = color;
        southR.rendererPriority = renderQueue;
        southR.sortingOrder = renderQueue;
        northGo.transform.position = shadowGo.transform.position + (Vector3)Vector2.up * distance;
        northR.sprite = sprite;
        northR.color = color;
        northR.rendererPriority = renderQueue;
        northR.sortingOrder = renderQueue;
        westGo.transform.position = shadowGo.transform.position + (Vector3)Vector2.left * distance;
        westR.sprite = sprite;
        westR.color = color;
        westR.rendererPriority = renderQueue;
        westR.sortingOrder = renderQueue;
        eastGo.transform.position = shadowGo.transform.position + (Vector3)Vector2.right * distance;
        eastR.sprite = sprite;
        eastR.color = color;
        eastR.rendererPriority = renderQueue;
        eastR.sortingOrder = renderQueue;
    }
}
