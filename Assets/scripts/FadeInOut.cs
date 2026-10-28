using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class FadeInOut : MonoBehaviour
{

    public float maxAlpha, minAlpha, speed;
    public Image image;
    public SpriteRenderer sr;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (image)
        {
            Color c = image.color;
            c.a = minAlpha + (maxAlpha - minAlpha) * (Mathf.Sin(Time.realtimeSinceStartup * speed)+1)/2;
            image.color = c;
        }
        if (sr)
        {
            Color c = sr.color;
            c.a = minAlpha + (maxAlpha - minAlpha) * (Mathf.Sin(Time.realtimeSinceStartup * speed)+1)/2;
            sr.color = c;
        }
    }
}
