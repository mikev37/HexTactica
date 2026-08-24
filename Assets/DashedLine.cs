using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DashedLine : MonoBehaviour
{
    //[RequireComponent(System.Type<LineRenderer>)]
    public LineRenderer lr;
    public float offset;
    public float width;
    public int num;
    // Start is called before the first frame update
    void Start()
    {
        lr = GetComponent<LineRenderer>();
    }

    // Update is called once per frame
    void Update()
    {
        Gradient grad = new Gradient();

        GradientAlphaKey[] alphas = new GradientAlphaKey[num*2];
        for(int i = 0; i < num; i++) {
            GradientAlphaKey color = new GradientAlphaKey();
            color.alpha = 1f;
            color.time = 1 / (num * 2) * i;
            alphas[i * 2] = color;
            GradientAlphaKey zero = new GradientAlphaKey();
            zero.alpha = 0f;
            zero.time = 1 / (num * 2) * (i+1);
            alphas[i * 2 + 1] = zero;
        }
        grad.alphaKeys = alphas;
        lr.colorGradient = grad;
    }
}
