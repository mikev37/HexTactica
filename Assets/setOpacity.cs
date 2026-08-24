using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class setOpacity : MonoBehaviour
{
    public Color offsetColor;
    public float alpha, offset, transition_speed;
    private float alpha_actual;
    SpriteRenderer render;
    Unit unit;
    // Start is called before the first frame update
    void Start()
    {
        render = GetComponent<SpriteRenderer>();
        unit = GetComponentInParent<Unit>();
        alpha_actual = render.color.a;
    }

    // Update is called once per frame
    void Update()
    {
        float inverse = 1f - offset;
        alpha_actual = Mathf.Lerp(alpha_actual, alpha, Time.deltaTime*transition_speed);
        Color pc = unit.owner.displayColor;
        Color c = new Color(pc.r*inverse + offsetColor.r*offset, pc.g*inverse + offsetColor.g*offset, pc.b*inverse + offsetColor.b * offset, alpha_actual);
        render.color = c;
    }
}
