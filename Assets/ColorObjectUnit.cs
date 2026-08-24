using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ColorObjectUnit : MonoBehaviour
{
    Unit unit;
    float alpha;
    SpriteRenderer render;
    // Start is called before the first frame update
    void Start()
    {
        render = GetComponent<SpriteRenderer>();
        unit = GetComponentInParent<Unit>();
        Color c = render.color;
        alpha = c.a;
    }

    // Update is called once per frame
    void Update()
    {
        Color pc = unit.owner.displayColor;
        Color c = new Color(pc.r, pc.g, pc.b, alpha);
        render.color = c;
    }
}
