using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

public class HealthDot : MonoBehaviour
{
    Unit unit;
    setOpacity opac;
    setSize size;
    public int iter;
    [Tooltip("Saturation of a pip that is suppressed (0 grey, 1 full colour).")]
    [Range(0, 1)] public float suppressedSaturation = 0.15f;
    // Start is called before the first frame update
    void Start()
    {
        unit = GetComponentInParent<Unit>();
        opac = GetComponent<setOpacity>();
        size = GetComponent<setSize>();
        try {
            iter = Int32.Parse(Regex.Match(gameObject.name, @"\d+").Value);
        } catch(FormatException e) {
            iter = 0;
        }
    }

    // Update is called once per frame
    void Update()
    {
        if(unit.Strength > iter) {
            opac.alpha = 1;
            size.sizeF = .5f;
            // The top pips are the suppressed ones.
            opac.saturation = iter >= unit.Strength - unit.suppressed ? suppressedSaturation : 1f;
        } else {
            opac.alpha = 0;
            size.sizeF = 1;
        }
    }
}
