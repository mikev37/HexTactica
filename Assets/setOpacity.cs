using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class setOpacity : MonoBehaviour
{
    public Color offsetColor;
    public float alpha, offset, transition_speed;
    [Tooltip("1: the colour as set; 0: fully grey (set by HealthDot for suppressed pips).")]
    [Range(0, 1)] public float saturation = 1f;
    private float saturation_actual = 1f;
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
        // Desaturate toward the colour's own brightness.
        saturation_actual = Mathf.Lerp(saturation_actual, saturation, Time.deltaTime * transition_speed);
        float grey = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
        c = new Color(Mathf.Lerp(grey, c.r, saturation_actual), Mathf.Lerp(grey, c.g, saturation_actual), Mathf.Lerp(grey, c.b, saturation_actual), c.a);
        render.color = c;
    }
}
