using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class setSize : MonoBehaviour
{

    public float sizeF;
    Vector2 size = Vector2.one;
    public float speed;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        size = Vector2.one * sizeF;
        transform.localScale = Vector2.Lerp(transform.localScale, size, speed*Time.fixedDeltaTime);
    }
}
