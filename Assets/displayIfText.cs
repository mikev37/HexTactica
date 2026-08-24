using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class displayIfText : MonoBehaviour
{
    Image i;
    Text t;
    Color imageColor;
    Color textColor;
    float speed = 5;
    // Start is called before the first frame update
    void Start()
    {
        i = GetComponent<Image>();
        t = GetComponentInChildren<Text>();
        imageColor = i.color;
        textColor = t.color;
    }

    // Update is called once per frame
    void Update()
    {
        if(t.text.Length > 0) {
            float a = Mathf.Lerp(imageColor.a, 1, Time.deltaTime*speed);
            imageColor.a = a;
            i.color = imageColor;
            textColor.a = a;
            t.color = textColor;
        } else {
            float a = Mathf.Lerp(imageColor.a, 0, Time.deltaTime*speed);
            imageColor.a = a;
            i.color = imageColor;
            textColor.a = a;
            t.color = textColor;
        }

    }
}
