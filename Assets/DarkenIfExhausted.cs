using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DarkenIfExhausted : MonoBehaviour
{
    SpriteRenderer render;
    Unit unit;
    // Start is called before the first frame update
    void Start()
    {
        render = GetComponent<SpriteRenderer>();
        unit = GetComponentInParent<Unit>();
    }

    // Update is called once per frame
    void Update()
    {
        bool exhausted = !unit.active && !unit.attacking && unit.path.Count == 0;
        if (!exhausted) {
            render.color = Color.white;
        } else {
            render.color = Color.gray;
        }
    }
}
